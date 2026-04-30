namespace FalconAuditWebServer.Services;

using System.Collections.Concurrent;
using FalconAuditWebServer.Models;
using Microsoft.Data.Sqlite;

public class QueryRepository : IDisposable
{
    private readonly ConcurrentDictionary<string, SqliteConnection> _connections =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _connLock = new();
    private readonly JobDiscoveryService _discovery;
    private readonly ILogger<QueryRepository> _logger;

    public QueryRepository(JobDiscoveryService discovery, ILogger<QueryRepository> logger)
    { _discovery=discovery; _logger=logger; }

    private SqliteConnection? GetConnection(string dbPath)
    {
        if (_connections.TryGetValue(dbPath, out var existing)) return existing;
        lock (_connLock)
        {
            if (_connections.TryGetValue(dbPath, out existing)) return existing;
            try
            {
                var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
                conn.Open();
                using var p = conn.CreateCommand();
                p.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=3000;";
                p.ExecuteNonQuery();
                _connections[dbPath] = conn;
                return conn;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QueryRepository: cannot open {P}", dbPath);
                return null;
            }
        }
    }

    public List<JobSummary> ListJobs()
    {
        var result = new List<JobSummary>();
        foreach (var job in _discovery.KnownJobs)
        {
            var shardPath = _discovery.ShardPath(job);
            if (shardPath is null) continue;
            try
            {
                var conn = GetConnection(shardPath);
                if (conn is null) continue;
                lock (conn)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(*), MIN(changed_at), MAX(changed_at), GROUP_CONCAT(DISTINCT machine_name) FROM audit_log";
                    using var r = cmd.ExecuteReader();
                    if (r.Read())
                        result.Add(new JobSummary
                        {
                            JobName        = job,
                            ShardPath      = shardPath,
                            TotalEvents    = r.IsDBNull(0) ? 0 : r.GetInt64(0),
                            FirstEvent     = r.IsDBNull(1) ? "" : r.GetString(1),
                            LastEvent      = r.IsDBNull(2) ? "" : r.GetString(2),
                            Machines       = r.IsDBNull(3) ? "" : r.GetString(3),
                            ShardSizeBytes = new FileInfo(shardPath).Length
                        });
                }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "QueryRepository: stats failed for {J}", job); }
        }
        return result;
    }

    public (List<AuditEventSummary> Items, long Total) GetEvents(string jobName, EventFilter f)
    {
        var shardPath = _discovery.ShardPath(jobName);
        if (shardPath is null) return (new(), 0);
        return GetEventsFromDb(shardPath, f);
    }

    public (List<AuditEventSummary> Items, long Total) GetEventsFromDb(string dbPath, EventFilter f)
    {
        if (!File.Exists(dbPath)) return (new(), 0);

        var conn = GetConnection(dbPath);
        if (conn is null) return (new(), 0);
        var where  = BuildWhere(f);
        var order  = f.Sort == "asc" ? "ASC" : "DESC";
        int offset = (f.Page - 1) * f.PageSize;
        if (offset < 0) offset = 0;

        long total = 0;
        var items = new List<AuditEventSummary>();
        lock (conn)
        {
            using (var cnt = conn.CreateCommand())
            {
                cnt.CommandText = $"SELECT COUNT(*) FROM audit_log WHERE {where}";
                BindFilter(cnt, f);
                total = (long)(cnt.ExecuteScalar() ?? 0L);
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"SELECT id,changed_at,event_type,filepath,rel_filepath,module,
                owner_service,monitor_priority,machine_name,sha256_hash,file_description,change_summary,is_backfill,diff_text,login_user
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
                    LoginUser=r.IsDBNull(14)?null:r.GetString(14)
                });
        }
        return (items, total);
    }

    public AuditEventDetail? GetEvent(string jobName, long id)
    {
        var shardPath = _discovery.ShardPath(jobName);
        if (shardPath is null) return null;
        var conn = GetConnection(shardPath);
        if (conn is null) return null;
        lock (conn)
        {
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
    }

    public List<FileHistoryItem> GetFileHistory(string jobName, string relFilepath)
    {
        var shardPath = _discovery.ShardPath(jobName);
        if (shardPath is null) return new();
        var conn = GetConnection(shardPath);
        if (conn is null) return new();
        var result = new List<FileHistoryItem>();
        lock (conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT id,changed_at,event_type,machine_name,sha256_hash,
                old_content,diff_text,is_backfill
                FROM audit_log WHERE rel_filepath=@p ORDER BY changed_at ASC";
            cmd.Parameters.AddWithValue("@p", relFilepath);
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
        return result;
    }

    private static string BuildWhere(EventFilter f)
    {
        var clauses = new List<string> { "1=1" };
        if (f.Module    is not null) clauses.Add("module            = @module");
        if (f.Priority  is not null) clauses.Add("monitor_priority  = @priority");
        if (f.Service   is not null) clauses.Add("owner_service     = @service");
        if (f.EventType is not null) clauses.Add("event_type        = @type");
        if (f.Machine   is not null) clauses.Add("machine_name      = @machine");
        if (f.From      is not null) clauses.Add("changed_at       >= @from");
        if (f.To        is not null) clauses.Add("changed_at       <= @to");
        if (f.Path      is not null) clauses.Add("instr(filepath, @path) > 0");
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
    }

    public void Dispose()
    {
        foreach (var c in _connections.Values) c.Dispose();
        _connections.Clear();
    }
}
