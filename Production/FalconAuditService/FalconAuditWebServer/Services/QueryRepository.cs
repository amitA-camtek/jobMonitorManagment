namespace FalconAuditWebServer.Services;

using FalconAuditService;
using FalconAuditWebServer.Models;
using Microsoft.Data.Sqlite;

/// <summary>
/// Read-only accessor for the audit DBs. Opens a FRESH read-only SQLite
/// connection per call (no caching) so the audit service never holds a
/// long-lived handle on `audit.db`. Before any read, the per-job
/// <see cref="AuditEventQueue"/> is force-flushed so pending in-memory events
/// land in `audit.db` — every API response is fully current with zero data
/// loss.
/// </summary>
public class QueryRepository
{
    private readonly JobDiscoveryService _discovery;
    private readonly ShardRegistry       _shards;
    private readonly ILogger<QueryRepository> _logger;

    public QueryRepository(JobDiscoveryService discovery, ShardRegistry shards,
                           ILogger<QueryRepository> logger)
    {
        _discovery = discovery;
        _shards    = shards;
        _logger    = logger;
    }

    // ── flush-then-query helper ─────────────────────────────────────────────

    /// <summary>
    /// Drain any pending in-memory events for the job to disk. Returns
    /// immediately if there is no live queue (job not loaded by the FSW worker)
    /// or the queue is empty.
    /// </summary>
    private async Task FlushAsync(string jobName)
    {
        if (_shards.TryGet(jobName, out var queue) && queue is not null)
            await queue.FlushAsync();
    }

    private static SqliteConnection OpenRead(string dbPath)
    {
        var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False");
        conn.Open();
        using var p = conn.CreateCommand();
        p.CommandText = "PRAGMA busy_timeout=3000;";
        p.ExecuteNonQuery();
        return conn;
    }

    // ── read API ────────────────────────────────────────────────────────────

    public async Task<List<JobSummary>> ListJobsAsync()
    {
        // Drain every loaded job's queue first so the listing is fully current.
        await _shards.FlushAllAsync();

        var result = new List<JobSummary>();
        foreach (var job in _discovery.KnownJobs)
        {
            var shardPath = _discovery.ShardPath(job);
            if (shardPath is null) continue;
            try
            {
                using var conn = OpenRead(shardPath);

                long    count    = 0;
                string  first    = "";
                string  last     = "";
                string  machines = "";

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*), MIN(changed_at), MAX(changed_at), GROUP_CONCAT(DISTINCT machine_name) FROM audit_log";
                    using var r = cmd.ExecuteReader();
                    if (r.Read())
                    {
                        count    = r.IsDBNull(0) ? 0  : r.GetInt64(0);
                        first    = r.IsDBNull(1) ? "" : r.GetString(1);
                        last     = r.IsDBNull(2) ? "" : r.GetString(2);
                        machines = r.IsDBNull(3) ? "" : r.GetString(3);
                    }
                }

                string? origin = null;
                string? jobCreatedAt = null;
                try
                {
                    using var mc = conn.CreateCommand();
                    mc.CommandText = "SELECT key, value FROM monitor_config WHERE key IN ('job_origin') " +
                                     "UNION ALL " +
                                     "SELECT key, value FROM schema_meta WHERE key = 'created_at_utc'";
                    using var mr = mc.ExecuteReader();
                    while (mr.Read())
                    {
                        var key = mr.GetString(0);
                        var val = mr.GetString(1);
                        if (key == "job_origin")     origin       = val;
                        if (key == "created_at_utc") jobCreatedAt = val;
                    }
                }
                catch (SqliteException) { /* legacy shard without these tables */ }

                result.Add(new JobSummary
                {
                    JobName        = job,
                    ShardPath      = shardPath,
                    TotalEvents    = count,
                    FirstEvent     = first,
                    LastEvent      = last,
                    Machines       = machines,
                    ShardSizeBytes = new FileInfo(shardPath).Length,
                    Origin         = origin,
                    JobCreatedAt   = jobCreatedAt
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "QueryRepository: stats failed for {J}", job);
            }
        }
        return result;
    }

    public async Task<(List<AuditEventSummary> Items, long Total)> GetEventsAsync(string jobName, EventFilter f)
    {
        await FlushAsync(jobName);
        var shardPath = _discovery.ShardPath(jobName);
        if (shardPath is null) return (new(), 0);
        return GetEventsFromDb(shardPath, f);
    }

    private static (List<AuditEventSummary> Items, long Total) GetEventsFromDb(string dbPath, EventFilter f)
    {
        if (!File.Exists(dbPath)) return (new(), 0);

        using var conn = OpenRead(dbPath);
        var where  = BuildWhere(f);
        var order  = f.Sort == "asc" ? "ASC" : "DESC";
        int offset = (f.Page - 1) * f.PageSize;
        if (offset < 0) offset = 0;

        long total = 0;
        var items = new List<AuditEventSummary>();

        using (var cnt = conn.CreateCommand())
        {
            cnt.CommandText = $"SELECT COUNT(*) FROM audit_log WHERE {where}";
            BindFilter(cnt, f);
            total = (long)(cnt.ExecuteScalar() ?? 0L);
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"SELECT id,changed_at,event_type,filepath,rel_filepath,module,
            owner_service,monitor_priority,machine_name,sha256_hash,file_description,change_summary,is_backfill,diff_text,login_user,setup_name,recipe_name,file_era
            FROM audit_log WHERE {where} ORDER BY changed_at {order} LIMIT @ps OFFSET @off";
        BindFilter(cmd, f);
        cmd.Parameters.AddWithValue("@ps",  f.PageSize);
        cmd.Parameters.AddWithValue("@off", offset);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            items.Add(new AuditEventSummary
            {
                Id=r.GetInt64(0), ChangedAt=r.GetString(1), EventType=r.GetString(2),
                Filepath=r.GetString(3), RelFilepath=r.GetString(4), Module=r.GetString(5),
                OwnerService=r.GetString(6), MonitorPriority=r.GetString(7), MachineName=r.GetString(8),
                Sha256Hash=r.GetString(9),
                FileDescription=r.IsDBNull(10)?"":r.GetString(10),
                ChangeSummary=r.IsDBNull(11)?"":r.GetString(11),
                IsBackfill=!r.IsDBNull(12) && r.GetInt32(12)==1,
                DiffText=r.IsDBNull(13)?null:r.GetString(13),
                LoginUser=r.IsDBNull(14)?null:r.GetString(14),
                Setup=r.IsDBNull(15)?null:r.GetString(15),
                Recipe=r.IsDBNull(16)?null:r.GetString(16),
                FileEra=r.IsDBNull(17)?null:r.GetString(17)
            });
        return (items, total);
    }

    public async Task<AuditEventDetail?> GetEventAsync(string jobName, long id)
    {
        await FlushAsync(jobName);
        var shardPath = _discovery.ShardPath(jobName);
        if (shardPath is null) return null;
        using var conn = OpenRead(shardPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT id,changed_at,event_type,filepath,rel_filepath,module,
            owner_service,monitor_priority,machine_name,sha256_hash,file_description,change_summary,
            old_content,diff_text,old_filepath,is_backfill
            FROM audit_log WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new AuditEventDetail
        {
            Id=r.GetInt64(0), ChangedAt=r.GetString(1), EventType=r.GetString(2),
            Filepath=r.GetString(3), RelFilepath=r.GetString(4), Module=r.GetString(5),
            OwnerService=r.GetString(6), MonitorPriority=r.GetString(7), MachineName=r.GetString(8),
            Sha256Hash=r.GetString(9),
            FileDescription=r.IsDBNull(10)?"":r.GetString(10),
            ChangeSummary=r.IsDBNull(11)?"":r.GetString(11),
            OldContent=r.IsDBNull(12)?null:r.GetString(12),
            DiffText=r.IsDBNull(13)?null:r.GetString(13),
            OldFilepath=r.IsDBNull(14)?null:r.GetString(14),
            IsBackfill=!r.IsDBNull(15) && r.GetInt32(15)==1
        };
    }

    public async Task<List<FileHistoryItem>> GetFileHistoryAsync(string jobName, string relFilepath,
        int limit = 500, int offset = 0)
    {
        await FlushAsync(jobName);
        var shardPath = _discovery.ShardPath(jobName);
        if (shardPath is null) return new();
        var result = new List<FileHistoryItem>();
        try
        {
            using var conn = OpenRead(shardPath);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT id,changed_at,event_type,machine_name,sha256_hash,
                old_content,diff_text,is_backfill
                FROM audit_log WHERE rel_filepath=@p ORDER BY changed_at ASC
                LIMIT @limit OFFSET @offset";
            cmd.Parameters.AddWithValue("@p",      relFilepath);
            cmd.Parameters.AddWithValue("@limit",  limit);
            cmd.Parameters.AddWithValue("@offset", offset);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                result.Add(new FileHistoryItem
                {
                    Id=r.GetInt64(0), ChangedAt=r.GetString(1), EventType=r.GetString(2),
                    MachineName=r.GetString(3), Sha256Hash=r.GetString(4),
                    OldContent=r.IsDBNull(5)?null:r.GetString(5),
                    DiffText=r.IsDBNull(6)?null:r.GetString(6),
                    IsBackfill=!r.IsDBNull(7) && r.GetInt32(7)==1
                });
        }
        catch (FileNotFoundException) { return new(); }
        catch (SqliteException ex)
        {
            _logger.LogWarning(ex, "GetFileHistory: read failed for {J}", jobName);
            return new();
        }
        return result;
    }

    /// <summary>Earliest audit-event timestamp for a job, or null.</summary>
    public async Task<string?> GetJobFirstEventTimeAsync(string jobName)
    {
        await FlushAsync(jobName);
        var shardPath = _discovery.ShardPath(jobName);
        if (shardPath is null) return null;
        using var conn = OpenRead(shardPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MIN(changed_at) FROM audit_log";
        return cmd.ExecuteScalar()?.ToString();
    }

    private static string BuildWhere(EventFilter f)
    {
        var clauses = new List<string> { "1=1" };
        if (f.Module         is not null) clauses.Add("module            = @module");
        if (f.Priority       is not null) clauses.Add("monitor_priority  = @priority");
        if (f.Service        is not null) clauses.Add("owner_service     = @service");
        if (f.EventType      is not null) clauses.Add("event_type        = @type");
        if (f.Machine        is not null) clauses.Add("machine_name      = @machine");
        if (f.From           is not null) clauses.Add("changed_at       >= @from");
        if (f.To             is not null) clauses.Add("changed_at       <= @to");
        if (f.Path           is not null) clauses.Add("filepath LIKE @path || '%'");
        if (f.FileEra        is not null) clauses.Add("file_era = @fileEra");
        if (f.ExcludeCreated && f.EventType is null)
                                          clauses.Add("event_type        != 'Created'");
        return string.Join(" AND ", clauses);
    }

    private static void BindFilter(SqliteCommand cmd, EventFilter f)
    {
        if (f.Module    is not null) cmd.Parameters.AddWithValue("@module",   f.Module);
        if (f.Priority  is not null) cmd.Parameters.AddWithValue("@priority", f.Priority);
        if (f.Service   is not null) cmd.Parameters.AddWithValue("@service",  f.Service);
        if (f.EventType is not null) cmd.Parameters.AddWithValue("@type",     f.EventType);
        if (f.Machine   is not null) cmd.Parameters.AddWithValue("@machine",  f.Machine);
        if (f.From      is not null) cmd.Parameters.AddWithValue("@from",     f.From);
        if (f.To        is not null) cmd.Parameters.AddWithValue("@to",       f.To);
        if (f.Path      is not null) cmd.Parameters.AddWithValue("@path",     f.Path);
        if (f.FileEra   is not null) cmd.Parameters.AddWithValue("@fileEra",  f.FileEra);
    }
}
