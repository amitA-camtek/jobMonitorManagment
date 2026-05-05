namespace FalconAuditService;

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

public class ShardRegistry : IDisposable
{
    private readonly ConcurrentDictionary<string, SqliteRepository> _shards =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ShardRegistry> _logger;

    public ShardRegistry(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger        = loggerFactory.CreateLogger<ShardRegistry>();
    }

    /// <summary>
    /// Return the SqliteRepository for a job, creating it on first call.
    /// The shard file lives at &lt;jobPath&gt;\.audit\audit.db.
    /// Returns null if the shard cannot be opened; callers must null-check.
    /// Failures are NOT cached — the next event for the same job will retry.
    /// </summary>
    public SqliteRepository? GetOrCreate(string jobName, string jobPath)
    {
        // Fast path: already open
        if (_shards.TryGetValue(jobName, out var existing)) return existing;

        // Don't resurrect a job whose folder has just been deleted by the user.
        // Without this, a queued FSW event for an already-deleted file would call
        // Directory.CreateDirectory(jobPath\.audit) and silently recreate the
        // parent job folder, fighting against ShardEvictionService.
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
            var repo = new SqliteRepository(dbPath, _loggerFactory.CreateLogger<SqliteRepository>());
            // If two threads raced, GetOrAdd ensures only one repo wins; loser is disposed.
            var added = _shards.GetOrAdd(jobName, repo);
            if (!ReferenceEquals(added, repo))
            {
                repo.Dispose();
            }
            return added;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ShardRegistry: failed to open shard for {J}; NOT cached. DB={D}", jobName, dbPath);
            return null;   // do NOT cache — next event will retry
        }
    }

    public bool TryGet(string jobName, out SqliteRepository? repo) =>
        _shards.TryGetValue(jobName, out repo);

    /// <summary>Close and remove the shard for a job (e.g., job folder deleted).</summary>
    public void Remove(string jobName)
    {
        if (_shards.TryRemove(jobName, out var repo))
        {
            _logger.LogInformation("ShardRegistry: closed shard for job '{J}'.", jobName);
            repo.Dispose();
        }
    }

    public IEnumerable<string> JobNames => _shards.Keys;

    public void Dispose()
    {
        foreach (var repo in _shards.Values)
            repo.Dispose();
        _shards.Clear();
    }
}
