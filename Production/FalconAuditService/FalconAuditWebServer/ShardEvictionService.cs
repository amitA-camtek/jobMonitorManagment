namespace FalconAuditService;

using System.Collections.Concurrent;
using FalconAuditWebServer.Services;
using Microsoft.Extensions.Logging;

/// <summary>
/// When a tracked-file Deleted event empties a job folder (only `.audit\` remains),
/// schedule a debounced eviction: close the SQLite shard, then remove the
/// orphaned `.audit\` directory and the now-empty job folder. This lets the user's
/// `Remove-Item -Recurse C:\job\{jobName}` complete cleanly on retry — the SQLite
/// handles that previously kept `.audit\audit.db` locked are released within the
/// grace window.
/// </summary>
public class ShardEvictionService
{
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pending =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ShardRegistry   _shards;
    private readonly ManifestManager _manifest;
    private readonly JobOriginChecker _origin;
    private readonly QueryRepository _queryRepo;
    private readonly ILogger<ShardEvictionService> _logger;

    public ShardEvictionService(
        ShardRegistry shards,
        ManifestManager manifest,
        JobOriginChecker origin,
        QueryRepository queryRepo,
        ILogger<ShardEvictionService> logger)
    {
        _shards    = shards;
        _manifest  = manifest;
        _origin    = origin;
        _queryRepo = queryRepo;
        _logger    = logger;
    }

    /// <summary>Called from FileChangeHandler after a Deleted event when the job folder is now empty.</summary>
    public void Schedule(string jobName, string jobPath)
    {
        // Debounce: replace any pending eviction for this job with a fresh one.
        if (_pending.TryRemove(jobName, out var prior))
        {
            prior.Cancel();
            prior.Dispose();
        }

        var cts = new CancellationTokenSource();
        if (!_pending.TryAdd(jobName, cts))
        {
            cts.Dispose();
            return;
        }

        _ = Task.Run(() => EvictAfterGraceAsync(jobName, jobPath, cts));
        _logger.LogDebug("ShardEvictionService: scheduled eviction for '{J}' in {S}s.", jobName, GracePeriod.TotalSeconds);
    }

    /// <summary>Called from FileChangeHandler on Created/Modified/Renamed events — keeps the shard alive.</summary>
    public void Cancel(string jobName)
    {
        if (_pending.TryRemove(jobName, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            _logger.LogDebug("ShardEvictionService: pending eviction cancelled for '{J}'.", jobName);
        }
    }

    private async Task EvictAfterGraceAsync(string jobName, string jobPath, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(GracePeriod, cts.Token);

            if (!IsJobFolderEffectivelyEmpty(jobPath))
            {
                _logger.LogDebug("ShardEvictionService: '{J}' no longer empty; skipping eviction.", jobName);
                return;
            }

            _origin.CancelCheck(jobName);
            _manifest.RecordDeparture(jobPath);
            _shards.Remove(jobName);
            _queryRepo.CloseShard(Path.Combine(jobPath, ".audit", "audit.db"));

            TryDeleteAuditFolder(jobName, jobPath);
            TryDeleteJobFolderIfEmpty(jobName, jobPath);

            _logger.LogInformation("ShardEvictionService: evicted shard for '{J}' (job folder emptied).", jobName);
        }
        catch (OperationCanceledException)
        {
            // Cancelled by Cancel() — a new file appeared during the grace window.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ShardEvictionService: eviction failed for '{J}'.", jobName);
        }
        finally
        {
            // Only remove if this CTS is still the registered one (a newer Schedule may have replaced us).
            if (_pending.TryGetValue(jobName, out var current) && ReferenceEquals(current, cts))
                _pending.TryRemove(jobName, out _);
            cts.Dispose();
        }
    }

    /// <summary>True if the job folder is gone, or contains nothing but `.audit\`.</summary>
    public static bool IsJobFolderEffectivelyEmpty(string jobPath)
    {
        try
        {
            if (!Directory.Exists(jobPath)) return true;
            foreach (var entry in Directory.EnumerateFileSystemEntries(jobPath))
            {
                if (!string.Equals(Path.GetFileName(entry), ".audit", StringComparison.OrdinalIgnoreCase))
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
            // Hidden attribute on .audit/ would block recursive delete on some configs.
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
