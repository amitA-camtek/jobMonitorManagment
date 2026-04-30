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
        .AddJsonFile("appsettings.json", optional: true)
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
        var globalDbPath = builder.Configuration["AuditService:GlobalDbPath"]
                           ?? @"C:\bis\auditlog\global.db";
        return new SqliteRepository(globalDbPath,
            sp.GetRequiredService<ILogger<SqliteRepository>>());
    });

    builder.Services.AddSingleton(sp =>
    {
        var cfg     = sp.GetRequiredService<SqliteRepository>().LoadConfig();
        var section = builder.Configuration.GetSection("AuditService");
        var watch   = section["WatchPath"];
        var rules   = section["ClassificationRulesPath"];
        var param   = section["ParameterDescriptionsPath"];
        var global  = section["GlobalDbPath"];
        if (!string.IsNullOrEmpty(watch))  cfg.WatchPath                 = watch;
        if (!string.IsNullOrEmpty(rules))  cfg.ClassificationRulesPath   = rules;
        if (!string.IsNullOrEmpty(param))  cfg.ParameterDescriptionsPath = param;
        if (!string.IsNullOrEmpty(global)) cfg.GlobalDbPath              = global;
        return cfg;
    });

    builder.Services.AddSingleton<LoginReader>();
    builder.Services.AddSingleton<ContentCache>();
    builder.Services.AddSingleton<ShardRegistry>();
    builder.Services.AddSingleton<ManifestManager>();

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
        var config   = sp.GetRequiredService<MonitorConfig>();
        var shards   = sp.GetRequiredService<ShardRegistry>();
        var manifest = sp.GetRequiredService<ManifestManager>();
        var logger   = sp.GetRequiredService<ILogger<DirectoryWatcher>>();
        return new DirectoryWatcher(config.WatchPath,
            onArrived: (jobName, jobPath) =>
            {
                shards.GetOrCreate(jobName, jobPath);
                manifest.RecordArrival(jobPath, config.MachineName);
            },
            onDeparted: (jobName) =>
            {
                manifest.RecordDeparture(Path.Combine(config.WatchPath, jobName));
                shards.Remove(jobName);
            },
            logger);
    });

    builder.Services.AddSingleton<FileChangeHandler>();
    builder.Services.AddSingleton<CatchUpScanner>();
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
        o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        o.AddPolicy("AuditorOnly", p => p.RequireRole("Auditor"));
    });

    var app = builder.Build();

    app.UseSwagger();
    app.UseSwaggerUI();

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
