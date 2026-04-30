namespace FalconAuditService;

using Microsoft.Extensions.Logging;

public class DirectoryWatcher : IDisposable
{
    private FileSystemWatcher?            _watcher;
    private readonly string               _watchPath;
    private readonly Action<string, string> _onArrived;   // (jobName, jobFullPath)
    private readonly Action<string>       _onDeparted;    // (jobName)
    private readonly ILogger<DirectoryWatcher> _logger;

    public DirectoryWatcher(
        string watchPath,
        Action<string, string> onArrived,
        Action<string> onDeparted,
        ILogger<DirectoryWatcher> logger)
    {
        _watchPath  = watchPath;
        _onArrived  = onArrived;
        _onDeparted = onDeparted;
        _logger     = logger;
    }

    public void Start()
    {
        if (!Directory.Exists(_watchPath))
        {
            _logger.LogWarning("DirectoryWatcher: watch path does not exist: {P}", _watchPath);
            return;
        }
        _watcher = new FileSystemWatcher(_watchPath)
        {
            NotifyFilter = System.IO.NotifyFilters.DirectoryName,
            IncludeSubdirectories = false,   // depth=1 — job directories only
            EnableRaisingEvents   = true
        };
        _watcher.Created += OnCreated;
        _watcher.Deleted += OnDeleted;
        _watcher.Renamed += OnRenamed;
        _logger.LogInformation("DirectoryWatcher: watching {P} for job folder changes.", _watchPath);
    }

    public void Stop()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    /// <summary>Enumerate existing job folders at startup — fires onArrived for each.</summary>
    public void EnumerateExisting()
    {
        if (!Directory.Exists(_watchPath)) return;
        foreach (var dir in Directory.EnumerateDirectories(_watchPath))
        {
            var name = Path.GetFileName(dir);
            if (!string.IsNullOrEmpty(name))
                _onArrived(name, dir);
        }
    }

    private void OnCreated(object _, FileSystemEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Name)) return;
        _logger.LogInformation("DirectoryWatcher: job folder arrived — '{N}'.", e.Name);
        _onArrived(e.Name!, e.FullPath);
    }

    private void OnDeleted(object _, FileSystemEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Name)) return;
        _logger.LogInformation("DirectoryWatcher: job folder departed — '{N}'.", e.Name);
        _onDeparted(e.Name!);
    }

    private void OnRenamed(object _, RenamedEventArgs e)
    {
        _logger.LogInformation("DirectoryWatcher: job folder renamed '{O}' → '{N}'.",
                                e.OldName, e.Name);
        if (!string.IsNullOrEmpty(e.OldName)) _onDeparted(e.OldName!);
        if (!string.IsNullOrEmpty(e.Name))    _onArrived(e.Name!, e.FullPath);
    }

    public void Dispose() => _watcher?.Dispose();
}
