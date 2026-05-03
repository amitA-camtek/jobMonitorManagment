namespace FalconAuditService;

using System.Collections.Concurrent;
using FalconAuditService.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Determines whether a job folder was created locally by BIS ("NewLocal") or
/// copied from another machine ("CopiedFromRemote"), and persists the result.
///
/// Detection uses two stages after a configurable settle window:
///   Stage 1 — manifest history: if any entry belongs to a different machine → CopiedFromRemote
///   Stage 2 — NTFS timestamp heuristic: copied files have CreationTime > LastWriteTime + threshold
/// </summary>
public class JobOriginChecker : IDisposable
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pending =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ShardRegistry    _shards;
    private readonly ManifestManager  _manifest;
    private readonly FileClassifier   _classifier;
    private readonly MonitorConfig    _config;
    private readonly ILogger<JobOriginChecker> _logger;

    public JobOriginChecker(
        ShardRegistry shards, ManifestManager manifest,
        FileClassifier classifier, MonitorConfig config,
        ILogger<JobOriginChecker> logger)
    {
        _shards     = shards;
        _manifest   = manifest;
        _classifier = classifier;
        _config     = config;
        _logger     = logger;
    }

    /// <summary>
    /// Called from onArrived. Starts a non-blocking settle timer.
    /// Replaces any existing pending timer for this job (defensive).
    /// </summary>
    public void ScheduleCheck(string jobName, string jobPath)
    {
        if (_pending.TryRemove(jobName, out var old)) old.Cancel();

        var cts = new CancellationTokenSource();
        _pending[jobName] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_config.JobSettleTimeSeconds), cts.Token);
                _pending.TryRemove(jobName, out _);
                await DetermineAndRecordAsync(jobName, jobPath, isRetry: false);
            }
            catch (OperationCanceledException) { }
        });
    }

    /// <summary>
    /// Called from onDeparted so a job that disappears before the timer fires
    /// does not generate a stale origin write.
    /// </summary>
    public void CancelCheck(string jobName)
    {
        if (_pending.TryRemove(jobName, out var cts)) cts.Cancel();
    }

    private async Task DetermineAndRecordAsync(string jobName, string jobPath, bool isRetry)
    {
        try
        {
            // Skip if origin was already determined (e.g. service restart on an existing job)
            var repo = _shards.GetOrCreate(jobName, jobPath);
            if (repo is not null && repo.GetConfigValue("job_origin") is not null)
            {
                _logger.LogDebug("JobOriginChecker: '{J}' already classified — skipping.", jobName);
                return;
            }

            var origin = DetermineOrigin(jobPath);

            if (origin == "Unknown" && !isRetry)
            {
                _logger.LogDebug(
                    "JobOriginChecker: '{J}' inconclusive (too few P1 files) — rescheduling once.",
                    jobName);

                if (_pending.TryRemove(jobName, out var old)) old.Cancel();
                var cts = new CancellationTokenSource();
                _pending[jobName] = cts;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(_config.JobSettleTimeSeconds), cts.Token);
                        _pending.TryRemove(jobName, out _);
                        await DetermineAndRecordAsync(jobName, jobPath, isRetry: true);
                    }
                    catch (OperationCanceledException) { }
                });
                return;
            }

            _logger.LogInformation("JobOriginChecker: '{J}' → {O}", jobName, origin);

            if (repo is not null)
                await repo.SetConfigValueAsync("job_origin", origin);

            await _manifest.UpdateOriginAsync(jobPath, origin);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JobOriginChecker: error determining origin for '{J}'.", jobName);
        }
    }

    /// <summary>
    /// Returns "NewLocal", "CopiedFromRemote", or "Unknown".
    /// Public so it can be called directly in tests or one-off checks.
    /// </summary>
    public string DetermineOrigin(string jobPath)
    {
        // Stage 1 — manifest history is definitive when it predates this service session
        var manifest = _manifest.ReadManifest(jobPath);
        if (manifest?.Created is not null)
        {
            var age = DateTime.UtcNow - manifest.Created.At;
            if (age > TimeSpan.FromSeconds(_config.JobSettleTimeSeconds + 60))
            {
                // Manifest is from a previous session — its history is authoritative
                bool hasForeignMachine = manifest.History.Any(h =>
                    !string.Equals(h.Machine, _config.MachineName,
                                    StringComparison.OrdinalIgnoreCase));
                return hasForeignMachine ? "CopiedFromRemote" : "NewLocal";
            }
            // else: manifest was just created by RecordArrival this session → fall through
        }

        // Stage 2 — NTFS CreationTime vs LastWriteTime heuristic
        return SampleNtfsTimestamps(jobPath);
    }

    private string SampleNtfsTimestamps(string jobPath)
    {
        var threshold = TimeSpan.FromMinutes(_config.OriginDeltaMinutes);

        // Collect P1 files, skip the .audit\ folder itself
        var candidates = new List<string>();
        try
        {
            candidates = Directory
                .EnumerateFiles(jobPath, "*.*", SearchOption.AllDirectories)
                .Where(f => !f.Contains(@"\.audit\", StringComparison.OrdinalIgnoreCase) &&
                            !f.Contains("/.audit/",  StringComparison.OrdinalIgnoreCase))
                .Where(f => _classifier.Classify(f).MonitorPriority == "P1")
                .Take(_config.OriginSampleSize * 3)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JobOriginChecker: error enumerating files in '{J}'.", jobPath);
            return "Unknown";
        }

        if (candidates.Count < 3)
        {
            _logger.LogDebug(
                "JobOriginChecker: '{J}' — only {N} P1 file(s) found, need ≥3.",
                Path.GetFileName(jobPath), candidates.Count);
            return "Unknown";
        }

        // Take the N most-recently-written P1 files as the sample
        var sample = candidates
            .Select(f =>
            {
                try { return new FileInfo(f); }
                catch { return null; }
            })
            .Where(fi => fi is not null)
            .OrderByDescending(fi => fi!.LastWriteTimeUtc)
            .Take(_config.OriginSampleSize)
            .ToList();

        if (sample.Count < 3) return "Unknown";

        // delta > 0 means CreationTime is NEWER than LastWriteTime → file was copied here
        int copiedCount = sample.Count(fi =>
            fi!.CreationTimeUtc - fi.LastWriteTimeUtc > threshold);

        double ratio = (double)copiedCount / sample.Count;
        _logger.LogDebug(
            "JobOriginChecker: '{J}' NTFS sample={N} copied={C} ratio={R:P0} threshold={T}min.",
            Path.GetFileName(jobPath), sample.Count, copiedCount, ratio, _config.OriginDeltaMinutes);

        return ratio >= _config.OriginCopiedRatio ? "CopiedFromRemote" : "NewLocal";
    }

    public void Dispose()
    {
        foreach (var cts in _pending.Values) cts.Cancel();
        _pending.Clear();
    }
}
