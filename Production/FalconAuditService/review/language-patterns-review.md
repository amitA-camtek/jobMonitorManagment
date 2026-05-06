# Language Patterns Review — FalconAuditService
**Runtime:** net7.0-windows (Windows Service + ASP.NET Core)  
**Primary language:** C#  
**Nullable:** enable  
**Reviewed:** 2026-05-05  
**Source root:** `C:\Amit\jobMonitorManagment\Production\FalconAuditService\FalconAuditWebServer\`

---

## Critical

### [CRITICAL] `Program.cs:91` — Silent swallow of `OperationCanceledException` in fire-and-forget Task.Run hides settle-window failures

```csharp
_ = Task.Run(async () =>
{
    try
    {
        await Task.Delay(TimeSpan.FromSeconds(config.JobSettleTimeSeconds));
        if (!repo.IsInitialScanDone())
            await repo.SetInitialScanDoneAsync();
    }
    catch (Exception) { }   // ← swallows everything silently
});
```

**Risk:** Any exception thrown by `SetInitialScanDoneAsync` (e.g. SQLite error, `ObjectDisposedException` when the shard was evicted before the timer fired) is swallowed with no log entry. `FileEra` will never flip to `"Runtime"` for that job, and the failure is invisible.

**Fix:** At minimum log the exception and filter out `OperationCanceledException` as a non-error:
```csharp
catch (OperationCanceledException) { /* normal — job departed before settle */ }
catch (Exception ex)
{
    Log.Warning(ex, "Settle-window SetInitialScanDone failed for job.");
}
```

---

### [CRITICAL] `ManifestManager.cs:62,112` — Synchronous `sem.Wait()` blocks an async-capable thread

```csharp
// RecordArrival (line 62) and RecordDeparture (line 112)
var sem = LockFor(manifestPath);
sem.Wait();   // blocking wait on a SemaphoreSlim inside what may be a thread-pool call
```

**Risk:** `RecordArrival` and `RecordDeparture` are called from FSW event callbacks and from `Worker.StopAsync`. In both cases the caller is on a thread-pool thread. Calling the synchronous `sem.Wait()` on a `SemaphoreSlim` that is already held (e.g. by `IncrementEventsAsync` or `UpdateOriginAsync`) will deadlock if the holding task is awaiting something that needs a thread-pool thread to complete, which is common in a bounded-thread-pool scenario under load.

**Fix:** Convert `RecordArrival` and `RecordDeparture` to `async Task` and use `await sem.WaitAsync()`. Callers (`Worker.StopAsync`, `DirectoryWatcher` callbacks) should also be made async or fire-and-forget appropriately.

---

### [CRITICAL] `JobOriginChecker.cs:44,47` and `JobOriginChecker.cs:90,92` — `CancellationTokenSource` cancelled without `Dispose`

```csharp
// ScheduleCheck, line 44-47
if (_pending.TryRemove(jobName, out var old)) old.Cancel();   // no old.Dispose()

var cts = new CancellationTokenSource();
_pending[jobName] = cts;   // not TryAdd — overwrites without disposing racing entry
```

And in the retry path (lines 90-92):
```csharp
if (_pending.TryRemove(jobName, out var old)) old.Cancel();  // no Dispose
var cts = new CancellationTokenSource();
_pending[jobName] = cts;   // same pattern
```

**Risk (two problems):**
1. `old.Cancel()` without `old.Dispose()` leaks the timer handle inside `CancellationTokenSource`. On a machine where jobs arrive/depart frequently this is a continuous leak.
2. Using `_pending[jobName] = cts` (indexer assignment) instead of `TryAdd` is non-atomic on `ConcurrentDictionary`. If two threads race on the same `jobName` (possible when the FSW fires rapidly), one CTS is silently overwritten and never cancelled or disposed. Use `TryAdd`; if it fails, dispose the losing `cts` immediately.

**Fix:**
```csharp
if (_pending.TryRemove(jobName, out var old)) { old.Cancel(); old.Dispose(); }
var cts = new CancellationTokenSource();
if (!_pending.TryAdd(jobName, cts))
{
    cts.Dispose();
    return;
}
```

---

## High

### [HIGH] `FileMonitorService.cs:63` — Synchronous blocking on async method risks deadlock

```csharp
public void Stop() => StopAsync().GetAwaiter().GetResult();
```

**Risk:** `Stop()` is kept for "backward compat" but is never called from `Worker.cs` — `Worker.StopAsync` calls `await _monitor.StopAsync()` directly (line 100). However, if any future caller invokes `Stop()` from a context that has a synchronisation context (e.g. ASP.NET Core request pipeline), the `.GetResult()` will deadlock because `StopAsync` itself awaits `Task.WhenAll`.

**Fix:** Remove the synchronous `Stop()` method entirely (it has no callers in the current codebase). Add a comment on `StopAsync` that it is the only shutdown entry point.

---

### [HIGH] `ManifestManager.cs` — `SemaphoreSlim` entries accumulate indefinitely in `_locks`

```csharp
private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks =
    new(StringComparer.OrdinalIgnoreCase);

private SemaphoreSlim LockFor(string manifestPath) =>
    _locks.GetOrAdd(manifestPath, _ => new SemaphoreSlim(1, 1));
```

**Risk:** A `SemaphoreSlim` is created for every unique `manifestPath` ever seen. Jobs come and go; their entries are never removed from `_locks` and the `SemaphoreSlim` objects are never disposed. On a long-running service managing many transient jobs this is a slow, unbounded leak of both managed and unmanaged (WaitHandle) resources.

**Fix:** Remove the entry in `RecordDeparture` after the semaphore is released:
```csharp
finally
{
    sem.Release();
    if (_locks.TryRemove(manifestPath, out _)) { /* disposed inline or just abandoned — SemaphoreSlim finalizer reclaims */ }
}
```
Alternatively, move to a `Dictionary<string, SemaphoreSlim>` protected by a single lock and dispose-on-remove.

---

### [HIGH] `FileMonitorService.cs:186-190` — `Dispose()` does not stop consumer tasks; disposed timers race with `FireDebounce`

```csharp
public void Dispose()
{
    _watcher?.Dispose();
    foreach (var t in _debounce.Values) t.Dispose();
    _latestEvent.Clear();
}
```

**Risk:**
1. `Dispose()` does not call `_queue.Writer.TryComplete()` or wait for `_consumers`. Consumer tasks keep running (reading from a channel whose writer is still open) after the object is disposed.
2. A `Timer` in `_debounce` may already be firing `FireDebounce` on a thread pool thread at the exact moment `Dispose()` enumerates and disposes it. `FireDebounce` then calls `TryEnqueueAsync` on a potentially disposed channel, which throws `ChannelClosedException`.

**Fix:** `Dispose()` should complete the writer and await consumers before disposing timers, matching `StopAsync`:
```csharp
public void Dispose()
{
    _watcher?.Dispose();
    _queue.Writer.TryComplete();
    foreach (var t in _debounce.Values) t.Dispose();
    _latestEvent.Clear();
    // Consumers are background tasks; accept their natural completion here.
}
```
Note: `StopAsync` is the primary shutdown path; if `Dispose` is never called separately (which appears to be the case — the DI container calls `Dispose` on `IDisposable` singletons at shutdown), the race only matters if someone calls `Dispose` directly.

---

### [HIGH] `ShardEvictionService.cs` — `ShardEvictionService` is not `IDisposable`; pending `CancellationTokenSource` objects are never cancelled on shutdown

```csharp
public class ShardEvictionService   // no IDisposable
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pending = ...;
```

**Risk:** When the host shuts down, any grace-period tasks still in flight (`EvictAfterGraceAsync`) will keep running until their delay expires. Each one holds a `CancellationTokenSource` that is never disposed. If the host forcibly aborts the process before the grace period expires, the `CancellationTokenSource` handles leak. More importantly, there is no cooperative cancellation on shutdown.

**Fix:** Implement `IDisposable`:
```csharp
public void Dispose()
{
    foreach (var cts in _pending.Values) { cts.Cancel(); cts.Dispose(); }
    _pending.Clear();
}
```
Register it with the DI container as `IDisposable` (ASP.NET Core disposes `IDisposable` singletons automatically).

---

### [HIGH] `LoginReader.cs:5` — Hard-coded absolute path is a magic string that should come from configuration

```csharp
private const string LoginFilePath = @"C:\bis\data\lastLogin.json";
```

**Risk:** The service cannot be deployed to a machine with a different BIS data path, and the path cannot be overridden in `appsettings.json` without a code change. `MonitorConfig` already holds `WatchPath`, `ClassificationRulesPath`, and `ParameterDescriptionsPath` from configuration — this should follow the same pattern.

**Fix:** Add `LoginFilePath` to `MonitorConfig` with the same default, inject `MonitorConfig` into `LoginReader`, and read the path from config.

---

### [HIGH] `JobManifest.cs:23,32,42` — `DateTime` without `DateTimeOffset` for cross-machine timestamps

```csharp
public DateTime? OriginDeterminedAt { get; set; }  // JobManifest
public DateTime At { get; set; }                    // MachineTimestamp
public DateTime From { get; set; }                  // HistoryEntry
public DateTime? To { get; set; }                   // HistoryEntry
```

**Risk:** These are serialised to JSON as bare `DateTime`. When deserialised, the `Kind` is `Unspecified`, which means `.ToLocalTime()` / comparisons will behave differently on different machines or after DST transitions. The existing comparisons in `JobOriginChecker.cs:129` (`DateTime.UtcNow - manifest.Created.At`) silently produce wrong durations when `At.Kind == Unspecified`.

**Fix:** Change all manifest timestamp fields to `DateTimeOffset`. Serialise/deserialise with `JsonSerializerOptions` that recognises ISO-8601 offsets. All write sites already use `DateTime.UtcNow` — replacing with `DateTimeOffset.UtcNow` is a one-line change per call site.

---

### [HIGH] `EventsEndpoints.cs:78` — `DateTime.Now` (local time) used in an API response that mixes UTC timestamps

```csharp
GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
```

**Risk:** All audit events store `changed_at` as UTC ISO-8601. `GeneratedAt` is emitted as local time with no timezone indication. Consumers comparing `GeneratedAt` against `changed_at` values will get subtly wrong durations, and the field is ambiguous to any client running in a different timezone.

**Fix:**
```csharp
GeneratedAt = DateTime.UtcNow.ToString("O"),
```

---

## Medium

### [MEDIUM] `Program.cs:83-92` — Fire-and-forget `Task.Run` for the settle window does not pass `stoppingToken`

```csharp
_ = Task.Run(async () =>
{
    try
    {
        await Task.Delay(TimeSpan.FromSeconds(config.JobSettleTimeSeconds));
        ...
    }
    catch (Exception) { }
});
```

**Risk:** The `Task.Run` call does not pass `stoppingToken` as the scheduler token, and the inner `Task.Delay` does not accept a cancellation token. When the service is stopping, these background settle tasks continue running for up to `JobSettleTimeSeconds` (default 30 s) after the host initiates shutdown, calling `repo.SetInitialScanDoneAsync()` on potentially already-disposed shards.

**Fix:**
```csharp
_ = Task.Run(async () =>
{
    try
    {
        await Task.Delay(TimeSpan.FromSeconds(config.JobSettleTimeSeconds),
                         stoppingToken);  // honour shutdown
        if (!repo.IsInitialScanDone())
            await repo.SetInitialScanDoneAsync();
    }
    catch (OperationCanceledException) { }
    catch (Exception ex)
    {
        Log.Warning(ex, "Settle-window SetInitialScanDone failed.");
    }
}, stoppingToken);
```

---

### [MEDIUM] `FileClassifier.cs:108-110` — `_reloadDebounce` Timer replaced without disposing the previous instance

```csharp
_configWatcher.Changed += (_, _) =>
{
    _reloadDebounce?.Dispose();
    _reloadDebounce = new Timer(_ => LoadRules(configPath), null, 1000, Timeout.Infinite);
};
```

**Risk:** Two FSW `Changed` events arriving within milliseconds can race on the read-then-write of `_reloadDebounce`. Thread A reads the old value and disposes it; thread B also reads the old value (already disposed) and disposes it again (harmless but confusing). Then both threads create a new `Timer` and write it to `_reloadDebounce`; one timer is overwritten and never disposed. The pattern is the same in `ChangeDescriptionEnricher.cs:76-77`.

**Fix:** Use `Interlocked.Exchange` to atomically swap the timer:
```csharp
_configWatcher.Changed += (_, _) =>
{
    var old = Interlocked.Exchange(ref _reloadDebounce,
        new Timer(_ => LoadRules(configPath), null, 1000, Timeout.Infinite));
    old?.Dispose();
};
```
The `_reloadDebounce` field must be declared as `Timer?` (already done) and marked `volatile`, or the exchange pattern must be used consistently.

---

### [MEDIUM] `FileMonitorService.cs:143-151` — `Task.Run` for `_catchUp.RunAllJobsParallelAsync` is fire-and-forget with no error handling

```csharp
_ = Task.Delay(_config.RecoveryDelayMs, _ct).ContinueWith(_ =>
{
    Interlocked.Exchange(ref _recoveryScheduled, 0);
    _logger.LogInformation("FSW overflow recovery: starting catch-up scan.");
    _ = _catchUp.RunAllJobsParallelAsync(_ct);   // ← fire-and-forget, no await, no try/catch
}, TaskScheduler.Default);
```

And at line 167:
```csharp
_ = Task.Run(() => _catchUp.RunAllJobsParallelAsync(_ct));   // same issue in TryEnqueueAsync
```

**Risk:** If `RunAllJobsParallelAsync` throws (e.g. `ObjectDisposedException` at shutdown), the exception is silently dropped. The service has no record of the failed recovery scan.

**Fix:** Add error handling:
```csharp
_ = Task.Run(async () =>
{
    try { await _catchUp.RunAllJobsParallelAsync(_ct); }
    catch (OperationCanceledException) { }
    catch (Exception ex) { _logger.LogError(ex, "FSW overflow catch-up scan failed."); }
}, _ct);
```

---

### [MEDIUM] `SqliteRepository.cs:192-244` — `_writeLock` not released when `_disposed` early-return is taken without acquiring

The `_writeLock.WaitAsync()` is properly guarded with `try/finally { _writeLock.Release(); }` when acquired. However, the `if (_disposed) return;` guard check at lines 192, 250, 319, 336 runs **before** `WaitAsync`. This is correct, but there is a TOCTOU window: `_disposed` can be set to `true` **after** the check passes and **before** `WaitAsync` completes. The subsequent call to a disposed `_conn` will then throw `ObjectDisposedException` inside the `try` block — which is not caught — and will also fail to call `_writeLock.Release()` only if the exception escapes the `try` block before entering it.

**Risk:** Low in practice because disposal only happens at service shutdown. But if a write is in-flight at the exact moment `Dispose()` is called, the sequence is: `WaitAsync` completes → `_disposed` is set → `_conn.BeginTransaction()` throws `ObjectDisposedException` → exception propagates up through the consumer task with no log, crashing the consumer.

**Fix:** Catch `ObjectDisposedException when (_disposed)` in each write method and return silently, matching the `GetBaselineAsync` pattern that already does `catch (Exception) when (_disposed)`.

---

### [MEDIUM] `QueryRepository.cs:86` — Silent swallow in `ListJobs` when reading `monitor_config`/`schema_meta`

```csharp
catch { /* tables may not exist on very old shards */ }
```

**Risk:** This bare `catch` swallows all exceptions, not just `SqliteException`. If a programming error or `ObjectDisposedException` is thrown here, it is silently ignored and `origin`/`jobCreatedAt` are returned as `null`, making the job appear to have no origin data when it actually does.

**Fix:** Narrow the catch to `SqliteException`:
```csharp
catch (SqliteException) { /* tables may not exist on very old shards */ }
```

---

### [MEDIUM] `ManifestManager.cs` — `SemaphoreSlim` not disposed on `Dispose()` (class is not `IDisposable` at all)

`ManifestManager` creates `SemaphoreSlim` instances in its `_locks` dictionary but has no `Dispose()` method. `SemaphoreSlim` implements `IDisposable` (it holds an event-wait-handle). At service shutdown, none of these handles are released.

**Fix:** Implement `IDisposable`:
```csharp
public void Dispose()
{
    foreach (var sem in _locks.Values) sem.Dispose();
    _locks.Clear();
}
```

---

### [MEDIUM] `CatchUpScanner.cs:14` — `_guard` SemaphoreSlim not disposed

```csharp
private readonly SemaphoreSlim _guard = new(1, 1);
```

`CatchUpScanner` does not implement `IDisposable` and never disposes `_guard`.

**Fix:** Implement `IDisposable` with `_guard.Dispose()`.

---

### [MEDIUM] `JobOriginChecker.cs:200-204` — `Dispose()` cancels but does not dispose the CancellationTokenSource values

```csharp
public void Dispose()
{
    foreach (var cts in _pending.Values) cts.Cancel();
    _pending.Clear();
}
```

**Risk:** `cts.Cancel()` is called but `cts.Dispose()` is not. Each `CancellationTokenSource` holds an unmanaged timer handle that is not freed.

**Fix:**
```csharp
public void Dispose()
{
    foreach (var cts in _pending.Values) { cts.Cancel(); cts.Dispose(); }
    _pending.Clear();
}
```

---

### [MEDIUM] `HashHelper.cs:26` — `Thread.Sleep` on a thread-pool thread inside a retry loop

```csharp
catch (IOException) when (attempt < MaxRetries - 1)
{
    Thread.Sleep(RetryDelayMs * (attempt + 1));
}
```

**Risk:** `ComputeSha256` is called from `FileChangeHandler.HandleAsync` which runs on the channel consumer tasks (thread-pool threads). Blocking a thread-pool thread for up to 300 ms per retry (3 retries × 100 ms base) under a high-churn scenario (many simultaneous file changes) degrades throughput. The workers are async but this synchronous sleep cannot be awaited.

**Fix:** `ComputeSha256` is already a static synchronous method called from async callers. Either keep it synchronous (the retry is short and the call is already offloaded from the hot path by the channel), or convert it to `async Task<string?>` with `await Task.Delay`.

---

### [MEDIUM] `FileMonitorService.cs:98` — Bare `catch` suppresses all exceptions from `Directory.Exists`

```csharp
try
{
    if (Directory.Exists(e.FullPath)) return;
}
catch { /* ignore — path may already be gone */ }
```

**Risk:** `Directory.Exists` does not throw for gone paths — it returns `false`. The only exception it throws is `PathTooLongException`, `ArgumentNullException`, or `ArgumentException` (programming errors), none of which should be silently swallowed. The comment is factually wrong.

**Fix:** Either remove the try/catch (it is not needed), or be explicit:
```csharp
// Directory.Exists returns false for gone paths; no try/catch needed.
if (Directory.Exists(e.FullPath)) return;
```

---

### [MEDIUM] `JobsEndpoints.cs:21` — Bare `catch` returns HTTP 500 with no logging

```csharp
catch { return Results.StatusCode(500); }
```

**Risk:** Any exception reading `manifest.json` is silently swallowed with no log entry, making it impossible to diagnose manifest corruption or access-denied errors in production.

**Fix:**
```csharp
catch (Exception ex)
{
    logger.LogWarning(ex, "Could not read manifest for {Job}.", jobName);
    return Results.StatusCode(500);
}
```
(The endpoint lambda would need to accept an `ILogger<JobsEndpoints>` from DI.)

---

## Low

### [LOW] `ChangeEvent.cs:7` — `DateTime DetectedAt` should be `DateTimeOffset`

```csharp
internal record ChangeEvent(
    string             FullPath,
    WatcherChangeTypes ChangeType,
    DateTime           DetectedAt,   // always assigned DateTime.UtcNow
    string?            OldPath = null
);
```

**Risk:** `DetectedAt` is always written as `DateTime.UtcNow` (e.g. `FileMonitorService.cs:124`, `:133`). Using `DateTime` instead of `DateTimeOffset` means the `Kind` is `Utc`, which is correct but brittle — any refactor that passes a local-time value will silently produce wrong timestamps. `DateTimeOffset` is self-describing.

**Fix:** Change `DateTime DetectedAt` to `DateTimeOffset DetectedAt` and update the two call sites to `DateTimeOffset.UtcNow`.

---

### [LOW] `DirectoryWatcher.cs:66,80` — Null-forgiving `!` operators on values already guarded by null check

```csharp
if (string.IsNullOrEmpty(e.Name)) return;
_onArrived(e.Name!, e.FullPath);   // Name is already known non-null here
```

```csharp
if (!string.IsNullOrEmpty(e.OldName)) _onDeparted(e.OldName!);
if (!string.IsNullOrEmpty(e.Name))    _onArrived(e.Name!, e.FullPath);
```

**Risk:** The `!` operators are unnecessary because the null/empty check immediately above guarantees non-null. They suppress nullability warnings without adding safety and could mask future refactors that remove the guard without removing the `!`.

**Fix:** Remove the `!` operators; the null-check guard is sufficient:
```csharp
_onArrived(e.Name, e.FullPath);
```

---

### [LOW] `ContentCache.cs` — Default 200 MB hard-coded in constructor default parameter

```csharp
public ContentCache(long maxBytes = 200L * 1024 * 1024)   // 200 MB default
```

`ContentCache` is registered as a singleton in `Program.cs` with `builder.Services.AddSingleton<ContentCache>()` which uses the parameterless DI constructor — so the 200 MB default is always used. `MonitorConfig` has `MaxContentBytes` (the per-file cap) but no `ContentCacheMaxBytes` setting. The cache size cannot be tuned without a code change.

**Fix:** Add `ContentCacheMaxBytes` to `MonitorConfig` (default `200 * 1024 * 1024`) and inject it into the `ContentCache` singleton factory in `Program.cs`.

---

### [LOW] `CatchUpScanner.cs` — `_guard` SemaphoreSlim used inconsistently

`RunAsync` acquires `_guard` (line 86); `RunJobAsync` and `RunAllJobsParallelAsync` do not. Callers of the latter two methods can execute concurrently with a `RunAsync` call, bypassing the single-runner guard. This is likely intentional (per-job runs and full runs are separate), but the field name `_guard` implies exclusive access that it does not actually enforce.

**Risk:** Low — the parallelism is managed at the per-shard level. But the naming is misleading.

**Fix:** Rename `_guard` to `_fullScanGuard` or document the scope clearly.

---

### [LOW] `FileHistoryEndpoints.cs:23` — Path normalisation converts forward slashes to backslashes before DB lookup, but DB stores paths with backslashes only on Windows

```csharp
var relPath = filePath.Replace('/', '\\');
```

This is fine on Windows but the pattern is fragile. The `rel_filepath` column was populated by `FileChangeHandler` using the result of `TrimStart('\\', '/')` which on Windows always produces backslash-separated paths. The endpoint normalisation is correct — but it is also undocumented and would silently fail on a case-sensitive filesystem if the path casing differs.

**Risk:** Low on Windows. Document the assumption or use `Path.GetFullPath` canonicalisation.

---

### [LOW] `QueryRepository.cs:64-65` — SQL query built with `GROUP_CONCAT(DISTINCT machine_name)` — no ORDER BY guarantee

```csharp
cmd.CommandText = "SELECT COUNT(*), MIN(changed_at), MAX(changed_at), GROUP_CONCAT(DISTINCT machine_name) FROM audit_log";
```

SQLite's `GROUP_CONCAT(DISTINCT ...)` does not guarantee any particular order of the concatenated values. This is unlikely to cause a bug (the result is displayed as an informational string), but worth noting.

---

## Clean areas

The following files/areas were reviewed and contain no findings:

| File | Notes |
|---|---|
| `Worker.cs` | BackgroundService lifecycle is correct. `ExecuteAsync` awaits `Task.Delay(Infinite, stoppingToken)` for clean shutdown. `StopAsync` calls `base.StopAsync` correctly. No I/O in `StartAsync`. |
| `SqliteRepository.cs` — connection disposal | Both `_conn` and `_readConn` are opened in the constructor and disposed in `Dispose()`. `_writeLock` is disposed. `_disposed` flag guards all write operations. WAL mode verification is thorough. |
| `ShardRegistry.cs` | Race between `GetOrCreate` and disposal handled correctly with the `GetOrAdd` + `ReferenceEquals` pattern. `Dispose()` iterates all shards. |
| `FileChangeHandler.cs` | No string interpolation in log calls. All log properties use PascalCase. Sensitive field (`OldContent`) is not logged. |
| `CatchUpScanner.cs` — core logic | Correct use of `ct.ThrowIfCancellationRequested()` in the main loops. Per-file `IOException` handling is appropriate. |
| `FileClassifier.cs` — classification | Thread-safe atomic publish via `Interlocked.Exchange` on `ImmutableList`. |
| `Program.cs` — top-level catch | `Log.Fatal(ex, ...)` correctly logs before `Log.CloseAndFlush()`. |
| `DiffHelper.cs` | No resource leaks. `StringBuilder`-based diff generation is clean. |
| `HashHelper.cs` — resource handling | `FileStream` and `SHA256` are both in `using` blocks. |
| `ChangeDescriptionEnricher.cs` — IDisposable | `_watcher` and `_debounce` both disposed in `Dispose()`. |
| All Models (`AuditLogEntry`, `AuditEventSummary`, `AuditEventDetail`, `FileBaseline`, `FileHistoryItem`, `EventFilter`, `JobSummary`) | Correctly use `record` types with `init`-only properties. Appropriate for DTOs. |
| Logging — no string interpolation | `grep` for `$"` inside log calls returned zero matches across all files. All log calls use message templates. |
| `FileHistoryEndpoints.cs` — path traversal guard | `Path.GetFullPath` canonicalisation + prefix check correctly prevents directory traversal. |
| `EventsEndpoints.cs` — pagination | `pageSize` is clamped to `[1, 500]` (GetEvents) and `[1, 5000]` (GetReport). Page cannot go below 1. |
| `GlobalUsings.g.cs` | Auto-generated. `System.Net.Http` and `System.Net.Http.Json` are included by default for Minimal API projects; no custom questionable usings. |
