# FalconAuditService — SQLite Storage Review

**Reviewed:** 2026-05-05
**Reviewer:** storage-reviewer (Claude)
**Technology:** SQLite via Microsoft.Data.Sqlite (per-job shard files)
**Design files:** none — context derived from source

---

## Summary verdict

| Area | Status |
|---|---|
| WAL / PRAGMA configuration | Mostly compliant; read connection missing `synchronous=NORMAL` |
| Write serialisation | Compliant — `SemaphoreSlim(1)` per shard |
| Eviction / disposal race | **Two races present** (medium severity) |
| Transactions | Mostly compliant; one write path missing a transaction |
| Schema correctness | Compliant with minor observations |
| Parameterised queries | **One interpolated DDL statement** (low risk but flagged); query WHERE clause safe |
| Index coverage | Compliant for all hot query paths |
| ContentCache growth cap | Compliant — LRU with 200 MB ceiling |
| Async correctness | Partially compliant — QueryRepository is sync on request thread |
| Vacuum / WAL checkpoint | Not configured (informational) |

---

## CRITICAL

None.

---

## HIGH

### H1 — `CloseShard` disposes a connection that may be in active use under `lock(conn)` in another thread

**File:** `QueryRepository.cs:19-25` and `QueryRepository.cs:62,128,171,204,232`

`CloseShard` is called from `ShardEvictionService.EvictAfterGraceAsync` (line 88 of `ShardEvictionService.cs`):

```csharp
_shards.Remove(jobName);                // disposes SqliteRepository (write conn)
_queryRepo.CloseShard(...);             // disposes read conn in QueryRepository
```

`CloseShard` does:

```csharp
if (_connections.TryRemove(dbPath, out var conn))
{
    try { conn.Dispose(); } catch { }
}
```

At the same time, any in-flight HTTP request (e.g. `GetEvents`, `ListJobs`) holds `lock(conn)` and may be mid-read when `Dispose()` is called on that connection from the eviction background task. `SqliteConnection.Dispose()` closes the underlying file handle. The reader will then throw `ObjectDisposedException` or `SqliteException: "database connection was closed"` through the live `SqliteDataReader`, which bubbles out of the `lock` block as an unhandled 500 on that request.

The grace period is 2 seconds, which is normally sufficient, but there is no guard that prevents disposal of a connection that is actively executing a query.

**Fix:** wrap `lock(conn)` usages with a disposed-check, or use a `ReaderWriterLockSlim` per connection that `CloseShard` acquires in write-mode before disposing:

```csharp
// In QueryRepository — lightweight approach:
private sealed class ShardConn : IDisposable
{
    public SqliteConnection Connection;
    public volatile bool     IsClosing;
}

// CloseShard sets IsClosing = true before Dispose.
// All query methods check IsClosing after acquiring lock(conn) and return empty if true.
```

A simpler immediate fix is to add an `ObjectDisposedException` catch to all `lock(conn)` call sites and return the appropriate empty result, making eviction races observable as empty responses rather than 500s.

---

### H2 — `ShardRegistry.GetOrCreate` race: two threads can both pass the fast-path check and construct two `SqliteRepository` instances for the same shard

**File:** `ShardRegistry.cs:25-60`

```csharp
// Fast path: already open
if (_shards.TryGetValue(jobName, out var existing)) return existing;

// ... no lock between TryGetValue and GetOrAdd ...
var repo = new SqliteRepository(dbPath, ...);
var added = _shards.GetOrAdd(jobName, repo);
if (!ReferenceEquals(added, repo)) repo.Dispose();
return added;
```

The code correctly disposes the loser, so the ConcurrentDictionary remains consistent. However, the `SqliteRepository` constructor (`SqliteRepository.cs:22-46`) opens **two connections** and runs `EnsureSchema` + `MigrateSchema` before the result is even registered. Under concurrent job-arrival events (CatchUpScanner runs all jobs in parallel via `Task.WhenAll`), multiple threads can each run `EnsureSchema`/`MigrateSchema` concurrently against the same file. SQLite WAL mode can handle concurrent readers, but the migration path uses unguarded `ALTER TABLE` on the write connection. Two simultaneous schema migrations to the same DB file — one of which will be immediately disposed — can produce transient `SQLITE_BUSY` errors or write-lock contention that the 3-second busy_timeout will mask at the cost of startup latency.

**Fix:** guard the slow-path construction with a per-jobName lock (or use `Lazy<SqliteRepository>` in the dictionary value):

```csharp
// replace the non-thread-safe slow path with a locked construction:
lock (_createLock)
{
    if (_shards.TryGetValue(jobName, out existing)) return existing;
    var repo = new SqliteRepository(dbPath, ...);
    _shards[jobName] = repo;
    return repo;
}
// private readonly object _createLock = new();
```

---

## MEDIUM

### M1 — Read connection does not set `PRAGMA synchronous=NORMAL`

**File:** `SqliteRepository.cs:29`

```csharp
rp.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=3000;";
```

The write connection (line 33) correctly sets `synchronous=NORMAL`. The read connection omits it. This is not a correctness bug (synchronous only governs write-flush behaviour), but it is an inconsistency that would matter if the read connection were ever used for a write (e.g., during a future refactor). It is good practice to set synchronous on every connection that touches the database.

**Fix:**

```csharp
rp.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=3000;";
```

---

### M2 — `UpsertBaselineAsync` runs without a transaction

**File:** `SqliteRepository.cs:249-269`

`InsertAuditEventAsync` wraps its two-statement write (INSERT audit_log + UPSERT file_baselines) in an explicit transaction. `UpsertBaselineAsync`, called by `CatchUpScanner` for unchanged files, issues a single-statement UPSERT with no transaction wrapper. While a single statement is implicitly atomic in SQLite, if the call pattern ever evolves to batch multiple upserts in a loop (likely for catch-up performance), this becomes a multi-statement implicit-autocommit path with no rollback coverage. Mark the intent explicitly.

There is also a functional asymmetry: if `InsertAuditEventAsync` crashes mid-transaction and rolls back, `UpsertBaselineAsync` for the same file will leave the baseline inconsistent with the audit_log (baseline updated, no audit event). The current code path does not combine these two in a way that causes this, but the design is fragile.

**Fix:** add an explicit transaction to `UpsertBaselineAsync` for future-safety and symmetry:

```csharp
await _writeLock.WaitAsync();
try
{
    using var tx  = _conn.BeginTransaction();
    using var cmd = _conn.CreateCommand();
    cmd.Transaction = tx;
    cmd.CommandText = @"INSERT INTO file_baselines ...";
    // ... bind params ...
    await cmd.ExecuteNonQueryAsync();
    tx.Commit();
}
finally { _writeLock.Release(); }
```

---

### M3 — `GetFileHistory` has no LIMIT — unbounded result set

**File:** `QueryRepository.cs:207-210`

```csharp
cmd.CommandText = @"SELECT id,changed_at,event_type,machine_name,sha256_hash,
    old_content,diff_text,is_backfill
    FROM audit_log WHERE rel_filepath=@p ORDER BY changed_at ASC";
```

For a high-churn P1 file (e.g., a recipe parameter file modified hundreds of times over the life of a job), this query returns every historical row including the full `old_content` and `diff_text` TEXT columns into memory in one call. `old_content` can be up to `MaxContentBytes` per row (default appears large). This is an unbounded in-memory accumulation risk.

**Fix:** apply a reasonable LIMIT (e.g., 500) with optional `?from` / `?to` parameters, or add a page parameter:

```sql
SELECT ... FROM audit_log
WHERE rel_filepath = @p
ORDER BY changed_at ASC
LIMIT @limit OFFSET @offset
```

---

### M4 — `ListJobs`: nested `SqliteCommand` created while `DataReader` is still open on the same `lock(conn)`

**File:** `QueryRepository.cs:62-100`

```csharp
lock (conn)
{
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT COUNT(*), MIN(changed_at)...";
    using var r = cmd.ExecuteReader();         // reader still open
    if (r.Read())
    {
        // ...
        using var mc = conn.CreateCommand();   // second command on same conn
        mc.CommandText = "SELECT key, value FROM monitor_config ...";
        using var mr = mc.ExecuteReader();     // second reader
```

`Microsoft.Data.Sqlite` does not support MARS (Multiple Active Result Sets). Opening a second `ExecuteReader()` while the first reader `r` is still open on the same connection will silently close and replace the first reader in some driver versions, or throw a `SqliteException`. The outer `r.Read()` returns `true` before the inner reader is opened, so the outer result has already been consumed by that point — but this pattern is fragile and driver-version dependent.

**Fix:** close the first reader before opening the second, or use separate SELECT statements combined into one query:

```sql
SELECT
    COUNT(*)                             AS total,
    MIN(changed_at)                      AS first_event,
    MAX(changed_at)                      AS last_event,
    GROUP_CONCAT(DISTINCT machine_name)  AS machines,
    MAX(CASE WHEN m.key='job_origin'     THEN m.value END) AS origin,
    MAX(CASE WHEN s.key='created_at_utc' THEN s.value END) AS created_at
FROM audit_log
LEFT JOIN monitor_config m ON 1=1
LEFT JOIN schema_meta    s ON 1=1
GROUP BY m.key, s.key   -- needs refinement
```

Alternatively, call `r.Close()` before the inner command block:

```csharp
using var r = cmd.ExecuteReader();
bool hasData = r.Read();
long count   = hasData && !r.IsDBNull(0) ? r.GetInt64(0)  : 0;
string first = hasData && !r.IsDBNull(1) ? r.GetString(1) : "";
// ... collect all outer results ...
r.Close();   // <-- close before second command
using var mc = conn.CreateCommand();
```

---

## LOW

### L1 — `AlterTableAddColumns` interpolates column DDL directly into SQL

**File:** `SqliteRepository.cs:169`

```csharp
ac.CommandText = $"ALTER TABLE audit_log ADD COLUMN {col}";
```

`col` is sourced from hardcoded string literals in `MigrateSchema` (lines 133, 141, 149, 155) — it is not user-supplied and poses no injection risk in the current code. However, the pattern deviates from the parameterised-query convention and would be dangerous if `AlterTableAddColumns` were called from outside the migration path. SQLite `ALTER TABLE ADD COLUMN` does not support parameter binding for column definitions, so this pattern is unavoidable for DDL, but it should be documented:

```csharp
// NOTE: col is always a compile-time constant string from MigrateSchema.
// SQLite DDL does not support parameter binding for column definitions.
ac.CommandText = $"ALTER TABLE audit_log ADD COLUMN {col}";
```

---

### L2 — `EventFilter.Path` uses `instr()` scalar function, not an indexed column scan

**File:** `QueryRepository.cs:250`, `SqliteRepository.cs:88` (ix_audit_log_filepath exists)

```csharp
clauses.Add("instr(filepath, @path) > 0");
```

`instr(filepath, @path) > 0` is a full-table function scan — SQLite cannot use the `ix_audit_log_filepath` index for a substring match. For the typical Falcon use case (one job, hundreds to low thousands of rows), this is acceptable. If the row count per shard grows into the tens of thousands, this will scan every row. The existing index `ix_audit_log_filepath` only helps for exact equality (`filepath = @path`). For prefix searches (the common case — filtering by directory), a `LIKE @path || '%'` clause combined with `PRAGMA case_sensitive_like=ON` would allow index range scans. Document the limitation or switch to a prefix-aware filter.

---

### L3 — `QueryRepository` read operations are synchronous on the ASP.NET Core request thread

**File:** `QueryRepository.cs:66-67`, `134`, `144`, `180`, `209`, `233`

All read methods use synchronous `ExecuteReader()`, `ExecuteScalar()`, and `cmd.ExecuteReader()` (not their `Async` counterparts). Because these are invoked directly from minimal API endpoint handlers (e.g., `EventsEndpoints.cs:33`), the request's thread-pool thread is blocked for the duration of the SQLite read. For SQLite on local disk this is typically sub-millisecond and acceptable. For the `GetAllBaselines` full-table scan during catch-up (`SqliteRepository.cs:296-316`) and for `GetFileHistory` without LIMIT (M3 above), blocking can stretch to tens of milliseconds under load.

No immediate code change required, but the async-capable overloads (`ExecuteReaderAsync`, `ExecuteScalarAsync`, `ReadAsync`) should be preferred for consistency and future correctness — the repository already uses them in the write path. The read path in `SqliteRepository.cs` correctly uses `await cmd.ExecuteReaderAsync()` and `await r.ReadAsync()`, so the `QueryRepository` (web read path) is the outlier.

**Fix:** replace synchronous calls in `QueryRepository` read methods:

```csharp
// Before:
using var r = cmd.ExecuteReader();
while (r.Read()) { ... }

// After (and make the method async Task<...>):
using var r = await cmd.ExecuteReaderAsync();
while (await r.ReadAsync()) { ... }
```

---

### L4 — No WAL checkpoint or `PRAGMA wal_autocheckpoint` configuration

**File:** not present anywhere in the codebase.

SQLite WAL mode accumulates a WAL file that grows until a checkpoint runs. The default `wal_autocheckpoint(1000)` triggers a passive checkpoint after every 1000 pages written. For infrequent-write shards (one event per job arrival) this is fine. For high-frequency shards (continuous file modifications during a long job), the WAL file can grow to hundreds of MB before a checkpoint occurs, increasing read latency as readers must traverse the WAL. Consider adding a periodic PRAGMA checkpoint or setting a lower auto-checkpoint threshold on the write connection:

```csharp
// In SqliteRepository constructor, after the WAL verify:
using var ck = _conn.CreateCommand();
ck.CommandText = "PRAGMA wal_autocheckpoint=200;";
ck.ExecuteNonQuery();
```

---

### L5 — `GetConfigValue` is synchronous while all other config methods are async

**File:** `SqliteRepository.cs:349-358`

```csharp
public string? GetConfigValue(string key)
{
    try
    {
        using var cmd = _readConn.CreateCommand();
        ...
        return cmd.ExecuteScalar()?.ToString();
    }
```

This is called from `FileChangeHandler.cs:177` (`repo.IsInitialScanDone()`) on an async call path without acquiring `_writeLock`. The read connection is separate from the write connection, so there is no write-lock conflict, and WAL isolation ensures the read sees a consistent snapshot. However, the synchronous call on an `async` call chain forces a sync-over-async pattern and can block a thread-pool thread. The call occurs in hot-path per-file handling.

As an informational note rather than a blocking issue: if `IsInitialScanDone()` were to migrate to use the write connection in the future, this would require `_writeLock` and must be made async.

---

## Schema Compliance

### `audit_log` — Compliant

| Column | Type | Nullable | Notes |
|---|---|---|---|
| id | INTEGER PK AUTOINCREMENT | NOT NULL | Correct |
| changed_at | TEXT NOT NULL | NOT NULL | ISO-8601 UTC via `.ToString("O")` — correct |
| event_type | TEXT NOT NULL CHECK(...) | NOT NULL | Enum guard present |
| filepath | TEXT NOT NULL | NOT NULL | Correct |
| rel_filepath | TEXT NOT NULL | NOT NULL | Correct |
| module | TEXT NOT NULL | NOT NULL | Correct |
| owner_service | TEXT NOT NULL | NOT NULL | Correct |
| monitor_priority | TEXT NOT NULL CHECK(...) | NOT NULL | Enum guard present |
| machine_name | TEXT NOT NULL | NOT NULL | Correct |
| sha256_hash | TEXT NOT NULL | NOT NULL | Correct |
| old_content | TEXT NULL | nullable | Intentional — P1 only |
| diff_text | TEXT NULL | nullable | Intentional — P1 Modified only |
| file_description | TEXT NOT NULL DEFAULT '' | NOT NULL | Correct |
| change_summary | TEXT NOT NULL DEFAULT '' | NOT NULL | Correct |
| is_backfill | INTEGER NOT NULL DEFAULT 0 | NOT NULL | Correct (boolean as INTEGER) |
| old_filepath | TEXT NULL | nullable | Intentional — Renamed only |
| login_user | TEXT NULL | nullable | Intentional |
| setup_name | TEXT NULL | nullable | Intentional |
| recipe_name | TEXT NULL | nullable | Intentional |
| file_era | TEXT NULL | nullable | Intentional — legacy rows |

No issues.

### `file_baselines` — Compliant

`filepath TEXT PRIMARY KEY` is appropriate (exact equality lookups). `last_content TEXT NULL` is intentional (P1 only). The `ix_file_baselines_last_seen` index is present.

**Minor observation:** `file_baselines.last_content` stores the full previous file content. For P1 files up to `MaxContentBytes`, this means each baseline row can be very large. There is no explicit cap on `MaxContentBytes` in the schema; it is a runtime config value. If it defaults to, for example, 10 MB, a shard with 100 P1 files will carry up to 1 GB in `file_baselines`. Consider documenting that `MaxContentBytes` directly governs per-row storage in SQLite.

### `schema_meta` — Compliant

### `monitor_config` — Compliant

**Minor observation:** `SqliteRepository.cs:110` inserts the `created_at_utc` key into `schema_meta` rather than `monitor_config`, which appears to be a copy-paste error. The key is cosmetically misplaced but does not break functionality since `ListJobs` queries both tables with a UNION (QueryRepository.cs:76-77).

```csharp
// Line 110 — inserts into schema_meta (likely should be monitor_config):
INSERT OR IGNORE INTO schema_meta (key, value) VALUES
    ('created_at_utc', strftime('%Y-%m-%dT%H:%M:%fZ','now'));
```

---

## Index Coverage

All WHERE-clause columns used in hot read paths are covered by indexes created in `EnsureSchema`:

| Query pattern | Column | Index |
|---|---|---|
| Filter by module | `module` | `ix_audit_log_module` |
| Filter by priority | `monitor_priority` | `ix_audit_log_priority` |
| Filter by event type | `event_type` | `ix_audit_log_event_type` |
| Filter by machine | `machine_name` | `ix_audit_log_machine` |
| Filter by time range | `changed_at` | `ix_audit_log_changed_at` |
| Filter by service | `owner_service` | `ix_audit_log_owner_service` |
| File history | `rel_filepath` | `ix_audit_log_rel_filepath` |
| File era | `file_era` | `ix_audit_log_file_era` |
| Compound (module+time) | `module, changed_at` | `ix_audit_log_module_changed_at` |
| Path substring filter | `filepath` via `instr()` | NOT usable — see L2 |
| Baseline lookup | `filepath` (PK) | primary key |

No missing indexes for the current access patterns.

---

## Parameterised Queries

All user-facing query parameters (module, priority, service, eventType, machine, from, to, path, fileEra) are bound via `cmd.Parameters.AddWithValue(...)` in `BindFilter` — compliant.

The `{where}` interpolation in `GetEventsFromDb` (QueryRepository.cs:132, 140) uses only operator keywords and parameter placeholders built by `BuildWhere` — no user input reaches the interpolated string. Compliant, but see L1 for the DDL case.

---

## Disposal / Lifecycle

- `SqliteRepository` correctly disposes both connections and the semaphore in `Dispose()` (line 367-373).
- `ShardRegistry.Dispose()` disposes all shard repositories (line 78-83).
- `QueryRepository.Dispose()` disposes all cached read connections (line 272-275).
- `using` is applied to all `SqliteCommand` and `SqliteDataReader` objects in both classes.
- The race described in H1 means `CloseShard` can dispose a connection with an active reader — see H1.

---

## Recommendations (priority order)

1. **H1 fix (eviction race):** In all `lock(conn)` query sites in `QueryRepository`, catch `ObjectDisposedException` and `SqliteException` with error code `SqliteErrorCode.NotADatabase` and return empty results. Add a volatile `bool _disposed` to the connection wrapper and check it after acquiring `lock(conn)`.

2. **H2 fix (double-construction race):** Add a `private readonly object _createLock = new()` to `ShardRegistry` and guard the slow-path construction block with it (double-checked locking, lock only the construction, not the fast-path `TryGetValue`).

3. **M3 fix (unbounded GetFileHistory):** Add `LIMIT 500 OFFSET @offset` to the `GetFileHistory` query and expose a `?limit=&offset=` parameter on the endpoint.

4. **M4 fix (double reader on same connection):** In `ListJobs`, call `r.Close()` before opening the second `SqliteCommand`. This requires restructuring the nested `if(r.Read())` block to read all values from `r` first, then close, then open the monitor_config command.

5. **M1 fix (read connection synchronous pragma):** Add `PRAGMA synchronous=NORMAL` to the read connection PRAGMA string.

6. **L4 (WAL checkpoint):** Add `PRAGMA wal_autocheckpoint=200` to the write connection initialisation to bound WAL growth on busy shards.

7. **L3 (async reads):** Migrate `QueryRepository` read methods to use async overloads and declare the methods as `async Task<...>`, updating endpoint handler signatures accordingly.
