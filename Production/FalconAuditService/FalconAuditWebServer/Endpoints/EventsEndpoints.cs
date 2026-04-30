namespace FalconAuditWebServer.Endpoints;

using FalconAuditWebServer.Models;
using FalconAuditWebServer.Services;
using Microsoft.AspNetCore.Authorization;

public static class EventsEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/jobs/{jobName}/events", (
            string jobName, QueryRepository repo,
            string? module, string? priority, string? service,
            string? eventType, string? machine, string? from, string? to, string? path,
            int page = 1, int pageSize = 50, string sort = "desc") =>
        {
            pageSize = Math.Min(Math.Max(pageSize, 1), 500);
            page     = Math.Max(page, 1);
            var filter = new EventFilter
            {
                Module=module, Priority=priority, Service=service, EventType=eventType,
                Machine=machine, From=from, To=to, Path=path, Page=page, PageSize=pageSize, Sort=sort
            };
            var (items, total) = repo.GetEvents(jobName, filter);
            return Results.Ok(new { Total = total, Page = page, PageSize = pageSize, Items = items });
        });

        api.MapGet("/jobs/{jobName}/events/{id:long}", [Authorize(Policy = "AuditorOnly")]
            (string jobName, long id, QueryRepository repo) =>
        {
            var detail = repo.GetEvent(jobName, id);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        api.MapGet("/global/events", (
            QueryRepository repo, JobDiscoveryService discovery,
            int page = 1, int pageSize = 50, string sort = "desc") =>
        {
            pageSize = Math.Min(Math.Max(pageSize, 1), 500);
            page     = Math.Max(page, 1);
            var filter = new EventFilter { Page=page, PageSize=pageSize, Sort=sort };
            var (items, total) = repo.GetEventsFromDb(discovery.GlobalDb, filter);
            return Results.Ok(new { Total = total, Page = page, PageSize = pageSize, Items = items });
        });

        api.MapGet("/jobs/{jobName}/report", (
            string jobName, QueryRepository repo, HttpContext ctx,
            string? from, string? to, string format = "html",
            int pageSize = 1000) =>
        {
            pageSize = Math.Min(Math.Max(pageSize, 1), 5000);
            var filter = new EventFilter { From = from, To = to, PageSize = pageSize, Sort = "asc" };
            var (items, total) = repo.GetEvents(jobName, filter);

            if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
                return BuildCsv(jobName, items, ctx);

            return BuildHtml(jobName, items, total, from, to, ctx);
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
        sb.AppendLine("Time,User,File Name,File Description,Parameter Change");
        foreach (var e in items)
        {
            var fileName = Path.GetFileName(e.RelFilepath);
            sb.Append(CsvField(FormatTime(e.ChangedAt))).Append(',')
              .Append(CsvField(e.LoginUser ?? "")).Append(',')
              .Append(CsvField(fileName)).Append(',')
              .Append(CsvField(e.FileDescription)).Append(',')
              .AppendLine(CsvField(e.ChangeSummary));
        }

        ctx.Response.Headers["Content-Disposition"] =
            $"attachment; filename=\"{jobName}-report.csv\"";
        return Results.Content(sb.ToString(), "text/csv; charset=utf-8");
    }

    private static IResult BuildHtml(
        string jobName, List<AuditEventSummary> items, long total,
        string? from, string? to, HttpContext ctx)
    {
        static string E(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");

        var rows = new System.Text.StringBuilder();
        foreach (var e in items)
        {
            var fileName = Path.GetFileName(e.RelFilepath);
            var fileCell = string.IsNullOrWhiteSpace(e.FileDescription)
                ? E(fileName)
                : $"<strong>{E(fileName)}</strong><br><span class=\"sub\">{E(e.FileDescription)}</span>";

            rows.Append("<tr>")
                .Append($"<td class=\"time\">{E(FormatTime(e.ChangedAt))}</td>")
                .Append($"<td class=\"user\">{E(e.LoginUser ?? "")}</td>")
                .Append($"<td>{fileCell}</td>")
                .Append($"<td>{E(e.ChangeSummary)}</td>")
                .AppendLine("</tr>");
        }

        var dateRange = (from != null || to != null)
            ? $" &nbsp;&mdash;&nbsp; {E(from ?? "…")} to {E(to ?? "now")}"
            : "";
        var csvUrl    = $"?format=csv{(from != null ? $"&from={Uri.EscapeDataString(from)}" : "")}{(to != null ? $"&to={Uri.EscapeDataString(to)}" : "")}";
        var title     = E(jobName);
        var genTime   = E(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));

        var html = $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8"/>
              <title>{{title}} — Change Report</title>
              <style>
                *, *::before, *::after { box-sizing: border-box; }
                body   { font-family: Segoe UI, Arial, sans-serif; margin: 0; background: #f5f6f8; color: #222; }
                .page  { max-width: 1200px; margin: 2rem auto; padding: 0 1.5rem; }
                .header { display: flex; justify-content: space-between; align-items: flex-end; flex-wrap: wrap; gap: .5rem; margin-bottom: 1.25rem; }
                h1     { font-size: 1.4rem; margin: 0; }
                .meta  { color: #666; font-size: .85rem; }
                .actions { display: flex; gap: .75rem; }
                .btn   { display: inline-flex; align-items: center; gap: .35rem; padding: .45rem .9rem;
                         border-radius: 6px; font-size: .85rem; font-weight: 600; text-decoration: none;
                         background: #0078d4; color: #fff; border: none; cursor: pointer; }
                .btn:hover { background: #006cbf; }
                .card  { background: #fff; border-radius: 10px; box-shadow: 0 1px 4px rgba(0,0,0,.08); overflow: hidden; }
                table  { border-collapse: collapse; width: 100%; }
                thead tr { background: #f0f2f5; }
                th     { text-align: left; padding: .6rem .9rem; font-size: .8rem; font-weight: 600;
                         text-transform: uppercase; letter-spacing: .04em; color: #555; white-space: nowrap; }
                td     { padding: .55rem .9rem; font-size: .88rem; vertical-align: top;
                         border-bottom: 1px solid #eef0f3; }
                tr:last-child td { border-bottom: none; }
                tr:hover td { background: #f9fafb; }
                td.time { white-space: nowrap; color: #555; font-size: .8rem; font-variant-numeric: tabular-nums; }
                td.user { white-space: nowrap; font-weight: 500; color: #0060b0; }
                .sub   { color: #888; font-size: .8rem; }
                .empty { text-align: center; padding: 3rem; color: #aaa; font-size: .95rem; }
                @media print { body { background: #fff; } .actions { display: none; } .card { box-shadow: none; } }
              </style>
            </head>
            <body>
              <div class="page">
                <div class="header">
                  <div>
                    <h1>{{title}} — Change Report</h1>
                    <div class="meta">{{total}} event(s){{dateRange}} &nbsp;&middot;&nbsp; Generated {{genTime}}</div>
                  </div>
                  <div class="actions">
                    <a class="btn" href="{{csvUrl}}">&#8595; Download CSV</a>
                    <a class="btn" href="#" onclick="window.print();return false;">&#128438; Print</a>
                  </div>
                </div>
                <div class="card">
                  <table>
                    <thead>
                      <tr>
                        <th>Time</th>
                        <th>User</th>
                        <th>File</th>
                        <th>Parameter Change</th>
                      </tr>
                    </thead>
                    <tbody>
            {{(rows.Length > 0 ? rows.ToString() : "<tr><td colspan=\"4\" class=\"empty\">No changes found for this period.</td></tr>")}}
                    </tbody>
                  </table>
                </div>
              </div>
            </body>
            </html>
            """;

        return Results.Content(html, "text/html; charset=utf-8");
    }

    private static string FormatTime(string iso)
    {
        if (DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToLocalTime().ToString("yyyy-MM-dd  HH:mm:ss");
        return iso;
    }
}

