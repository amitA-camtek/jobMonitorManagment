namespace FalconAuditService;

using System.Collections.Concurrent;
using System.Threading.Channels;
using FalconAuditService.Models;
using Microsoft.Extensions.Logging;

public class FileMonitorService : IDisposable
{
    private FileSystemWatcher?                                     _watcher;
    private readonly ConcurrentDictionary<string, Timer>           _debounce    = new();
    private readonly ConcurrentDictionary<string, FileSystemEventArgs> _latestEvent = new();
    private int _recoveryScheduled;
    private readonly Channel<ChangeEvent> _queue = Channel.CreateBounded<ChangeEvent>(
        new BoundedChannelOptions(1024)
        {
            FullMode     = BoundedChannelFullMode.Wait,   // back-pressure on producer
            SingleReader = false,                          // multiple consumers
            SingleWriter = false
        });
    private Task[]? _consumers;
    private readonly FileChangeHandler  _handler;
    private readonly CatchUpScanner     _catchUp;
    private readonly MonitorConfig      _config;
    private readonly ILogger<FileMonitorService> _logger;
    private CancellationToken           _ct;

    public bool IsActive => _watcher?.EnableRaisingEvents == true;

    public FileMonitorService(FileChangeHandler handler, CatchUpScanner catchUp,
                               MonitorConfig config, ILogger<FileMonitorService> logger)
    {
        _handler = handler;
        _catchUp = catchUp;
        _config  = config;
        _logger  = logger;
    }

    public void Start(CancellationToken ct)
    {
        _ct = ct;
        InitWatcher();

        int workerCount = Math.Max(2, Environment.ProcessorCount);
        _consumers = Enumerable.Range(0, workerCount)
                     .Select(_ => Task.Run(ConsumeAsync, ct))
                     .ToArray();
        _logger.LogInformation(
            "FileMonitorService: FSW enabled. Path={P} Buffer={B} Workers={W}",
            _config.WatchPath, _config.FswBufferBytes, workerCount);
    }

    public async Task StopAsync()
    {
        _watcher?.Dispose();
        _queue.Writer.TryComplete();
        if (_consumers is not null)
            await Task.WhenAll(_consumers).WaitAsync(TimeSpan.FromSeconds(10));
        _logger.LogInformation("FileMonitorService stopped.");
    }

    // Keep synchronous Stop() for backward compat with Worker.cs StopAsync
    public void Stop() => StopAsync().GetAwaiter().GetResult();

    private void InitWatcher()
    {
        _watcher?.Dispose();
        if (!Directory.Exists(_config.WatchPath))
        {
            _logger.LogWarning("FileMonitorService: watch path does not exist: {P}", _config.WatchPath);
            return;
        }
        _watcher = new FileSystemWatcher(_config.WatchPath)
        {
            NotifyFilter = System.IO.NotifyFilters.FileName
                                  | System.IO.NotifyFilters.LastWrite
                                  | System.IO.NotifyFilters.DirectoryName,
            IncludeSubdirectories = true,
            InternalBufferSize    = _config.FswBufferBytes,
            Filter                = "*.*",
            EnableRaisingEvents   = true
        };
        _watcher.Changed += OnFileEvent;
        _watcher.Created += OnFileEvent;
        _watcher.Deleted += OnFileEvent;
        _watcher.Renamed += OnRenamed;
        _watcher.Error   += OnError;
    }

    private void OnFileEvent(object _, FileSystemEventArgs e)
    {
        // Skip events on directories (FSW also fires on directory creates)
        // We only care about file events for our debounce path.
        try
        {
            if (Directory.Exists(e.FullPath)) return;
        }
        catch { /* ignore — path may already be gone */ }

        _logger.LogDebug("FSW event received. Type={T} Path={P}", e.ChangeType, e.FullPath);
        // Always record the latest event so FireDebounce dispatches the most recent change type.
        _latestEvent[e.FullPath] = e;
        _debounce.AddOrUpdate(
            e.FullPath,
            key =>
            {
                _logger.LogDebug("FSW debounce created. Path={P}", key);
                return new Timer(FireDebounce, key, _config.DebounceMs, Timeout.Infinite);
            },
            (key, existing) =>
            {
                existing.Change(_config.DebounceMs, Timeout.Infinite);
                _logger.LogDebug("FSW event received. Type={T} Path={P}  (debounce reset)",
                                  e.ChangeType, e.FullPath);
                return existing;
            });
    }

    private void OnRenamed(object _, RenamedEventArgs e)
    {
        _logger.LogDebug("FSW event received. Type=Renamed OldPath={O} NewPath={N}",
                          e.OldFullPath, e.FullPath);
        _ = TryEnqueueAsync(new ChangeEvent(e.FullPath, WatcherChangeTypes.Renamed,
                                            DateTime.UtcNow, e.OldFullPath));
    }

    private void FireDebounce(object? state)
    {
        var key = (string)state!;
        if (_debounce.TryRemove(key, out var t)) t.Dispose();
        if (!_latestEvent.TryRemove(key, out var e)) return;   // should always succeed
        _logger.LogDebug("Debounce fired. Path={P} FinalType={T}  Enqueued.", key, e.ChangeType);
        _ = TryEnqueueAsync(new ChangeEvent(e.FullPath, e.ChangeType, DateTime.UtcNow));
    }

    private void OnError(object _, ErrorEventArgs e)
    {
        _logger.LogWarning("FSW buffer overflow or error: {M}. Restarting watcher.",
                            e.GetException().Message);
        InitWatcher();

        // Debounce the recovery scan: coalesce rapid overflow events into a single full re-hash.
        if (Interlocked.Exchange(ref _recoveryScheduled, 1) == 0)
        {
            _ = Task.Delay(_config.RecoveryDelayMs, _ct).ContinueWith(_ =>
            {
                Interlocked.Exchange(ref _recoveryScheduled, 0);
                _logger.LogInformation("FSW overflow recovery: starting catch-up scan.");
                _ = _catchUp.RunAllJobsParallelAsync(_ct);
            }, TaskScheduler.Default);
        }
    }

    private async Task TryEnqueueAsync(ChangeEvent ev)
    {
        // Wait up to 1 s; if still full, log + trigger CatchUp recovery.
        using var cts    = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_ct, cts.Token);
        try
        {
            await _queue.Writer.WriteAsync(ev, linked.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Audit event queue full — triggering CatchUpScanner. DroppedPath={P}", ev.FullPath);
            _ = Task.Run(() => _catchUp.RunAllJobsParallelAsync(_ct));
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (var ev in _queue.Reader.ReadAllAsync(_ct))
            {
                try { await _handler.HandleAsync(ev); }
                catch (Exception ex) { _logger.LogError(ex, "Error processing event. Path={P}", ev.FullPath); }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        foreach (var t in _debounce.Values) t.Dispose();
        _latestEvent.Clear();
    }
}
