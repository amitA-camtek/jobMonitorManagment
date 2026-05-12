namespace FalconAuditService;

using FalconAuditService.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

/// <summary>
/// Per-job SQLite shard accessor with **lazy connections** — no handle is held
/// between calls. Every method opens its own connection, does the work, closes
/// the connection. This keeps `audit.db` unlocked between writes so BIS's
/// recursive delete of the job folder never hits a sharing violation.
///
/// For high-frequency write traffic, route through <see cref="AuditEventQueue"/>
/// (one per job) which buffers events and calls <see cref="WriteBatchAsync"/>
/// once per flush — a single open-transaction-close cycle for many events.
/// </summary>
public class SqliteRepository
{
    private readonly string _dbPath;
    private readonly ILogger<SqliteRepository> _logger;

    public SqliteRepository(string dbPath, ILogger<SqliteRepository> logger)
    {
        _dbPath = dbPath;
        _logger = logger;
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        EnsureSchema();
        logger.LogInformation("SqliteRepository: ready (lazy). DB={D}", dbPath);
    }

    public string DbPath => _dbPath;

    // ── connection helpers ──────────────────────────────────────────────────

    private SqliteConnection OpenWrite()
    {
        // Pooling=False ensures Dispose immediately releases the OS file handle.
        var conn = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
        conn.Open();
        using var p = conn.CreateCommand();
        p.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=3000; PRAGMA wal_autocheckpoint=200;";
        p.ExecuteNonQuery();
        return conn;
    }

    private SqliteConnection OpenRead()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly;Pooling=False");
        conn.Open();
        using var p = conn.CreateCommand();
        p.CommandText = "PRAGMA busy_timeout=3000;";
        p.ExecuteNonQuery();
        return conn;
    }

    // ── Schema ──────────────────────────────────────────────────────────────

    private void EnsureSchema()
    {
        using var conn = OpenWrite();

        // verify WAL was actually enabled (PRAGMA in OpenWrite already ran it)
        using (var check = conn.CreateCommand())
        {
            check.CommandText = "PRAGMA journal_mode;";
            var mode = check.ExecuteScalar()?.ToString();
            if (!string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"SQLite WAL mode could not be enabled (got '{mode}'). " +
                    "Ensure the database is not on a network share or FAT32 volume.");
        }

        using var tx  = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS audit_log (
                id               INTEGER PRIMARY KEY AUTOINCREMENT,
                changed_at       TEXT    NOT NULL,
                event_type       TEXT    NOT NULL
                                 CHECK(event_type IN ('Created','Modified','Deleted','Renamed')),
                filepath         TEXT    NOT NULL,
                rel_filepath     TEXT    NOT NULL,
                module           TEXT    NOT NULL,
                owner_service    TEXT    NOT NULL,
                monitor_priority TEXT    NOT NULL CHECK (monitor_priority IN ('P1','P2','P3')),
                machine_name     TEXT    NOT NULL,
                sha256_hash      TEXT    NOT NULL,
                old_content      TEXT    NULL,
                diff_text        TEXT    NULL,
                file_description TEXT    NOT NULL DEFAULT '',
                change_summary   TEXT    NOT NULL DEFAULT '',
                is_backfill      INTEGER NOT NULL DEFAULT 0,
                old_filepath     TEXT    NULL,
                login_user       TEXT    NULL,
                setup_name       TEXT    NULL,
                recipe_name      TEXT    NULL,
                file_era         TEXT    NULL
            );

            CREATE INDEX IF NOT EXISTS ix_audit_log_changed_at        ON audit_log (changed_at DESC);
            CREATE INDEX IF NOT EXISTS ix_audit_log_module            ON audit_log (module);
            CREATE INDEX IF NOT EXISTS ix_audit_log_priority          ON audit_log (monitor_priority);
            CREATE INDEX IF NOT EXISTS ix_audit_log_event_type        ON audit_log (event_type);
            CREATE INDEX IF NOT EXISTS ix_audit_log_machine           ON audit_log (machine_name);
            CREATE INDEX IF NOT EXISTS ix_audit_log_owner_service     ON audit_log (owner_service);
            CREATE INDEX IF NOT EXISTS ix_audit_log_rel_filepath      ON audit_log (rel_filepath);
            CREATE INDEX IF NOT EXISTS ix_audit_log_module_changed_at ON audit_log (module, changed_at DESC);
            CREATE INDEX IF NOT EXISTS ix_audit_log_filepath          ON audit_log (filepath);
            CREATE INDEX IF NOT EXISTS ix_audit_log_file_era          ON audit_log (file_era);

            CREATE TABLE IF NOT EXISTS file_baselines (
                filepath     TEXT PRIMARY KEY,
                last_hash    TEXT NOT NULL,
                last_seen    TEXT NOT NULL,
                last_content TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_file_baselines_last_seen ON file_baselines (last_seen);

            CREATE TABLE IF NOT EXISTS schema_meta (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            INSERT OR IGNORE INTO schema_meta (key, value) VALUES ('schema_version', '5');
            INSERT OR IGNORE INTO schema_meta (key, value) VALUES ('audit_db_version', '1');

            CREATE TABLE IF NOT EXISTS monitor_config (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            INSERT OR IGNORE INTO schema_meta (key, value) VALUES
                ('created_at_utc', strftime('%Y-%m-%dT%H:%M:%fZ','now'));
        ";
        cmd.ExecuteNonQuery();
        tx.Commit();

        MigrateSchema(conn);
    }

    private void MigrateSchema(SqliteConnection conn)
    {
        var version = 1;
        using (var qv = conn.CreateCommand())
        {
            qv.CommandText = "SELECT value FROM schema_meta WHERE key='schema_version'";
            var raw = qv.ExecuteScalar()?.ToString();
            if (int.TryParse(raw, out var v)) version = v;
        }

        if (version < 2)
        {
            AlterTableAddColumns(conn, new[] { "file_description TEXT NOT NULL DEFAULT ''",
                                                "change_summary   TEXT NOT NULL DEFAULT ''" });
            SetSchemaVersion(conn, 2);
            _logger.LogInformation("SqliteRepository: migrated schema to v2 (file_description, change_summary).");
        }

        if (version < 3)
        {
            AlterTableAddColumns(conn, new[] { "is_backfill  INTEGER NOT NULL DEFAULT 0",
                                                 "old_filepath TEXT NULL" });
            SetSchemaVersion(conn, 3);
            _logger.LogInformation("SqliteRepository: migrated schema to v3 (is_backfill, old_filepath).");
        }

        if (version < 4)
        {
            AlterTableAddColumns(conn, new[] { "login_user TEXT NULL" });
            SetSchemaVersion(conn, 4);
            _logger.LogInformation("SqliteRepository: migrated schema to v4 (login_user).");
        }

        if (version < 5)
        {
            AlterTableAddColumns(conn, new[] { "setup_name TEXT NULL", "recipe_name TEXT NULL" });
            SetSchemaVersion(conn, 5);
            _logger.LogInformation("SqliteRepository: migrated schema to v5 (setup_name, recipe_name).");
        }
    }

    private static void AlterTableAddColumns(SqliteConnection conn, string[] columnDefs)
    {
        foreach (var col in columnDefs)
        {
            try
            {
                using var ac = conn.CreateCommand();
                ac.CommandText = $"ALTER TABLE audit_log ADD COLUMN {col}";
                ac.ExecuteNonQuery();
            }
            catch (SqliteException ex) when (
                ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
            {
                // column already exists — safe to continue
            }
        }
    }

    private static void SetSchemaVersion(SqliteConnection conn, int version)
    {
        using var uv = conn.CreateCommand();
        uv.CommandText = "INSERT OR REPLACE INTO schema_meta (key,value) VALUES ('schema_version',@v)";
        uv.Parameters.AddWithValue("@v", version.ToString());
        uv.ExecuteNonQuery();
    }

    // ── audit_log writes ────────────────────────────────────────────────────

    /// <summary>
    /// Insert a single audit row + upsert its baseline. Opens its own connection.
    /// Use <see cref="WriteBatchAsync"/> for multiple events from the same job.
    /// </summary>
    public async Task InsertAuditEventAsync(AuditLogEntry e, FileBaseline baseline)
    {
        await WriteBatchAsync(new[] { (e, baseline) });
    }

    /// <summary>
    /// Insert N audit rows and upsert their baselines in a single transaction.
    /// One open-transaction-close cycle for the whole batch — minimum write
    /// amplification on `audit.db`.
    /// </summary>
    public async Task WriteBatchAsync(IReadOnlyList<(AuditLogEntry Entry, FileBaseline Baseline)> batch)
    {
        if (batch.Count == 0) return;

        // SqliteConnection is sync-only on Open/Dispose; the inserts are async.
        await using var conn = OpenWrite();
        await using var tx   = (SqliteTransaction)await conn.BeginTransactionAsync();

        await using var ins = conn.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = @"
            INSERT INTO audit_log
              (changed_at, event_type, filepath, rel_filepath, module, owner_service,
               monitor_priority, machine_name, sha256_hash, old_content, diff_text,
               file_description, change_summary, is_backfill, old_filepath, login_user,
               setup_name, recipe_name, file_era)
            VALUES (@ca,@et,@fp,@rfp,@mod,@svc,@pri,@mn,@hash,@oc,@dt,@fd,@cs,@ib,@ofp,@lu,@sn,@rn,@fe)";
        // Pre-create parameters; reset values per row.
        var pCa  = ins.Parameters.Add("@ca",  Microsoft.Data.Sqlite.SqliteType.Text);
        var pEt  = ins.Parameters.Add("@et",  Microsoft.Data.Sqlite.SqliteType.Text);
        var pFp  = ins.Parameters.Add("@fp",  Microsoft.Data.Sqlite.SqliteType.Text);
        var pRfp = ins.Parameters.Add("@rfp", Microsoft.Data.Sqlite.SqliteType.Text);
        var pMod = ins.Parameters.Add("@mod", Microsoft.Data.Sqlite.SqliteType.Text);
        var pSvc = ins.Parameters.Add("@svc", Microsoft.Data.Sqlite.SqliteType.Text);
        var pPri = ins.Parameters.Add("@pri", Microsoft.Data.Sqlite.SqliteType.Text);
        var pMn  = ins.Parameters.Add("@mn",  Microsoft.Data.Sqlite.SqliteType.Text);
        var pHsh = ins.Parameters.Add("@hash",Microsoft.Data.Sqlite.SqliteType.Text);
        var pOc  = ins.Parameters.Add("@oc",  Microsoft.Data.Sqlite.SqliteType.Text);
        var pDt  = ins.Parameters.Add("@dt",  Microsoft.Data.Sqlite.SqliteType.Text);
        var pFd  = ins.Parameters.Add("@fd",  Microsoft.Data.Sqlite.SqliteType.Text);
        var pCs  = ins.Parameters.Add("@cs",  Microsoft.Data.Sqlite.SqliteType.Text);
        var pIb  = ins.Parameters.Add("@ib",  Microsoft.Data.Sqlite.SqliteType.Integer);
        var pOfp = ins.Parameters.Add("@ofp", Microsoft.Data.Sqlite.SqliteType.Text);
        var pLu  = ins.Parameters.Add("@lu",  Microsoft.Data.Sqlite.SqliteType.Text);
        var pSn  = ins.Parameters.Add("@sn",  Microsoft.Data.Sqlite.SqliteType.Text);
        var pRn  = ins.Parameters.Add("@rn",  Microsoft.Data.Sqlite.SqliteType.Text);
        var pFe  = ins.Parameters.Add("@fe",  Microsoft.Data.Sqlite.SqliteType.Text);

        await using var upb = conn.CreateCommand();
        upb.Transaction = tx;
        upb.CommandText = @"
            INSERT INTO file_baselines (filepath, last_hash, last_seen, last_content)
            VALUES (@fp, @lh, @ls, @lc)
            ON CONFLICT(filepath) DO UPDATE SET
              last_hash    = excluded.last_hash,
              last_seen    = excluded.last_seen,
              last_content = excluded.last_content";
        var bFp = upb.Parameters.Add("@fp", Microsoft.Data.Sqlite.SqliteType.Text);
        var bLh = upb.Parameters.Add("@lh", Microsoft.Data.Sqlite.SqliteType.Text);
        var bLs = upb.Parameters.Add("@ls", Microsoft.Data.Sqlite.SqliteType.Text);
        var bLc = upb.Parameters.Add("@lc", Microsoft.Data.Sqlite.SqliteType.Text);

        foreach (var (e, baseline) in batch)
        {
            pCa.Value  = e.ChangedAt;
            pEt.Value  = e.EventType;
            pFp.Value  = e.Filepath;
            pRfp.Value = e.RelFilepath;
            pMod.Value = e.Module;
            pSvc.Value = e.OwnerService;
            pPri.Value = e.MonitorPriority;
            pMn.Value  = e.MachineName;
            pHsh.Value = e.Sha256Hash;
            pOc.Value  = (object?)e.OldContent  ?? DBNull.Value;
            pDt.Value  = (object?)e.DiffText    ?? DBNull.Value;
            pFd.Value  = e.FileDescription;
            pCs.Value  = e.ChangeSummary;
            pIb.Value  = e.IsBackfill ? 1 : 0;
            pOfp.Value = (object?)e.OldFilepath ?? DBNull.Value;
            pLu.Value  = (object?)e.LoginUser   ?? DBNull.Value;
            pSn.Value  = (object?)e.Setup       ?? DBNull.Value;
            pRn.Value  = (object?)e.Recipe      ?? DBNull.Value;
            pFe.Value  = (object?)e.FileEra     ?? DBNull.Value;
            await ins.ExecuteNonQueryAsync();

            bFp.Value = baseline.Filepath;
            bLh.Value = baseline.LastHash;
            bLs.Value = baseline.LastSeen;
            bLc.Value = (object?)baseline.LastContent ?? DBNull.Value;
            await upb.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    /// <summary>Update a baseline entry without writing an audit event (used by CatchUpScanner for unchanged files).</summary>
    public async Task UpsertBaselineAsync(FileBaseline baseline)
    {
        await using var conn = OpenWrite();
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO file_baselines (filepath, last_hash, last_seen, last_content)
            VALUES (@fp, @lh, @ls, @lc)
            ON CONFLICT(filepath) DO UPDATE SET
              last_hash    = excluded.last_hash,
              last_seen    = excluded.last_seen,
              last_content = excluded.last_content";
        cmd.Parameters.AddWithValue("@fp", baseline.Filepath);
        cmd.Parameters.AddWithValue("@lh", baseline.LastHash);
        cmd.Parameters.AddWithValue("@ls", baseline.LastSeen);
        cmd.Parameters.AddWithValue("@lc", (object?)baseline.LastContent ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    // ── file_baselines reads ────────────────────────────────────────────────

    public async Task<FileBaseline?> GetBaselineAsync(string filepath)
    {
        await using var conn = OpenRead();
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText =
            "SELECT filepath, last_hash, last_seen, last_content " +
            "FROM file_baselines WHERE filepath=@fp";
        cmd.Parameters.AddWithValue("@fp", filepath);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new FileBaseline
        {
            Filepath    = r.GetString(0),
            LastHash    = r.GetString(1),
            LastSeen    = r.GetString(2),
            LastContent = r.IsDBNull(3) ? null : r.GetString(3)
        };
    }

    public async Task<List<FileBaseline>> GetAllBaselinesAsync()
    {
        await using var conn = OpenRead();
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText =
            "SELECT filepath, last_hash, last_seen, last_content " +
            "FROM file_baselines";
        var list = new List<FileBaseline>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new FileBaseline
            {
                Filepath    = r.GetString(0),
                LastHash    = r.GetString(1),
                LastSeen    = r.GetString(2),
                LastContent = r.IsDBNull(3) ? null : r.GetString(3)
            });
        return list;
    }

    public async Task DeleteBaselineAsync(string filepath)
    {
        await using var conn = OpenWrite();
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM file_baselines WHERE filepath=@fp";
        cmd.Parameters.AddWithValue("@fp", filepath);
        await cmd.ExecuteNonQueryAsync();
    }

    // ── monitor_config ──────────────────────────────────────────────────────

    public async Task SetConfigValueAsync(string key, string value)
    {
        await using var conn = OpenWrite();
        await using var cmd  = conn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO monitor_config (key, value) VALUES (@k, @v)";
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@v", value);
        await cmd.ExecuteNonQueryAsync();
    }

    public string? GetConfigValue(string key)
    {
        try
        {
            using var conn = OpenRead();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM monitor_config WHERE key=@k";
            cmd.Parameters.AddWithValue("@k", key);
            return cmd.ExecuteScalar()?.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GetConfigValue({K}) failed.", key);
            return null;
        }
    }

    public bool IsInitialScanDone() => GetConfigValue("initial_scan_done") is not null;

    public Task SetInitialScanDoneAsync() => SetConfigValueAsync("initial_scan_done", "true");
}
