namespace FalconAuditService;

using System.Collections.Concurrent;
using FalconAuditService.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Per-job lifecycle manager. Holds one <see cref="AuditEventQueue"/> per job;
/// the queue wraps a stateless <see cref="SqliteRepository"/> that opens/closes
/// connections on demand.
///
/// In the lazy connection model, the registry no longer holds long-lived
/// SQLite handles — the only persistent per-job state is the in-memory event
/// queue, which is cheap. <c>Remove(jobName)</c> drains the queue to disk
/// before disposing (best-effort; if the audit folder has just been deleted by
/// BIS the flush is a no-op).
/// </summary>
public class ShardRegistry : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, AuditEventQueue> _queues =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ManifestManager _manifest;
    private readonly MonitorConfig   _config;
    private readonly ILoggerFactory  _loggerFactory;
    private readonly ILogger<ShardRegistry> _logger;

    public ShardRegistry(
        ManifestManager manifest, MonitorConfig config,
        ILoggerFactory loggerFactory)
    {
        _manifest      = manifest;
        _config        = config;
        _loggerFactory = loggerFactory;
        _logger        = loggerFactory.CreateLogger<ShardRegistry>();
    }

    /// <summary>
    /// Return the per-job audit-event queue, creating it (and ensuring the
    /// underlying audit.db schema exists) on first call. Returns null if the
    /// shard cannot be opened (e.g. job folder vanished mid-call).
    /// </summary>
    public AuditEventQueue? GetOrCreate(string jobName, string jobPath)
    {
        if (_queues.TryGetValue(jobName, out var existing)) return existing;

        // Don't resurrect a shard for a job whose folder has been deleted.
        if (!Directory.Exists(jobPath))
        {
            _logger.LogDebug("ShardRegistry: skipping GetOrCreate for '{J}' — parent folder gone.", jobName);
            return null;
        }

        var auditDir = Path.Combine(jobPath, ".audit");
        var dbPath   = Path.Combine(auditDir, "audit.db");
        try
        {
            Directory.CreateDirectory(auditDir);
            File.SetAttributes(auditDir, File.GetAttributes(auditDir) | FileAttributes.Hidden);

            _logger.LogInformation("ShardRegistry: opening shard for job '{J}' at {D}", jobName, dbPath);
            var repo  = new SqliteRepository(dbPath, _loggerFactory.CreateLogger<SqliteRepository>());
            var queue = new AuditEventQueue(jobName, jobPath, repo, _manifest, _config,
                                             _loggerFactory.CreateLogger<AuditEventQueue>());

            // GetOrAdd ensures only one queue wins under concurrent GetOrCreate.
            var added = _queues.GetOrAdd(jobName, queue);
            if (!ReferenceEquals(added, queue))
            {
                _ = queue.DisposeAsync();
            }
            return added;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ShardRegistry: failed to open shard for {J}; NOT cached. DB={D}", jobName, dbPath);
            return null;
        }
    }

    public bool TryGet(string jobName, out AuditEventQueue? queue) =>
        _queues.TryGetValue(jobName, out queue);

    /// <summary>
    /// Drain and remove the queue for a job. Best-effort flush before disposal —
    /// if the audit folder has just been deleted by BIS, the flush quietly fails
    /// and the queue is discarded.
    /// </summary>
    public void Remove(string jobName)
    {
        if (_queues.TryRemove(jobName, out var queue))
        {
            _logger.LogInformation("ShardRegistry: closing queue for job '{J}'.", jobName);
            // Fire-and-forget the dispose: the queue's DisposeAsync flushes
            // with a 10 s timeout, and we don't want Remove to block.
            _ = queue.DisposeAsync();
        }
    }

    /// <summary>
    /// Discard the queue WITHOUT flushing. Used on job departure: the audit DB
    /// is going away with the folder, so flush attempts would just race the
    /// folder removal. Awaits DisposeAsync so the SQLite handle on audit.db is
    /// fully released before we return — required so that <see cref="ShardEvictionService.EvictNowAsync"/>
    /// (and Falcon's subsequent recursive Directory.Delete) does not race a
    /// still-running flush that holds audit.db open.
    /// </summary>
    public async Task DiscardOnDepartureAsync(string jobName)
    {
        if (_queues.TryRemove(jobName, out var queue))
        {
            queue.Discard();
            await queue.DisposeAsync();
            _logger.LogInformation("ShardRegistry: discarded queue for departed job '{J}'.", jobName);
        }
    }

    public IEnumerable<string> JobNames => _queues.Keys;

    /// <summary>
    /// Flush all per-job queues to disk. Called by API handlers iterating jobs
    /// (e.g. GET /api/jobs) so the response reflects all in-memory data.
    /// </summary>
    public async Task FlushAllAsync()
    {
        var tasks = _queues.Values.Select(q => q.FlushAsync()).ToArray();
        await Task.WhenAll(tasks);
    }

    public async ValueTask DisposeAsync()
    {
        // Drain all queues (graceful shutdown path).
        var tasks = _queues.Values.Select(q => q.DisposeAsync().AsTask()).ToArray();
        await Task.WhenAll(tasks);
        _queues.Clear();
    }
}
