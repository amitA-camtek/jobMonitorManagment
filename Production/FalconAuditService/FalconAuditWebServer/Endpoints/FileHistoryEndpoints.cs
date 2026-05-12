namespace FalconAuditWebServer.Endpoints;

using FalconAuditWebServer.Services;

public static class FileHistoryEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/jobs/{jobName}/history/{*filePath}", async (
            string jobName, string filePath, QueryRepository repo, JobDiscoveryService discovery) =>
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return Results.BadRequest("filePath required.");

            var jobRoot = Path.GetFullPath(Path.Combine(discovery.WatchPath, jobName));
            string full;
            try { full = Path.GetFullPath(Path.Combine(jobRoot, filePath)); }
            catch { return Results.BadRequest("Invalid file path."); }

            if (!full.StartsWith(jobRoot, StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest("Invalid file path.");

            var relPath = filePath.Replace('/', '\\');
            var history = await repo.GetFileHistoryAsync(jobName, relPath);
            return Results.Ok(history);
        });
    }
}
