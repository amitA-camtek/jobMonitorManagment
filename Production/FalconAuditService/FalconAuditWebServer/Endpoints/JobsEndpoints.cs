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

        // Falcon.Net's FalconAuditClient.TryDeleteJob calls this synchronously BEFORE
        // Directory.Delete so the service can close write+read SQLite connections and
        // remove .audit\ first — letting Falcon's recursive delete succeed first try
        // with no IOException. AllowAnonymous because Falcon's UI thread blocks on
        // this call: a Negotiate challenge handshake here would add ~600ms per delete
        // and desynchronizes Falcon's post-delete state, leaving the next "Open Job"
        // unable to load (zones loaded from stale path → empty UI). The service binds
        // to 127.0.0.1 only (see Kestrel config), so this is local-only access.
        api.MapDelete("/jobs/{jobName}", async (string jobName, JobDiscoveryService discovery, ShardEvictionService eviction) =>
        {
            if (string.IsNullOrWhiteSpace(jobName) ||
                jobName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return Results.BadRequest("Invalid job name.");

            var jobPath = Path.Combine(discovery.WatchPath, jobName);
            await eviction.EvictNowAsync(jobName, jobPath, "API delete");
            return Results.Ok();
        }).AllowAnonymous();
    }
}
