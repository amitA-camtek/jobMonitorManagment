namespace FalconAuditWebServer.Endpoints;

using FalconAuditService;
using FalconAuditWebServer.Services;

public static class JobsEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/jobs", async (QueryRepository repo) =>
            Results.Ok(await repo.ListJobsAsync()));

        api.MapGet("/jobs/{jobName}/manifest", async (string jobName, JobDiscoveryService discovery, ShardRegistry shards) =>
        {
            // Drain pending manifest bumps before reading so the response is current.
            if (shards.TryGet(jobName, out var queue) && queue is not null)
                await queue.FlushAsync();

            var manifestPath = Path.Combine(discovery.WatchPath, jobName, ".audit", "manifest.json");
            if (!File.Exists(manifestPath)) return Results.NotFound();
            try
            {
                var json = await File.ReadAllTextAsync(manifestPath);
                return Results.Content(json, "application/json");
            }
            catch { return Results.StatusCode(500); }
        });

        // Note: there used to be a MapDelete("/jobs/{jobName}") here that Falcon
        // called synchronously before its own recursive Directory.Delete. It was
        // there to close audit.db handles before the walk so the delete wouldn't
        // hit IOException. Under the lazy-connection model (no long-lived SQLite
        // handles, Pooling=False) the call provided no benefit. Eviction now runs
        // entirely from DirectoryWatcher onDeparted; the resurrection guard in
        // ShardRegistry.GetOrCreate prevents a fresh shard from being opened
        // during Falcon's in-flight recursive delete.
    }
}
