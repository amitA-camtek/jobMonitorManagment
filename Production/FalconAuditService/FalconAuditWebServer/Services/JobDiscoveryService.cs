namespace FalconAuditWebServer.Services;

public class JobDiscoveryService : IDisposable
{
    private readonly string _watchPath;
    private readonly string _globalDb;
    private readonly ILogger<JobDiscoveryService> _logger;
    private readonly Timer _refreshTimer;
    private volatile IReadOnlyList<string> _knownJobs = Array.Empty<string>();

    public JobDiscoveryService(IConfiguration cfg, ILogger<JobDiscoveryService> logger)
    {
        _watchPath   = cfg["AuditService:WatchPath"]    ?? @"C:\job";
        _globalDb    = cfg["AuditService:GlobalDbPath"] ?? @"C:\bis\auditlog\global.db";
        _logger      = logger;
        Refresh();
        _refreshTimer = new Timer(_ => Refresh(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public IReadOnlyList<string> KnownJobs => _knownJobs;
    public string WatchPath => _watchPath;
    public string GlobalDb  => _globalDb;

    public void Refresh()
    {
        try
        {
            if (!Directory.Exists(_watchPath))
            {
                _knownJobs = Array.Empty<string>();
                return;
            }
            var jobs = Directory.EnumerateDirectories(_watchPath)
                .Where(d => File.Exists(Path.Combine(d, ".audit", "audit.db")))
                .Select(d => Path.GetFileName(d)!)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();
            _knownJobs = jobs;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "JobDiscoveryService: refresh failed."); }
    }

    public string? ShardPath(string jobName)
    {
        var actual = _knownJobs.FirstOrDefault(j => j.Equals(jobName, StringComparison.OrdinalIgnoreCase))
                     ?? jobName;
        var path = Path.Combine(_watchPath, actual, ".audit", "audit.db");
        return File.Exists(path) ? path : null;
    }

    public void Dispose() => _refreshTimer.Dispose();
}
