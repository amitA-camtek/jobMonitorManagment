namespace FalconAuditService;

using FalconAuditService.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

public class SqliteRepository : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly SqliteConnection _readConn;
    private readonly SemaphoreSlim    _writeLock = new(1, 1);
    private volatile bool             _disposed;
    private readonly ILogger<SqliteRepository> _logger;

    public SqliteRepository(string dbPath, ILogger<SqliteRepository> logger)
    {
        _logger = logger;
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        // Pooling=False ensures Dispose immediately releases the OS file handle
        // so the .audit\ directory can be removed when a job is evicted.
        _conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        _conn.Open();

        _readConn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        _readConn.Open();

        using var rp = _readConn.CreateCommand();
        rp.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=3000;";
        rp.ExecuteNonQuery();

        using var wp = _conn.CreateCommand();
        wp.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=3000;";
        wp.ExecuteNonQuery();

        using var check = _conn.CreateCommand();
        check.CommandText = "PRAGMA journal_mode;";
        var mode = check.ExecuteScalar()?.ToString();
        if (!string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"SQLite WAL mode could not be enabled (got '{mode}'). " +
                "Ensure the database is not on a network share or FAT32 volume.");

        EnsureSchema();
        logger.LogInformation("SqliteRepository: ready. DB={D}", dbPath);
    }

    // ── Schema ───────────────────────────────────────────────────────────────

    private void EnsureSchema()
    {
        using var tx  = _conn.BeginTransaction();
        using var cmd = _conn.CreateCommand();
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

        MigrateSchema();
    }

    // Migrates databases created before the current schema version.
    // ALTER TABLE ADD COLUMN is idempotent-safe only via try/catch (SQLite has no IF NOT EXISTS).
    private void MigrateSchema()
    {
        var version = 1;
        using (var qv = _conn.CreateCommand())
        {
            qv.CommandText = "SELECT value FROM schema_meta WHERE key='schema_version'";
            var raw = qv.ExecuteScalar()?.ToString();
            if (int.TryParse(raw, out var v)) version = v;
        }

        if (version < 2)
        {
            AlterTableAddColumns(new[] { "file_description TEXT NOT NULL DEFAULT ''",
                                         "change_summary   TEXT NOT NULL DEFAULT ''" });
            SetSchemaVersion(2);
            _logger.LogInformation("SqliteRepository: migrated schema to v2 (file_description, change_summary).");
        }

        if (version < 3)
        {
            AlterTableAddColumns(new[] { "is_backfill  INTEGER NOT NULL DEFAULT 0",
                                          "old_filepath TEXT NULL" });
            SetSchemaVersion(3);
            _logger.LogInformation("SqliteRepository: migrated schema to v3 (is_backfill, old_filepath).");
        }

        if (version < 4)
        {
            AlterTableAddColumns(new[] { "login_user TEXT NULL" });
            SetSchemaVersion(4);
            _logger.LogInformation("SqliteRepository: migrated schema to v4 (login_user).");
        }

        if (version < 5)
        {
            AlterTableAddColumns(new[] { "setup_name TEXT NULL", "recipe_name TEXT NULL" });
            SetSchemaVersion(5);
            _logger.LogInformation("SqliteRepository: migrated schema to v5 (setup_name, recipe_name).");
        }
    }

    private void AlterTableAddColumns(string[] columnDefs)
    {
        foreach (var col in columnDefs)
        {
            try
            {
                using var ac = _conn.CreateCommand();
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

    private void SetSchemaVersion(int version)
    {
        using var uv = _conn.CreateCommand();
        uv.CommandText = "INSERT OR REPLACE INTO schema_meta (key,value) VALUES ('schema_version',@v)";
        uv.Parameters.AddWithValue("@v", version.ToString());
        uv.ExecuteNonQuery();
    }

    // ── audit_log ────────────────────────────────────────────────────────────

    public async Task InsertAuditEventAsync(AuditLogEntry e, FileBaseline baseline)
    {
        if (_disposed) return;
        await _writeLock.WaitAsync();
        try
        {
            using var tx = _conn.BeginTransaction();
            using var ins = _conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = @"
                INSERT INTO audit_log
                  (changed_at, event_type, filepath, rel_filepath, module, owner_service,
                   monitor_priority, machine_name, sha256_hash, old_content, diff_text,
                   file_description, change_summary, is_backfill, old_filepath, login_user,
                   setup_name, recipe_name, file_era)
                VALUES (@ca,@et,@fp,@rfp,@mod,@svc,@pri,@mn,@hash,@oc,@dt,@fd,@cs,@ib,@ofp,@lu,@sn,@rn,@fe)";
            ins.Parameters.AddWithValue("@ca",  e.ChangedAt);
            ins.Parameters.AddWithValue("@et",  e.EventType);
            ins.Parameters.AddWithValue("@fp",  e.Filepath);
            ins.Parameters.AddWithValue("@rfp", e.RelFilepath);
            ins.Parameters.AddWithValue("@mod", e.Module);
            ins.Parameters.AddWithValue("@svc", e.OwnerService);
            ins.Parameters.AddWithValue("@pri", e.MonitorPriority);
            ins.Parameters.AddWithValue("@mn",  e.MachineName);
            ins.Parameters.AddWithValue("@hash",e.Sha256Hash);
            ins.Parameters.AddWithValue("@oc",  (object?)e.OldContent   ?? DBNull.Value);
            ins.Parameters.AddWithValue("@dt",  (object?)e.DiffText     ?? DBNull.Value);
            ins.Parameters.AddWithValue("@fd",  e.FileDescription);
            ins.Parameters.AddWithValue("@cs",  e.ChangeSummary);
            ins.Parameters.AddWithValue("@ib",  e.IsBackfill ? 1 : 0);
            ins.Parameters.AddWithValue("@ofp", (object?)e.OldFilepath  ?? DBNull.Value);
            ins.Parameters.AddWithValue("@lu",  (object?)e.LoginUser    ?? DBNull.Value);
            ins.Parameters.AddWithValue("@sn",  (object?)e.Setup        ?? DBNull.Value);
            ins.Parameters.AddWithValue("@rn",  (object?)e.Recipe       ?? DBNull.Value);
            ins.Parameters.AddWithValue("@fe",  (object?)e.FileEra      ?? DBNull.Value);
            await ins.ExecuteNonQueryAsync();

            using var upb = _conn.CreateCommand();
            upb.Transaction = tx;
            upb.CommandText = @"
                INSERT INTO file_baselines (filepath, last_hash, last_seen, last_content)
                VALUES (@fp, @lh, @ls, @lc)
                ON CONFLICT(filepath) DO UPDATE SET
                  last_hash    = excluded.last_hash,
                  last_seen    = excluded.last_seen,
                  last_content = excluded.last_content";
            upb.Parameters.AddWithValue("@fp", baseline.Filepath);
            upb.Parameters.AddWithValue("@lh", baseline.LastHash);
            upb.Parameters.AddWithValue("@ls", baseline.LastSeen);
            upb.Parameters.AddWithValue("@lc", (object?)baseline.LastContent ?? DBNull.Value);
            await upb.ExecuteNonQueryAsync();

            tx.Commit();
        }
        finally { _writeLock.Release(); }
    }

    /// <summary>Update a baseline entry without writing an audit event (used for unchanged files in CatchUpScanner).</summary>
    public async Task UpsertBaselineAsync(FileBaseline baseline)
    {
        if (_disposed) return;
        await _writeLock.WaitAsync();
        try
        {
            using var cmd = _conn.CreateCommand();
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
        finally { _writeLock.Release(); }
    }

    // ── file_baselines ───────────────────────────────────────────────────────

    public async Task<FileBaseline?> GetBaselineAsync(string filepath)
    {
        try
        {
            using var cmd = _readConn.CreateCommand();
            cmd.CommandText =
                "SELECT filepath, last_hash, last_seen, last_content " +
                "FROM file_baselines WHERE filepath=@fp";
            cmd.Parameters.AddWithValue("@fp", filepath);
            using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            return new FileBaseline
            {
                Filepath    = r.GetString(0),
                LastHash    = r.GetString(1),
                LastSeen    = r.GetString(2),
                LastContent = r.IsDBNull(3) ? null : r.GetString(3)
            };
        }
        catch (Exception) when (_disposed) { return null; }
    }

    public async Task<List<FileBaseline>> GetAllBaselinesAsync()
    {
        try
        {
            var list = new List<FileBaseline>();
            using var cmd = _readConn.CreateCommand();
            cmd.CommandText =
                "SELECT filepath, last_hash, last_seen, last_content " +
                "FROM file_baselines";
            using var r = await cmd.ExecuteReaderAsync();
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
        catch (Exception) when (_disposed) { return new(); }
    }

    public async Task DeleteBaselineAsync(string filepath)
    {
        if (_disposed) return;
        await _writeLock.WaitAsync();
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM file_baselines WHERE filepath=@fp";
            cmd.Parameters.AddWithValue("@fp", filepath);
            await cmd.ExecuteNonQueryAsync();
        }
        finally { _writeLock.Release(); }
    }

    // ── monitor_config ───────────────────────────────────────────────────────

    public async Task SetConfigValueAsync(string key, string value)
    {
        if (_disposed) return;
        await _writeLock.WaitAsync();
        try
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO monitor_config (key, value) VALUES (@k, @v)";
            cmd.Parameters.AddWithValue("@k", key);
            cmd.Parameters.AddWithValue("@v", value);
            await cmd.ExecuteNonQueryAsync();
        }
        finally { _writeLock.Release(); }
    }

    public string? GetConfigValue(string key)
    {
        try
        {
            using var cmd = _readConn.CreateCommand();
            cmd.CommandText = "SELECT value FROM monitor_config WHERE key=@k";
            cmd.Parameters.AddWithValue("@k", key);
            return cmd.ExecuteScalar()?.ToString();
        }
        catch (Exception) when (_disposed) { return null; }
    }

    public bool IsInitialScanDone()
        => GetConfigValue("initial_scan_done") is not null;

    public Task SetInitialScanDoneAsync()
        => SetConfigValueAsync("initial_scan_done", "true");

    public void Dispose()
    {
        _disposed = true;
        _writeLock.Dispose();
        _conn.Dispose();
        _readConn.Dispose();
    }
}
