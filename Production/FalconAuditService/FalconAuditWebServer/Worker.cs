namespace FalconAuditService;

using FalconAuditService.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public class Worker : BackgroundService
{
    private readonly FileMonitorService _monitor;
    private readonly CatchUpScanner     _catchUp;
    private readonly ShardRegistry      _shards;
    private readonly ManifestManager    _manifest;
    private readonly DirectoryWatcher   _dirWatcher;
    private readonly MonitorConfig      _config;
    private readonly ILogger<Worker>    _logger;

    public Worker(
        FileMonitorService monitor, CatchUpScanner catchUp,
        ShardRegistry shards, ManifestManager manifest,
        DirectoryWatcher dirWatcher, MonitorConfig config,
        ILogger<Worker> logger)
    {
        _monitor    = monitor;
        _catchUp    = catchUp;
        _shards     = shards;
        _manifest   = manifest;
        _dirWatcher = dirWatcher;
        _config     = config;
        _logger     = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FalconAuditService starting. WatchPath={W}", _config.WatchPath);
        if (!Directory.Exists(_config.WatchPath))
        {
            _logger.LogCritical("WatchPath does not exist: {P}", _config.WatchPath);
            try
            {
                Directory.CreateDirectory(_config.WatchPath);
                _logger.LogInformation("Created WatchPath: {P}", _config.WatchPath);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Cannot create WatchPath: {P}", _config.WatchPath);
                return;
            }
        }

        // Step 1: register the recursive FSW BEFORE any catch-up work
        _monitor.Start(stoppingToken);
        _dirWatcher.Start();
        _logger.LogInformation("FalconAuditService FSW live.");

        // Step 2: enumerate existing job folders (opens shards, records arrival in manifest)
        _dirWatcher.EnumerateExisting();

        // Step 3: run catch-up scan in PARALLEL across all jobs. Runs after FSW
        // is already live, so any live event during catch-up is queued and processed.
        _ = Task.Run(async () =>
        {
            using var scanTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var scanCts = CancellationTokenSource.CreateLinkedTokenSource(
                                    stoppingToken, scanTimeout.Token);
            try
            {
                await _catchUp.RunAllJobsParallelAsync(scanCts.Token);
                _logger.LogInformation("CatchUpScanner: full reconciliation complete.");
            }
            catch (OperationCanceledException) when (scanTimeout.IsCancellationRequested)
            {
                _logger.LogWarning("CatchUpScanner exceeded 5-min limit.");
            }
            catch (OperationCanceledException) { /* normal shutdown */ }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CatchUpScanner failed.");
            }
        }, stoppingToken);

        _logger.LogInformation("FalconAuditService running.");
        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (TaskCanceledException) { /* normal shutdown */ }
        catch (OperationCanceledException) { /* normal shutdown */ }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("StopAsync requested. Draining queue.");

        // Record departure in manifest for every active job
        foreach (var jobName in _shards.JobNames.ToList())
        {
            var jobPath = Path.Combine(_config.WatchPath, jobName);
            try { _manifest.RecordDeparture(jobPath); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not record departure for {J}", jobName); }
        }

        _dirWatcher.Stop();
        try { await _monitor.StopAsync(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Error stopping FileMonitorService."); }
        await base.StopAsync(cancellationToken);
        _logger.LogInformation("FalconAuditService stopped.");
    }
}
