namespace FalconAuditService;

using Microsoft.Extensions.Logging;

/// <summary>
/// Cleans up orphaned `.audit\` folders and any leftover job-folder files
/// after BIS has finished its `Directory.Delete(jobPath, recursive)`.
///
/// In the lazy-connection model, the audit service does not hold long-lived
/// SQLite handles, so BIS's recursive delete typically completes cleanly on
/// its first attempt. This service exists to:
///   • drain the per-job AuditEventQueue if events were buffered
///   • record the departure in manifest.json (best-effort, may no-op if .audit\ is gone)
///   • cancel any in-flight origin-check task for this job
///   • sweep `.audit\` and known orphan files (e.g. `MetaData.ini`) so the
///     job folder can be removed if BIS left it half-deleted
///
/// Invoked by:
///   • DirectoryWatcher onDeparted (folder gone — main path)
///   • DELETE /api/jobs/{name} (Falcon.Net's JobSerializer.Delete announcing intent)
/// </summary>
public class ShardEvictionService
{
    // Falcon.Net leaves these job-level files behind when its non-recursive
    // JobSerializer.Delete fails. Sweep them during eviction.
    private static readonly HashSet<string> IgnoredOrphanFiles =
        new(StringComparer.OrdinalIgnoreCase) { "MetaData.ini" };

    private readonly ShardRegistry   _shards;
    private readonly ManifestManager _manifest;
    private readonly JobOriginChecker _origin;
    private readonly ILogger<ShardEvictionService> _logger;

    public ShardEvictionService(
        ShardRegistry shards,
        ManifestManager manifest,
        JobOriginChecker origin,
        ILogger<ShardEvictionService> logger)
    {
        _shards    = shards;
        _manifest  = manifest;
        _origin    = origin;
        _logger    = logger;
    }

    /// <summary>
    /// Run the eviction now: drain the queue if possible, record departure,
    /// cancel pending origin check, sweep the orphan `.audit\` and `MetaData.ini`,
    /// and remove the empty job folder.
    /// </summary>
    public async Task EvictNowAsync(string jobName, string jobPath, string reason)
    {
        try
        {
            _origin.CancelCheck(jobName);

            // Drain any pending events (may no-op if the audit DB is already gone)
            // and AWAIT the dispose so the SQLite handle on audit.db is released
            // before we proceed. Without this await, a still-running in-flight
            // flush (started by the queue's timer just before Discard cleared the
            // buffer) would keep audit.db / audit.db-wal open while Falcon's
            // recursive Directory.Delete(jobPath) runs — leaving a ghost
            // .audit\ folder behind.
            await _shards.DiscardOnDepartureAsync(jobName);

            // Note: we used to call _manifest.RecordDepartureAsync(jobPath) here, but
            // the very next step deletes .audit\ wholesale — so the departure record
            // was written and immediately destroyed. No code path reads departed-job
            // manifest data, so the write was orphaned. Skip it.

            TryDeleteAuditFolder(jobName, jobPath);
            TryDeleteJobFolderIfEmpty(jobName, jobPath);

            _logger.LogInformation("ShardEvictionService: evicted '{J}' ({R}).", jobName, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ShardEvictionService: EvictNow failed for '{J}'.", jobName);
        }
    }

    /// <summary>True if the job folder is gone, or contains nothing but `.audit\` and ignorable orphan files.</summary>
    public static bool IsJobFolderEffectivelyEmpty(string jobPath)
    {
        try
        {
            if (!Directory.Exists(jobPath)) return true;
            foreach (var entry in Directory.EnumerateFileSystemEntries(jobPath))
            {
                var name = Path.GetFileName(entry);
                if (string.Equals(name, ".audit", StringComparison.OrdinalIgnoreCase)) continue;
                if (IgnoredOrphanFiles.Contains(name)) continue;
                return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void TryDeleteAuditFolder(string jobName, string jobPath)
    {
        var auditDir = Path.Combine(jobPath, ".audit");
        try
        {
            if (!Directory.Exists(auditDir)) return;
            ClearHiddenAttribute(auditDir);
            Directory.Delete(auditDir, recursive: true);
            _logger.LogInformation("ShardEvictionService: removed orphaned .audit\\ for '{J}'.", jobName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ShardEvictionService: could not remove .audit\\ for '{J}' at {P}.", jobName, auditDir);
        }
    }

    private void TryDeleteJobFolderIfEmpty(string jobName, string jobPath)
    {
        try
        {
            if (!Directory.Exists(jobPath)) return;

            foreach (var entry in Directory.EnumerateFiles(jobPath))
            {
                var name = Path.GetFileName(entry);
                if (!IgnoredOrphanFiles.Contains(name)) continue;
                try { File.Delete(entry); }
                catch (Exception swept)
                {
                    _logger.LogDebug(swept,
                        "ShardEvictionService: could not remove orphan file '{F}' for '{J}'.",
                        entry, jobName);
                }
            }

            if (Directory.EnumerateFileSystemEntries(jobPath).Any()) return;
            Directory.Delete(jobPath, recursive: false);
            _logger.LogInformation("ShardEvictionService: removed empty job folder for '{J}'.", jobName);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ShardEvictionService: did not remove job folder for '{J}' at {P}.", jobName, jobPath);
        }
    }

    private static void ClearHiddenAttribute(string dir)
    {
        try
        {
            var attrs = File.GetAttributes(dir);
            if ((attrs & FileAttributes.Hidden) != 0)
                File.SetAttributes(dir, attrs & ~FileAttributes.Hidden);
        }
        catch { }
    }
}
