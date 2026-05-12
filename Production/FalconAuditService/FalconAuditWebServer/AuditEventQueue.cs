namespace FalconAuditService;

using FalconAuditService.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Per-job in-memory queue of audit events + manifest increments. Events are
/// buffered and flushed to disk in a single transaction. The queue exists so
/// the audit service can keep `audit.db` closed >99% of the time, eliminating
/// the file-lock contention with BIS's <c>Directory.Delete(jobPath, recursive)</c>.
///
/// Flush triggers (any one of them):
/// <list type="bullet">
/// <item>X seconds elapsed since the FIRST event in the current batch
///       (the timer is anchored to the first event and is NOT reset by later events)</item>
/// <item>Queue size hits <c>queueMax</c></item>
/// <item>A read API call invokes <see cref="FlushAsync"/></item>
/// <item>The job departs (folder removed) — see <see cref="Discard"/></item>
/// <item>Service shutdown — see <see cref="DisposeAsync"/></item>
/// </list>
///
/// Concurrent flush requests coalesce: if a flush is already in flight, new
/// callers await the same task instead of starting a second flush.
/// </summary>
public sealed class AuditEventQueue : IAsyncDisposable
{
    private readonly string _jobName;
    private readonly string _jobPath;
    private readonly SqliteRepository _repo;
    private readonly ManifestManager _manifest;
    private readonly TimeSpan _flushInterval;
    private readonly int _queueMax;
    private readonly ILogger<AuditEventQueue> _logger;

    private readonly object _lock = new();
    private List<(AuditLogEntry Entry, FileBaseline Baseline)> _buffer = new();
    private int _pendingManifestBumps;
    private Timer? _timer;
    private Task? _inFlightFlush;

    public string JobName => _jobName;
    public string JobPath => _jobPath;
    public SqliteRepository Repository => _repo;

    public int PendingCount
    {
        get { lock (_lock) return _buffer.Count; }
    }

    public AuditEventQueue(
        string jobName, string jobPath,
        SqliteRepository repo, ManifestManager manifest,
        MonitorConfig config, ILogger<AuditEventQueue> logger)
    {
        _jobName       = jobName;
        _jobPath       = jobPath;
        _repo          = repo;
        _manifest      = manifest;
        _flushInterval = TimeSpan.FromSeconds(Math.Max(1, config.FlushIntervalSeconds));
        _queueMax      = Math.Max(1, config.FlushQueueMax);
        _logger        = logger;
    }

    /// <summary>
    /// Append an audit event to the queue. If queue size hits the cap, this
    /// triggers an immediate flush (and the caller observes that flush via the
    /// returned task). Otherwise returns immediately.
    /// </summary>
    public Task EnqueueAsync(AuditLogEntry entry, FileBaseline baseline)
    {
        Task? toAwait = null;
        lock (_lock)
        {
            _buffer.Add((entry, baseline));
            if (_buffer.Count >= _queueMax)
            {
                _logger.LogDebug("AuditEventQueue '{J}': cap hit ({N}) — flushing.", _jobName, _buffer.Count);
                toAwait = StartFlushIfNeededLocked();
            }
            else
            {
                ArmTimerIfNeededLocked();
            }
        }
        return toAwait ?? Task.CompletedTask;
    }

    /// <summary>
    /// Record a pending manifest event-counter bump (one per audit event written).
    /// Applied during the next flush as a single read-modify-write of manifest.json.
    /// </summary>
    public void EnqueueManifestBump(int count = 1)
    {
        lock (_lock) { _pendingManifestBumps += count; }
    }

    /// <summary>
    /// Force a flush now. If a flush is already in flight, returns the same
    /// task so concurrent callers all observe the single in-flight flush.
    /// Returns immediately (with completed task) if there's nothing pending.
    /// </summary>
    public Task FlushAsync()
    {
        lock (_lock)
        {
            return StartFlushIfNeededLocked() ?? Task.CompletedTask;
        }
    }

    /// <summary>
    /// Discard the queue without flushing. Called when the job folder departs:
    /// the audit DB is going away with the folder, so the buffered events are
    /// lost on purpose.
    /// </summary>
    public void Discard()
    {
        int dropped;
        lock (_lock)
        {
            dropped = _buffer.Count;
            _buffer.Clear();
            _pendingManifestBumps = 0;
            _timer?.Dispose();
            _timer = null;
        }
        if (dropped > 0)
            _logger.LogInformation(
                "AuditEventQueue '{J}': discarded {N} events on departure.", _jobName, dropped);
    }

    public async ValueTask DisposeAsync()
    {
        try { await FlushAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AuditEventQueue '{J}': flush-on-dispose failed.", _jobName);
        }
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    // ── internals — caller must hold _lock unless noted ─────────────────────

    private void ArmTimerIfNeededLocked()
    {
        // First-event-anchored: only arm if no timer exists. Subsequent events
        // do NOT reset the timer.
        if (_timer is not null) return;
        _timer = new Timer(_ => OnTimerFire(), null, _flushInterval, Timeout.InfiniteTimeSpan);
    }

    private void OnTimerFire()
    {
        Task? toAwait;
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
            toAwait = StartFlushIfNeededLocked();
        }
        // Don't await — the task continuation handles cleanup.
        _ = toAwait;
    }

    private Task? StartFlushIfNeededLocked()
    {
        if (_inFlightFlush is not null) return _inFlightFlush;
        if (_buffer.Count == 0 && _pendingManifestBumps == 0) return null;

        // Snapshot the buffer + bumps; reset state. Subsequent enqueues during
        // the flush populate a fresh batch and may arm a new timer.
        var batch = _buffer;
        var bumps = _pendingManifestBumps;
        _buffer = new List<(AuditLogEntry, FileBaseline)>();
        _pendingManifestBumps = 0;
        _timer?.Dispose();
        _timer = null;

        var flushTask = Task.Run(() => DoFlushAsync(batch, bumps));
        _inFlightFlush = flushTask;

        // Clear _inFlightFlush after completion so the next flush can start.
        _ = flushTask.ContinueWith(_ =>
        {
            lock (_lock) { _inFlightFlush = null; }
        }, TaskContinuationOptions.ExecuteSynchronously);

        return flushTask;
    }

    private async Task DoFlushAsync(List<(AuditLogEntry Entry, FileBaseline Baseline)> batch, int bumps)
    {
        try
        {
            // Race avoidance for manual / non-API deletes: if the job folder has
            // been emptied of user content (only .audit\ left), a recursive
            // Directory.Delete is almost certainly walking the tree right now.
            // Opening audit.db here would lock it mid-walk → IOException →
            // partial delete → husk. Drop the buffer and let the walk finish;
            // ShardEvictionService will clean up our shard via DirectoryWatcher
            // departed once the folder is actually gone.
            if (IsJobFolderEmptyOfUserContent())
            {
                Discard();
                _logger.LogInformation(
                    "AuditEventQueue '{J}': job folder content gone (only .audit\\ remains) — skipping flush of {N} events to avoid blocking the recursive delete.",
                    _jobName, batch.Count);
                return;
            }

            if (batch.Count > 0)
            {
                await _repo.WriteBatchAsync(batch);
                _logger.LogDebug(
                    "AuditEventQueue '{J}': flushed {N} events to audit.db.", _jobName, batch.Count);
            }
            if (bumps > 0)
            {
                await _manifest.IncrementEventsByAsync(_jobPath, bumps);
            }
        }
        catch (Exception ex)
        {
            // Most likely cause: audit.db / manifest.json got deleted by BIS
            // between the enqueue and the flush. Acceptable — the audit data
            // was for a job that's going away anyway.
            _logger.LogWarning(ex,
                "AuditEventQueue '{J}': flush failed — dropping {N} events.",
                _jobName, batch.Count);
        }
    }

    private bool IsJobFolderEmptyOfUserContent()
    {
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(_jobPath))
            {
                if (!string.Equals(Path.GetFileName(entry), ".audit",
                                    StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            // Folder fully gone — also "empty of user content" for our purposes
            return true;
        }
        catch
        {
            // Enumeration failed for some other reason (permissions, etc.).
            // Be safe: don't skip the flush on uncertain signal.
            return false;
        }
    }
}
