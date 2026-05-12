using FalconAuditService;
using FalconAuditService.Models;
using FalconAuditWebServer.Endpoints;
using FalconAuditWebServer.Services;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Serilog;

// Bootstrap logger uses appsettings.json from the exe's BaseDirectory
// (CWD is System32 when launched by SCM as a Windows Service).
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .Build())
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args              = args,
        ContentRootPath   = AppContext.BaseDirectory
    });

    builder.Host.UseWindowsService(o => o.ServiceName = "FalconAuditService");
    builder.Host.UseSerilog();

    // ── Audit service dependencies ─────────────────────────────────────────
    builder.Services.AddSingleton(sp =>
    {
        var section = builder.Configuration.GetSection("AuditService");
        var cfg     = new MonitorConfig();
        cfg.WatchPath = section["WatchPath"]
            ?? throw new InvalidOperationException("AuditService:WatchPath is required in appsettings.json.");
        cfg.ClassificationRulesPath = section["ClassificationRulesPath"]
            ?? throw new InvalidOperationException("AuditService:ClassificationRulesPath is required in appsettings.json.");
        cfg.ParameterDescriptionsPath = section["ParameterDescriptionsPath"]
            ?? throw new InvalidOperationException("AuditService:ParameterDescriptionsPath is required in appsettings.json.");
        var login = section["LoginFilePath"];
        if (!string.IsNullOrEmpty(login)) cfg.LoginFilePath = login;

        if (int.TryParse(section["FlushIntervalSeconds"], out var fi) && fi > 0)
            cfg.FlushIntervalSeconds = fi;
        if (int.TryParse(section["FlushQueueMax"], out var fq) && fq > 0)
            cfg.FlushQueueMax = fq;
        if (int.TryParse(section["ReadConnectionTimeoutSeconds"], out var rt) && rt > 0)
            cfg.ReadConnectionTimeoutSeconds = rt;

        return cfg;
    });

    builder.Services.AddSingleton<LoginReader>();
    builder.Services.AddSingleton<ContentCache>();
    builder.Services.AddSingleton<ShardRegistry>();
    builder.Services.AddSingleton<ManifestManager>();
    builder.Services.AddSingleton<ShardEvictionService>();

    builder.Services.AddSingleton(sp =>
    {
        var config     = sp.GetRequiredService<MonitorConfig>();
        var classifier = new FileClassifier(sp.GetRequiredService<ILogger<FileClassifier>>());
        classifier.LoadRules(config.ClassificationRulesPath);
        return classifier;
    });

    builder.Services.AddSingleton(sp =>
    {
        var config   = sp.GetRequiredService<MonitorConfig>();
        var enricher = new ChangeDescriptionEnricher(
                            sp.GetRequiredService<ILogger<ChangeDescriptionEnricher>>());
        enricher.Load(config.ParameterDescriptionsPath);
        return enricher;
    });

    builder.Services.AddSingleton(sp =>
    {
        var config        = sp.GetRequiredService<MonitorConfig>();
        var shards        = sp.GetRequiredService<ShardRegistry>();
        var manifest      = sp.GetRequiredService<ManifestManager>();
        var originChecker = sp.GetRequiredService<JobOriginChecker>();
        var eviction      = sp.GetRequiredService<ShardEvictionService>();
        var logger        = sp.GetRequiredService<ILogger<DirectoryWatcher>>();
        return new DirectoryWatcher(config.WatchPath,
            onArrived: async (jobName, jobPath) =>
            {
                var queue = shards.GetOrCreate(jobName, jobPath);
                var repo  = queue?.Repository;
                await manifest.RecordArrivalAsync(jobPath, config.MachineName);
                originChecker.ScheduleCheck(jobName, jobPath);
                // After the settle window, close the "JobInit" era so that all
                // subsequent FSW events from FileChangeHandler are classified "Runtime".
                if (repo is not null)
                {
                    var capturedJobName = jobName;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(config.JobSettleTimeSeconds));

                            // The job may have been deleted (UI delete, API delete, manual
                            // Explorer wipe) during the settle window. If the shard is no
                            // longer tracked, audit.db is gone — there's nothing to mark
                            // and "FileEra remains JobInit" is moot, so skip silently
                            // rather than logging a warning users can't act on.
                            if (!shards.TryGet(capturedJobName, out _))
                                return;

                            if (!repo.IsInitialScanDone())
                                await repo.SetInitialScanDoneAsync();
                        }
                        catch (OperationCanceledException) { /* service stopping — expected */ }
                        catch (Exception ex)
                        {
                            Log.Warning(ex,
                                "Settle-window SetInitialScanDone failed for job '{J}'. " +
                                "FileEra will remain 'JobInit' until next scan.",
                                capturedJobName);
                        }
                    });
                }
            },
            // Folder-level Deleted/Renamed: discard any buffered events
            // (audit DB went with the folder), then run the full eviction now —
            // sweeps `.audit\` and `MetaData.ini` so the job folder can be
            // removed if BIS left it half-deleted.
            onDeparted: (jobName) =>
                eviction.EvictNowAsync(jobName, Path.Combine(config.WatchPath, jobName), "folder departed"),
            logger);
    });

    builder.Services.AddSingleton<FileChangeHandler>();
    builder.Services.AddSingleton<CatchUpScanner>();
    builder.Services.AddSingleton<JobOriginChecker>();
    builder.Services.AddSingleton<FileMonitorService>();
    builder.Services.AddHostedService<Worker>();

    // ── Web server dependencies ────────────────────────────────────────────
    builder.Services.AddSingleton<JobDiscoveryService>();
    builder.Services.AddSingleton<QueryRepository>();

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
    builder.Services.AddAuthorization(o =>
    {
        // No FallbackPolicy: endpoints are anonymous unless they explicitly call
        // .RequireAuthorization(...). The previous fallback (RequireAuthenticatedUser)
        // forced a Negotiate handshake on every request including the synchronous DELETE
        // that Falcon's UI thread blocks on — adding ~600ms per delete and corrupting
        // Falcon's post-delete state. Endpoints that hold sensitive data must opt in via
        // .RequireAuthorization("AuditorOnly") (see EventsEndpoints).
        o.AddPolicy("AuditorOnly", p => p.RequireRole("Auditor"));
    });

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseAuthentication();
    app.UseAuthorization();

    var api = app.MapGroup("/api");
    JobsEndpoints.Map(api);
    EventsEndpoints.Map(api);
    FileHistoryEndpoints.Map(api);

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "FalconAuditService terminated unexpectedly.");
}
finally
{
    Log.CloseAndFlush();
}
