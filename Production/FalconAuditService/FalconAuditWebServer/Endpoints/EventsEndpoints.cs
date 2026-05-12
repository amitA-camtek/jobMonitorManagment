namespace FalconAuditWebServer.Endpoints;

using FalconAuditWebServer.Models;
using FalconAuditWebServer.Services;

public static class EventsEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/jobs/{jobName}/events", GetEvents);
        api.MapGet("/jobs/{jobName}/events/{id:long}", GetEvent)
           .RequireAuthorization("AuditorOnly");
        api.MapGet("/jobs/{jobName}/report", GetReport);
    }

    private static async Task<IResult> GetEvents(
        string jobName, QueryRepository repo,
        string? module, string? priority, string? service,
        string? eventType, string? machine, string? from, string? to, string? path,
        string? fileEra = null,
        bool excludeCreated = true,
        int page = 1, int pageSize = 50, string sort = "desc")
    {
        pageSize = Math.Min(Math.Max(pageSize, 1), 500);
        page     = Math.Max(page, 1);
        if (!string.Equals(sort, "asc",  StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(sort, "desc", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest("Invalid sort direction. Use 'asc' or 'desc'.");
        var filter = new EventFilter
        {
            Module=module, Priority=priority, Service=service, EventType=eventType,
            Machine=machine, From=from, To=to, Path=path, FileEra=fileEra,
            Page=page, PageSize=pageSize, Sort=sort,
            ExcludeCreated=excludeCreated
        };
        var (items, total) = await repo.GetEventsAsync(jobName, filter);
        return Results.Ok(new { Total = total, Page = page, PageSize = pageSize, Items = items });
    }

    private static async Task<IResult> GetEvent(string jobName, long id, QueryRepository repo)
    {
        var detail = await repo.GetEventAsync(jobName, id);
        return detail is null ? Results.NotFound() : Results.Ok(detail);
    }

    private static async Task<IResult> GetReport(
        string jobName, QueryRepository repo, HttpContext ctx,
        string? from, string? to, string format = "json",
        int pageSize = 1000)
    {
        pageSize = Math.Min(Math.Max(pageSize, 1), 5000);

        // Exclude 'Created' events from the report unless the job was first set up
        // within this report period. When from is null we show the full history,
        // which naturally includes the job-creation Created events, so no exclusion.
        bool excludeCreated = false;
        if (from is not null)
        {
            var firstEvent = await repo.GetJobFirstEventTimeAsync(jobName);
            bool jobCreatedInPeriod = firstEvent is not null &&
                string.Compare(firstEvent, from, StringComparison.Ordinal) >= 0;
            excludeCreated = !jobCreatedInPeriod;
        }

        var filter = new EventFilter { From = from, To = to, PageSize = pageSize, Sort = "asc",
                                       ExcludeCreated = excludeCreated,
                                       FileEra = "Runtime" };
        var (items, total) = await repo.GetEventsAsync(jobName, filter);

        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
            return BuildCsv(jobName, items, ctx);

        return Results.Ok(new
        {
            Job         = jobName,
            From        = from,
            To          = to,
            Total       = total,
            GeneratedAt = DateTimeOffset.UtcNow.ToString("O"),
            Items       = items
        });
    }

    // ── report builders ──────────────────────────────────────────────────────

    private static IResult BuildCsv(string jobName, List<AuditEventSummary> items, HttpContext ctx)
    {
        static string CsvField(string? v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            if (v.Contains(',') || v.Contains('"') || v.Contains('\n'))
                return "\"" + v.Replace("\"", "\"\"") + "\"";
            return v;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Time,User,Setup,Recipe,File Name,File Description,Parameter Change,Diff");
        foreach (var e in items)
        {
            var fileName = Path.GetFileName(e.RelFilepath);
            sb.Append(CsvField(FormatTime(e.ChangedAt))).Append(',')
              .Append(CsvField(e.LoginUser ?? "")).Append(',')
              .Append(CsvField(e.Setup ?? "")).Append(',')
              .Append(CsvField(e.Recipe ?? "")).Append(',')
              .Append(CsvField(fileName)).Append(',')
              .Append(CsvField(e.FileDescription)).Append(',')
              .Append(CsvField(e.ChangeSummary)).Append(',')
              .AppendLine(CsvField(e.DiffText ?? ""));
        }

        var safeJobName = System.Text.RegularExpressions.Regex.Replace(jobName, @"[^\w\-]", "_");
        ctx.Response.Headers["Content-Disposition"] =
            $"attachment; filename=\"{safeJobName}-report.csv\"";
        return Results.Content(sb.ToString(), "text/csv; charset=utf-8");
    }

    private static string FormatTime(string iso)
    {
        if (DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToLocalTime().ToString("yyyy-MM-dd  HH:mm:ss");
        return iso;
    }
}
