namespace FalconAuditWebServer.Endpoints;

using FalconAuditWebServer.Services;

public static class JobsEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/jobs", (QueryRepository repo) =>
            Results.Ok(repo.ListJobs()));

        api.MapGet("/jobs/{jobName}/manifest", (string jobName, JobDiscoveryService discovery) =>
        {
            var manifestPath = Path.Combine(discovery.WatchPath, jobName, ".audit", "manifest.json");
            if (!File.Exists(manifestPath)) return Results.NotFound();
            try
            {
                var json = File.ReadAllText(manifestPath);
                return Results.Content(json, "application/json");
            }
            catch { return Results.StatusCode(500); }
        });
    }
}
