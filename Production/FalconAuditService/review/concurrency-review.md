# Concurrency Review — FalconAuditService

**Date:** 2026-05-05
**Reviewer:** concurrency-reviewer (Claude Sonnet)
**Language/Runtime:** C# / .NET 7 (Windows Service + ASP.NET Core minimal API)
**Design folder:** Not present — context derived from source files.

---

## Summary

| Severity | Count |
|---|---|
| Critical | 3 |
| High | 5 |
| Medium | 4 |
| Low | 3 |

---

## Critical Findings

---

### [SEVERITY: Critical] `SqliteRepository.cs:192-244` — Write semaphore acquired but `_disposed` check is outside the lock; use-after-dispose on the connection is possible

**Concurrency issue type:** TOCTOU on `_disposed` flag / use-after-free

**Problematic code:**
```csharp
// InsertAuditEventAsync (line 192-244)
public async Task InsertAuditEventAsync(AuditLogEntry e, FileBaseline baseline)
{
    if (_disposed) return;           // <── check (line 192)
    await _writeLock.WaitAsync();    // <── context switch / await here
    try
    {
        using var tx = _conn.BeginTransaction();  // <── _conn may be disposed
        ...
    }
    finally { _writeLock.Release(); }
}

// Dispose (line 367-373)
public void Dispose()
{
    _disposed = true;
    _writeLock.Dispose();   // <── disposes semaphore
    _conn.Dispose();
    _readConn.Dispose();
}
```

**Failure scenario:** A writer checks `_disposed == false`, then `ShardEvictionService` calls `repo.Dispose()` on another thread (or after a context switch during `WaitAsync`). The writer now holds a `WaitAsync` on a disposed `SemaphoreSlim`, which throws `ObjectDisposedException`, or proceeds to call `_conn.BeginTransaction()` on a disposed `SqliteConnection`, corrupting the DB or throwing.

The same pattern exists in `UpsertBaselineAsync` (line 249), `DeleteBaselineAsync` (line 319), and `SetConfigValueAsync` (line 335).

**Fix:**
```csharp
public async Task InsertAuditEventAsync(AuditLogEntry e, FileBaseline baseline)
{
    if (_disposed) return;
    try
    {
        await _writeLock.WaitAsync();
    }
    catch (ObjectDisposedException) { return; }   // disposed between check and wait
    try
    {
        if (_disposed) { _writeLock.Release(); return; }  // re-check under lock
        using var tx = _conn.BeginTransaction();
        // ...
        tx.Commit();
    }
    finally { _writeLock.Release(); }
}
```
Apply the same pattern to `UpsertBaselineAsync`, `DeleteBaselineAsync`, `SetConfigValueAsync`.

---

### [SEVERITY: Critical] `SqliteRepository.cs:273-293` — `_readConn` is shared across concurrent HTTP request threads with no synchronisation

**Concurrency issue type:** Shared mutable state — unsynchronised concurrent use of a `SqliteConnection`

**Problematic code:**
```csharp
// GetBaselineAsync (line 273-293) — called from FileChangeHandler (multiple consumer tasks)
public async Task<FileBaseline?> GetBaselineAsync(string filepath)
{
    using var cmd = _readConn.CreateCommand();   // shared connection, no lock
    cmd.CommandText = "...";
    cmd.Parameters.AddWithValue("@fp", filepath);
    using var r = await cmd.ExecuteReaderAsync();
    ...
}

// GetAllBaselinesAsync (line 295-316) — called from CatchUpScanner (parallel Task.WhenAll)
public async Task<List<FileBaseline>> GetAllBaselinesAsync()
{
    using var cmd = _readConn.CreateCommand();   // same shared connection
    ...
}
```

**Failure scenario:** `FileMonitorService` starts `workerCount = Math.Max(2, ProcessorCount)` concurrent consumer tasks (line 44-46). Each calls `FileChangeHandler.HandleAsync` → `repo.GetBaselineAsync` concurrently on the same `SqliteRepository` instance, sharing `_readConn` with no locking. `Microsoft.Data.Sqlite` connections are not thread-safe; concurrent calls on one connection produce `InvalidOperationException` or silent data corruption at the SQLite level.

`GetConfigValue` (line 349) has the same problem — it is called from `JobOriginChecker` and `IsInitialScanDone()` from both `FileChangeHandler` and `CatchUpScanner`.

**Fix — option A (simplest):** Wrap all `_readConn` usages in `lock (_readLock)`. Because these methods use `ExecuteReaderAsync`, switch to synchronous calls inside the lock or use a `SemaphoreSlim(1,1)` in place of `lock` to keep async correctness:
```csharp
private readonly SemaphoreSlim _readLock = new(1, 1);

public async Task<FileBaseline?> GetBaselineAsync(string filepath)
{
    await _readLock.WaitAsync();
    try
    {
        using var cmd = _readConn.CreateCommand();
        // ...
        using var r = await cmd.ExecuteReaderAsync();
        // ...
    }
    finally { _readLock.Release(); }
}
```
**Fix — option B (higher throughput):** Open a new connection per read call (WAL mode allows concurrent readers from separate connections without blocking the single writer).

---

### [SEVERITY: Critical] `QueryRepository.cs:27-48` — Double-checked locking on `ConcurrentDictionary` is broken; a disposed connection can be handed to a caller after `CloseShard`

**Concurrency issue type:** TOCTOU between `CloseShard` eviction and `GetConnection` reader; use-after-dispose

**Problematic code:**
```csharp
// CloseShard (line 19-25) — called from ShardEvictionService on any thread
public void CloseShard(string dbPath)
{
    if (_connections.TryRemove(dbPath, out var conn))
    {
        try { conn.Dispose(); } catch { }
    }
}

// GetConnection (line 27-48) — called from all HTTP query methods
private SqliteConnection? GetConnection(string dbPath)
{
    if (_connections.TryGetValue(dbPath, out var existing)) return existing;  // (A)
    lock (_connLock)
    {
        if (_connections.TryGetValue(dbPath, out existing)) return existing;  // (B)
        // ... create new connection, store it
        _connections[dbPath] = conn;
        return conn;
    }
}
```

**Failure scenario:** Thread 1 reaches (A), reads a non-null `existing`. Thread 2 calls `CloseShard` and disposes it. Thread 1 returns the now-disposed connection to a caller which calls `ExecuteReader` on it, throwing `ObjectDisposedException` or producing corrupted query results. The `lock (_connLock)` only guards creation, not the `TryGetValue` fast-path.

**Fix:** Replace the pattern with a lock that covers both the lookup and the usage, or replace `SqliteConnection` references with a wrapper that tracks `IsDisposed` and re-opens on disposal:
```csharp
private SqliteConnection? GetConnection(string dbPath)
{
    lock (_connLock)
    {
        if (_connections.TryGetValue(dbPath, out var existing)) return existing;
        // ... create ...
        _connections[dbPath] = conn;
        return conn;
    }
}
```
All callers already acquire `lock (conn)` before executing commands, so placing the lookup inside `_connLock` is sufficient — the per-connection `lock (conn)` blocks command execution while `CloseShard` is disposing.

---

## High Findings

---

### [SEVERITY: High] `FileMonitorService.cs:63` — `Stop()` calls `StopAsync().GetAwaiter().GetResult()` — potential deadlock

**Concurrency issue type:** Sync-over-async — `.GetAwaiter().GetResult()` on async method

**Problematic code:**
```csharp
// FileMonitorService.cs line 63
public void Stop() => StopAsync().GetAwaiter().GetResult();
```
`Worker.StopAsync` (Worker.cs:100) calls `await _monitor.StopAsync()`, so this synchronous overload is only reached if a non-async caller invokes it. However the method is `public`, and on a thread pool thread without a synchronisation context a deadlock is unlikely — but `StopAsync` itself calls `await Task.WhenAll(_consumers).WaitAsync(...)` which schedules continuations. If this is ever called from a context that has a single-threaded scheduler (e.g., a UI dispatcher or a test runner with `SynchronizationContext`) the blocking call will deadlock.

**Fix:** Remove `Stop()` entirely and replace all callers with `await StopAsync()`. The only call site is `Worker.StopAsync` which already awaits the async overload.

---

### [SEVERITY: High] `ManifestManager.cs:48-101` — `RecordArrival` and `RecordDeparture` use synchronous `sem.Wait()` on the calling thread, which may be a ThreadPool thread

**Concurrency issue type:** Synchronous block on async-capable thread-pool thread; can starve the thread pool under high job-arrival rates

**Problematic code:**
```csharp
// RecordArrival (line 61-100)
var sem = LockFor(manifestPath);
sem.Wait();   // <── synchronous block
try { ... }
finally { sem.Release(); }

// RecordDeparture (line 111-129)
sem.Wait();   // <── synchronous block
```

**Failure scenario:** `DirectoryWatcher.OnCreated` / `Worker.StopAsync` fire on a thread-pool thread. Each one blocks that thread-pool thread for the duration of the file I/O inside the semaphore. Under rapid job creation (e.g., batch copy of many job folders) all available thread-pool threads can be occupied waiting for manifests, starving the `Channel<ChangeEvent>` consumer tasks and the ASP.NET Core request pipeline.

**Fix:** Make both methods `async Task` and use `await sem.WaitAsync()`:
```csharp
public async Task RecordArrivalAsync(string jobPath, string machineName)
{
    ...
    var sem = LockFor(manifestPath);
    await sem.WaitAsync();
    try { ... }
    finally { sem.Release(); }
}
```
Update call sites accordingly: `DirectoryWatcher.OnCreated` callback should be an `async` lambda or dispatch to a `Channel`; `Worker.StopAsync` should `await` the call.

---

### [SEVERITY: High] `Program.cs:83-92` / `JobOriginChecker.cs:92-103` — Fire-and-forget `Task.Run` with silent `catch (Exception) { }` — exceptions fully swallowed

**Concurrency issue type:** Fire-and-forget with swallowed exceptions

**Problematic code (Program.cs lines 83-92):**
```csharp
if (repo is not null)
    _ = Task.Run(async () =>
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(config.JobSettleTimeSeconds));
            if (!repo.IsInitialScanDone())
                await repo.SetInitialScanDoneAsync();
        }
        catch (Exception) { }   // <── entire exception class swallowed silently
    });
```

**Problematic code (JobOriginChecker.cs lines 92-103):**
```csharp
_ = Task.Run(async () =>
{
    try
    {
        await Task.Delay(TimeSpan.FromSeconds(_config.JobSettleTimeSeconds), cts.Token);
        _pending.TryRemove(jobName, out _);
        await DetermineAndRecordAsync(jobName, jobPath, isRetry: true);
    }
    catch (OperationCanceledException) { }   // OK — but nothing else
});
// No catch for Exception — unhandled task exception absorbed by GC finaliser
```

**Failure scenario (Program.cs):** If `repo.SetInitialScanDoneAsync()` throws (e.g., `ObjectDisposedException` during shutdown, or SQLite error), the error is silently swallowed. The job's `initial_scan_done` flag is never set, so every future FSW event for that job is classified `"JobInit"` instead of `"Runtime"`, permanently corrupting the audit era label for all subsequent events.

**Failure scenario (JobOriginChecker.cs):** Any non-cancellation exception from `DetermineAndRecordAsync` (e.g. IO error, SQLite error) is absorbed silently and the origin is never recorded.

**Fix:**
```csharp
catch (OperationCanceledException) { /* expected */ }
catch (Exception ex) { _logger.LogError(ex, "Settle timer failed for job '{J}'.", jobName); }
```

---

### [SEVERITY: High] `ChangeDescriptionEnricher.cs:75-78` — `_debounce` Timer replaced without disposing the old one under concurrent FSW events

**Concurrency issue type:** Race condition on shared mutable `Timer` field; resource leak

**Problematic code:**
```csharp
_watcher.Changed += (_, _) =>
{
    _debounce?.Dispose();                              // (A) read-then-dispose
    _debounce = new Timer(_ => Load(configPath),       // (B) create-then-store
                          null, 1000, Timeout.Infinite);
};
```

**Failure scenario:** Two rapid file-system `Changed` events fire concurrently (FSW raises events on thread-pool threads). Thread 1 executes (A) — disposes the old timer. Thread 2 also executes (A) on a non-null value, double-disposes it. Then both threads execute (B), storing two different timers — the first one is immediately orphaned and never disposed. Both timers fire, calling `Load` twice concurrently. `Load` calls `Interlocked.Exchange(ref _map, ...)` so the final map is consistent, but the double-load is wasteful and the orphaned timer is a resource leak.

The identical pattern exists in `FileClassifier.cs:108-110`.

**Fix:** Use `Interlocked.Exchange` to swap atomically:
```csharp
_watcher.Changed += (_, _) =>
{
    var old = Interlocked.Exchange(ref _debounce,
                  new Timer(_ => Load(configPath), null, 1000, Timeout.Infinite));
    old?.Dispose();
};
```
Declare `_debounce` as `volatile Timer?` (or use `Interlocked`-compatible access).

---

### [SEVERITY: High] `ShardEvictionService.cs:43-59` — CTS replace sequence is not atomic; two concurrent `Schedule` calls can both register and both run eviction

**Concurrency issue type:** Non-atomic read-modify-write on `_pending` dictionary; TOCTOU in debounce replace

**Problematic code:**
```csharp
public void Schedule(string jobName, string jobPath)
{
    if (_pending.TryRemove(jobName, out var prior))
    {
        prior.Cancel();
        prior.Dispose();
    }

    var cts = new CancellationTokenSource();
    if (!_pending.TryAdd(jobName, cts))   // <── gap between TryRemove and TryAdd
    {
        cts.Dispose();
        return;
    }
    _ = Task.Run(() => EvictAfterGraceAsync(jobName, jobPath, cts));
}
```

**Failure scenario:** Two concurrent `Deleted` FSW events arrive for the same job. Thread 1 calls `TryRemove` (succeeds, no prior). Thread 2 calls `TryRemove` (also succeeds, also no prior — the key is already gone). Both threads then call `TryAdd` — one succeeds, the other returns and disposes its CTS. But between the `TryRemove` of Thread 1 and the `TryAdd` of Thread 1, Thread 2 may have already inserted its own CTS. Thread 1's `TryAdd` then fails and discards its CTS even though Thread 2's eviction task is now racing with a new Schedule call. The `finally` block re-check (`ReferenceEquals(current, cts)`) partially mitigates double-eviction but does not prevent the first eviction from proceeding while a `Cancel` that should have stopped it never arrives.

**Fix:** Use `AddOrUpdate` to atomically cancel-and-replace:
```csharp
public void Schedule(string jobName, string jobPath)
{
    var newCts = new CancellationTokenSource();
    var registered = _pending.AddOrUpdate(jobName, newCts, (_, old) =>
    {
        old.Cancel();
        old.Dispose();
        return newCts;
    });

    if (!ReferenceEquals(registered, newCts))
    {
        newCts.Dispose();   // lost the race — AddOrUpdate returned the existing CTS
        return;
    }
    _ = Task.Run(() => EvictAfterGraceAsync(jobName, jobPath, newCts));
}
```

---

## Medium Findings

---

### [SEVERITY: Medium] `FileMonitorService.cs:144-151` — Fire-and-forget `CatchUpScanner` re-trigger after queue overflow swallows exceptions

**Concurrency issue type:** Fire-and-forget without error handling

**Problematic code:**
```csharp
// TryEnqueueAsync (line 167)
_ = Task.Run(() => _catchUp.RunAllJobsParallelAsync(_ct));
```
And (line 149):
```csharp
_ = Task.Delay(_config.RecoveryDelayMs, _ct).ContinueWith(_ =>
{
    Interlocked.Exchange(ref _recoveryScheduled, 0);
    _logger.LogInformation("FSW overflow recovery: starting catch-up scan.");
    _ = _catchUp.RunAllJobsParallelAsync(_ct);   // <── no await, no error handler
}, TaskScheduler.Default);
```

**Failure scenario:** `RunAllJobsParallelAsync` is itself `async Task` — the inner call is fire-and-forget with no error handler. If the catch-up scan throws unexpectedly, the exception is silently lost and the FSW overflow is never reconciled, leaving the audit log permanently stale.

**Fix:**
```csharp
_ = Task.Run(async () =>
{
    try { await _catchUp.RunAllJobsParallelAsync(_ct); }
    catch (OperationCanceledException) { }
    catch (Exception ex) { _logger.LogError(ex, "Overflow recovery catch-up failed."); }
});
```

---

### [SEVERITY: Medium] `JobOriginChecker.cs:43-58` — `ScheduleCheck` CTS replace sequence has the same TOCTOU gap as `ShardEvictionService`

**Concurrency issue type:** Non-atomic read-modify-write on per-key CancellationTokenSource

**Problematic code:**
```csharp
public void ScheduleCheck(string jobName, string jobPath)
{
    if (_pending.TryRemove(jobName, out var old)) old.Cancel();   // (A)

    var cts = new CancellationTokenSource();
    _pending[jobName] = cts;   // (B)  — direct assignment, not TryAdd

    _ = Task.Run(async () => { ... });
}
```

**Failure scenario:** If two threads simultaneously call `ScheduleCheck` for the same job (concurrent `DirectoryWatcher.OnCreated` events), both execute (A) (both remove the prior CTS), both create a new CTS, and both execute (B) — the second write silently overwrites the first. The first fire-and-forget task retains a reference to the first CTS which is no longer in `_pending`, so `CancelCheck` cannot cancel it. Two simultaneous origin-check timers now run for the same job; the second overwrites the job_origin written by the first, potentially with a stale or wrong value.

Neither CTS created in this race is `Dispose()`d correctly: the overwritten one is orphaned.

**Fix:** Apply the same `AddOrUpdate` atomic pattern described for `ShardEvictionService`, and call `old.Dispose()` after cancellation.

---

### [SEVERITY: Medium] `CatchUpScanner.cs:84-93` — `RunAsync` guard semaphore `_guard` is bypassed by `RunAllJobsParallelAsync` and `RunJobAsync`

**Concurrency issue type:** Inconsistent use of single-flight guard

**Problematic code:**
```csharp
// RunAsync uses the guard (line 84-93)
public async Task RunAsync(string watchPath, CancellationToken ct, string? jobPath = null)
{
    if (!await _guard.WaitAsync(0)) { _logger.LogWarning("CatchUpScanner: already running — skipping."); return; }
    try   { await CoreAsync(watchPath, ct, jobPath); }
    finally { _guard.Release(); }
}

// RunAllJobsParallelAsync does NOT use the guard (line 41-63)
public async Task RunAllJobsParallelAsync(CancellationToken ct)
{
    ...
    await Task.WhenAll(tasks);   // directly calls CoreAsync via RunJobAsync
}
```

**Failure scenario:** Both `Worker.ExecuteAsync` (startup) and `FileMonitorService.OnError` (overflow) call `RunAllJobsParallelAsync` concurrently. There is no guard preventing two simultaneous full scans from running in parallel. Each scan opens the same shards, reads overlapping baselines, and writes audit events — potentially creating duplicate `Created`/`Modified` audit log rows for the same file within the same second.

**Fix:** Either route `RunAllJobsParallelAsync` through `_guard`, or document clearly that the guard is a per-call guard only for `RunAsync` and not intended for parallel job scans. If duplicates are acceptable (idempotent upserts), document this. If not:
```csharp
public async Task RunAllJobsParallelAsync(CancellationToken ct)
{
    if (!await _guard.WaitAsync(0)) { _logger.LogWarning("CatchUpScanner: already running — skipping."); return; }
    try   { /* existing body */ }
    finally { _guard.Release(); }
}
```

---

### [SEVERITY: Medium] `SqliteRepository.cs:367-373` — `_writeLock` disposed before connections, leaving any blocked `WaitAsync` with no `Release` path

**Concurrency issue type:** Incorrect disposal order within `Dispose()`

**Problematic code:**
```csharp
public void Dispose()
{
    _disposed = true;
    _writeLock.Dispose();   // <── disposes semaphore first
    _conn.Dispose();
    _readConn.Dispose();
}
```

**Failure scenario:** A writer task is currently awaiting `_writeLock.WaitAsync()`. `Dispose()` sets `_disposed = true`, then disposes the semaphore. The waiting `WaitAsync` throws `ObjectDisposedException` instead of returning cleanly. The `finally { _writeLock.Release(); }` block is never reached for that waiter (the exception propagates). A second concurrent writer that entered its `try` block before `Dispose` and is mid-transaction will now call `Release()` on a disposed semaphore — another `ObjectDisposedException`.

**Fix:** Reverse disposal order and catch `ObjectDisposedException` in `Release`:
```csharp
public void Dispose()
{
    _disposed = true;
    // Allow in-progress writes to complete before closing connections
    _writeLock.Wait();          // drain the single permit
    _conn.Dispose();
    _readConn.Dispose();
    _writeLock.Release();
    _writeLock.Dispose();
}
```
Or use a `CancellationTokenSource` to signal shutdown and await all pending writes before disposing.

---

## Low Findings

---

### [SEVERITY: Low] `JobsEndpoints.cs:17-20` — `File.ReadAllText` on manifest.json without any concurrency protection

**Concurrency issue type:** Unprotected file read racing with `ManifestManager.WriteManifest` (atomic rename)

**Problematic code:**
```csharp
var json = File.ReadAllText(manifestPath);
return Results.Content(json, "application/json");
```

**Failure scenario:** `ManifestManager.WriteManifest` writes to a `.tmp` file then does `File.Move(..., overwrite: true)`. On NTFS this is atomic at the OS level, but on network shares or FAT32 volumes (the code itself warns about this) the move is not atomic. The HTTP GET can read a zero-length file or partial content during the brief window of the non-atomic move. The result is a 500 or a malformed JSON response.

**Fix:** Use `ManifestManager.ReadManifest(jobPath)` which already handles `try/catch` and always reads the finished file, and serialize the result through `ManifestManager`'s own `SemaphoreSlim` to avoid the race entirely.

---

### [SEVERITY: Low] `FileMonitorService.cs:26` — `_ct` field written in `Start()` and read by multiple consumers; no volatile keyword or memory barrier

**Concurrency issue type:** Visibility of shared field across threads

**Problematic code:**
```csharp
private CancellationToken _ct;   // line 26

public void Start(CancellationToken ct)
{
    _ct = ct;   // written on the caller thread
    ...
    _consumers = Enumerable.Range(0, workerCount)
                 .Select(_ => Task.Run(ConsumeAsync, ct))   // ct captured by value — OK
                 .ToArray();
}
```
`_ct` is also used directly in `OnError` (line 145) and `TryEnqueueAsync` (line 158) from thread-pool threads. In practice, `Start` always runs before any event fires, but without `volatile` the JIT is permitted to cache the field in a register on the reading threads.

**Fix:** Declare `private volatile CancellationToken _ct;` — or, better, remove `_ct` as a field entirely and instead store a `CancellationTokenSource` that can be cancelled during `StopAsync`, and read `.Token` on demand.

---

### [SEVERITY: Low] `JobDiscoveryService.cs:8` — `_knownJobs` declared `volatile IReadOnlyList<string>` but reassigned inside a non-locked `Refresh()` method — multiple threads can observe a stale list between refresh cycles

**Concurrency issue type:** Non-atomic list replacement visible to concurrent HTTP request threads

**Problematic code:**
```csharp
private volatile IReadOnlyList<string> _knownJobs = Array.Empty<string>();

public void Refresh()
{
    ...
    var jobs = Directory.EnumerateDirectories(...)...ToList();
    _knownJobs = jobs;   // atomic reference assignment — OK
}
```

This is actually safe because `IReadOnlyList<string>` is immutable once assigned and the assignment is a single reference write (atomic on .NET). The `volatile` keyword ensures all threads see the latest reference. This is a correct lock-free pattern. **No action required** — listed here only to document that the pattern was reviewed.

---

## Clean Areas (no concurrency findings)

- **`ContentCache.cs`** — Correct LRU implementation; all public methods (`Set`, `Get`, `Remove`) are fully guarded by a single `lock (_lock)`.
- **`ShardRegistry.cs`** — `GetOrCreate` correctly handles the create-race via `GetOrAdd`; the loser disposes its unused `SqliteRepository`. `Remove` uses `TryRemove` atomically.
- **`DirectoryWatcher.cs`** — Correctly delegates all processing out of the FSW callback; no blocking I/O inline; the `onArrived`/`onDeparted` lambdas registered in `Program.cs` dispatch asynchronous work through `Task.Run`.
- **`FileChangeHandler.cs`** — Is correctly invoked only from `ConsumeAsync` tasks in `FileMonitorService`; not shared across threads directly.
- **`Worker.cs`** — `ExecuteAsync` respects `stoppingToken` via `Task.Delay(Timeout.Infinite, stoppingToken)`. `StopAsync` drains in order. The catch-up fire-and-forget (line 60-79) has appropriate error logging and cancellation token linkage (linked to both `stoppingToken` and a 5-minute timeout).
- **`FileClassifier.cs`** — `_rules` published via `Interlocked.Exchange` to an `ImmutableList`; `Classify` takes a local snapshot. Correctly lock-free for reads. (The `_reloadDebounce` timer race noted in High findings is the only issue.)
- **`ChangeDescriptionEnricher.cs`** — `_map` published via `Interlocked.Exchange` to an `ImmutableDictionary`; `Enrich` takes a local snapshot. Correctly lock-free for reads. (The `_debounce` timer race noted in High findings is the only issue.)
- **`ManifestManager.cs`** — Per-path `SemaphoreSlim` from `ConcurrentDictionary.GetOrAdd` is a sound pattern; all async write paths use `WaitAsync`/`Release` in `finally`. The sync `sem.Wait()` paths are flagged separately.
- **`CatchUpScanner.cs (CoreAsync)`** — `EraForFile` uses a local `Dictionary<string, bool>` (`jobInitFlags`) that is never shared; no concurrency issues within a single scan invocation.
- **`Endpoints`** — No `async void`; all endpoint handlers are synchronous delegates or `IResult`-returning methods; no `.Result`/`.Wait()` calls.
