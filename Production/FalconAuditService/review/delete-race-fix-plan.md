# Delete-Race Fix Plan

## Symptom

With FalconAuditService running:
1. User deletes the currently-open job in BIS.
2. UI thread logs `System.IO.DirectoryNotFoundException: Could not find a part of the path 'c:\job\{Job}\300mm\Recipes\AllMags\ReferencesInfo.json'` via `Machine.UI.Common.SynchronizeInvokeExtensions.InvokeIfRequired`.
3. User tries to open a new job — nothing happens, no error UI.

Without the audit service, deletion + new-job-open works.

## Root cause

Three interacting effects, all originating in the audit service:

1. **`SqliteRepository` keeps `.audit\audit.db` open** with `Pooling=False`. When BIS's `Directory.Delete(jobPath, recursive: true)` (`JobSerializerV1.Delete`) reaches `audit.db`, it fails with `IOException`. `JobSerializerV1.Delete` swallows it (returns `void`), leaving the job folder on disk. `frmJobTab.CheckLoadJob` later sees stale state and silently returns `false` — that is the "no job opens" symptom.

2. **`ShardEvictionService` only triggers after a 2 s grace period** once the folder is *already* effectively empty (`ShardEvictionService.cs:48-70`). BIS's recursive delete reaches `audit.db` long before that timer fires, so the handle is never released in time.

3. **`FileChangeHandler.HandleAsync` keeps writing to `.audit\manifest.json` and `audit.db` while files are vanishing.** Each Deleted event calls `ShardRegistry.GetOrCreate` (which `Directory.CreateDirectory(.audit)`), then `ManifestManager.IncrementEventsAsync` (which reads + atomically rewrites manifest.json). This races with BIS's iteration and produces the `ReferencesInfo.json` `DirectoryNotFoundException` on a UI refresh that fires after the file has been deleted.

## Fix A — Eager shard release on first emptying-event

**File**: `FileChangeHandler.cs` (around lines 198-212 — the existing self-eviction block)

Today the eviction is *scheduled* for 2 s later. Change it so that when a Deleted event leaves the job folder effectively empty, the shard handle is closed **synchronously, immediately**, before the 2 s grace task even starts. The grace task continues to handle the orphan-folder cleanup (`.audit\` removal, empty job-folder removal).

```csharp
if (jobName is not null && jobPath is not null)
{
    if (ev.ChangeType == WatcherChangeTypes.Deleted &&
        ShardEvictionService.IsJobFolderEffectivelyEmpty(jobPath))
    {
        // Release the SQLite handle NOW so BIS's Directory.Delete(recursive)
        // can remove .audit\audit.db on the same pass. The eviction task
        // continues to handle .audit\ + empty-folder cleanup after grace.
        _shards.Remove(jobName);
        _eviction.Schedule(jobName, jobPath);
    }
    else if (ev.ChangeType != WatcherChangeTypes.Deleted)
    {
        _eviction.Cancel(jobName);
    }
}
```

`ShardRegistry.Remove` is already idempotent (`TryRemove` + `Dispose`), so the later `_shards.Remove` inside `EvictAfterGraceAsync` is a no-op.

**Why this works**: The first `Deleted` event whose effect is "only `.audit\` remains" is exactly the moment the recursive delete is about to descend into `.audit\`. Closing the handle there gives BIS the lock it needs.

## Fix B — Don't resurrect or write into a dying job folder

Three small guards, all on the write path:

**B1. `ShardRegistry.GetOrCreate`** — already checks `Directory.Exists(jobPath)`. Tighten it to also bail if the folder is effectively empty (i.e. mid-delete):

```csharp
if (!Directory.Exists(jobPath) ||
    ShardEvictionService.IsJobFolderEffectivelyEmpty(jobPath))
{
    _logger.LogDebug(
        "ShardRegistry: skipping GetOrCreate for '{J}' — folder gone or being deleted.", jobName);
    return null;
}
```

**B2. `FileChangeHandler.HandleAsync`** — for `Deleted` events specifically, don't try to fetch the repo if the job folder is now effectively empty (the event we are processing IS the one that emptied it). Move the empty-check **before** `GetRepo`:

```csharp
var (jobName, jobPath) = ExtractJob(ev.FullPath);
if (ev.ChangeType == WatcherChangeTypes.Deleted &&
    jobPath is not null &&
    ShardEvictionService.IsJobFolderEffectivelyEmpty(jobPath))
{
    // Job folder is being torn down — do not write a tombstone event,
    // do not resurrect .audit\, do not bump manifest.
    return;
}

var repo = GetRepo(ev.FullPath);
...
```

**B3. `ManifestManager.WriteManifest`** — bail before creating the `.tmp` file if the parent directory has already been removed. Currently it writes the temp file, then `File.Move` throws `DirectoryNotFoundException`, which is caught and logged — but the `.tmp` file may already be on disk inside a folder BIS thought it had emptied:

```csharp
private void WriteManifest(string path, JobManifest manifest)
{
    var dir = Path.GetDirectoryName(path);
    if (dir is null || !Directory.Exists(dir))
    {
        _logger.LogDebug("ManifestManager: parent gone, skipping write of {P}.", path);
        return;
    }
    var tmp = path + ".tmp";
    try
    {
        File.WriteAllText(tmp, JsonSerializer.Serialize(manifest, _jsonOpts));
        ...
```

## Files touched

| File | Change |
|---|---|
| `FileChangeHandler.cs` | Add Fix A (sync `_shards.Remove`) + Fix B2 (early-return on Deleted-into-empty) |
| `ShardRegistry.cs` | Fix B1 (additional guard in `GetOrCreate`) |
| `ManifestManager.cs` | Fix B3 (parent-exists guard in `WriteManifest`) |

Estimated diff: ~30 lines.

## Test plan

1. Start the audit service against `c:\job\`.
2. Create a job in BIS, open it, save a recipe (so `ReferencesInfo.json` exists).
3. Delete the job from BIS while watching the audit log and BIS UI log.
   - Expect: no `DirectoryNotFoundException` in BIS UI log.
   - Expect: `c:\job\{Job}\` is fully removed (no `.audit\` orphan, no leftover folder).
   - Expect: audit log shows `ShardRegistry: closed shard for job '{J}'.` immediately on the emptying event, not 2 s later.
4. Immediately open a different job in BIS.
   - Expect: the job opens normally.
5. Repeat 5× rapidly to flush out any residual race.

## Out of scope

- BIS-side hardening (`JobSerializerV1.Delete` returning a result, `frmJobTab.CheckLoadJob` showing an error when it bails) — separate change in the BIS repo.
- WAL/SHM file lifecycle — `Pooling=False` already releases handles on `Dispose`.
- Replacing the 2 s grace period — keep it; the orphan-cleanup work it does is still needed.
