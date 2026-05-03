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
            string? fileEra = null,
            bool excludeCreated = true,
            int page = 1, int pageSize = 50, string sort = "desc") =>
        {
            pageSize = Math.Min(Math.Max(pageSize, 1), 500);
            page     = Math.Max(page, 1);
            var filter = new EventFilter
            {
                Module=module, Priority=priority, Service=service, EventType=eventType,
                Machine=machine, From=from, To=to, Path=path, FileEra=fileEra,
                Page=page, PageSize=pageSize, Sort=sort,
                ExcludeCreated=excludeCreated
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

        api.MapGet("/jobs/{jobName}/report", (
            string jobName, QueryRepository repo, HttpContext ctx,
            string? from, string? to, string format = "html",
            int pageSize = 1000) =>
        {
            pageSize = Math.Min(Math.Max(pageSize, 1), 5000);

            // Exclude 'Created' events from the report unless the job was first set up
            // within this report period.  When from is null we show the full history, which
            // naturally includes the job-creation Created events, so no exclusion needed.
            bool excludeCreated = false;
            if (from is not null)
            {
                var firstEvent = repo.GetJobFirstEventTime(jobName);
                bool jobCreatedInPeriod = firstEvent is not null &&
                    string.Compare(firstEvent, from, StringComparison.Ordinal) >= 0;
                excludeCreated = !jobCreatedInPeriod;
            }

            var filter = new EventFilter { From = from, To = to, PageSize = pageSize, Sort = "asc",
                                           ExcludeCreated = excludeCreated,
                                           FileEra = "Runtime" };
            var (items, total) = repo.GetEvents(jobName, filter);

            if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
                return BuildCsv(jobName, items, ctx);

            var now      = DateTime.Now;
            var htmlBody = BuildHtmlContent(jobName, items, total, from, to, now);
            const string outDir = @"C:\Amit\html";
            Directory.CreateDirectory(outDir);
            var safeJob  = string.Concat(jobName.Select(c =>
                Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var htmlPath = Path.Combine(outDir, $"{safeJob}-{now:yyyyMMdd-HHmmss}.html");
            File.WriteAllText(htmlPath, htmlBody, System.Text.Encoding.UTF8);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                { FileName = htmlPath, UseShellExecute = true });
            return Results.Ok(new
            {
                Job         = jobName,
                From        = from,
                To          = to,
                Total       = total,
                GeneratedAt = now.ToString("yyyy-MM-dd HH:mm:ss"),
                HtmlFile    = htmlPath,
                Items       = items
            });
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

        ctx.Response.Headers["Content-Disposition"] =
            $"attachment; filename=\"{jobName}-report.csv\"";
        return Results.Content(sb.ToString(), "text/csv; charset=utf-8");
    }

    private static string BuildHtmlContent(
        string jobName, List<AuditEventSummary> items, long total,
        string? from, string? to, DateTime genTime)
    {
        static string E(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");

        var rows = new System.Text.StringBuilder();
        foreach (var e in items)
        {
            var fileName = Path.GetFileName(e.RelFilepath);
            var fileCell = string.IsNullOrWhiteSpace(e.FileDescription)
                ? E(fileName)
                : $"<strong>{E(fileName)}</strong><br><span class=\"sub\">{E(e.FileDescription)}</span>";

            var ext       = Path.GetExtension(fileName).ToLowerInvariant();
            var isTextIni = ext is ".ini" or ".txt";
            var diffBlock = "";
            if (!string.IsNullOrWhiteSpace(e.DiffText))
            {
                var diffLines = new System.Text.StringBuilder();
                foreach (var line in e.DiffText.Split('\n'))
                {
                    if (isTextIni)
                    {
                        // Show only updated lines — strip the leading '+', skip removed lines and headers
                        if (line.StartsWith('+') && !line.StartsWith("+++"))
                            diffLines.Append($"<span>{E(line[1..])}</span>\n");
                    }
                    else
                    {
                        var cls = line.StartsWith('+') ? " class=\"da\""
                                : line.StartsWith('-') ? " class=\"dr\"" : "";
                        diffLines.Append($"<span{cls}>{E(line)}</span>\n");
                    }
                }
                diffBlock = isTextIni && diffLines.Length > 0
                    ? $"<pre class=\"diff diff-inline\">{diffLines}</pre>"
                    : $"<details><summary class=\"diff-toggle\">Show diff</summary>" +
                      $"<pre class=\"diff\">{diffLines}</pre></details>";
            }

            rows.Append("<tr>")
                .Append($"<td class=\"time\">{E(FormatTime(e.ChangedAt))}</td>")
                .Append($"<td class=\"user\">{E(e.LoginUser ?? "")}</td>")
                .Append($"<td class=\"ctx\">{E(e.Setup ?? "")}</td>")
                .Append($"<td class=\"ctx\">{E(e.Recipe ?? "")}</td>")
                .Append($"<td>{fileCell}</td>")
                .Append($"<td>{E(e.ChangeSummary)}{diffBlock}</td>")
                .AppendLine("</tr>");
        }

        var dateRange = (from != null || to != null)
            ? $" &nbsp;&mdash;&nbsp; {E(from ?? "…")} to {E(to ?? "now")}"
            : "";
        var csvUrl    = $"?format=csv{(from != null ? $"&from={Uri.EscapeDataString(from)}" : "")}{(to != null ? $"&to={Uri.EscapeDataString(to)}" : "")}";
        var title      = E(jobName);
        var genTimeStr = E(genTime.ToString("yyyy-MM-dd HH:mm"));

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
                td.ctx  { white-space: nowrap; color: #555; }
                .sub   { color: #888; font-size: .8rem; }
                .empty { text-align: center; padding: 3rem; color: #aaa; font-size: .95rem; }
                @media print { body { background: #fff; } .actions { display: none; } .card { box-shadow: none; } }
                .diff { font-size:.75rem; font-family:Consolas,monospace; margin:.4rem 0 0; background:#f8f8f8; padding:.5rem .75rem; border-radius:4px; border:1px solid #e0e0e0; overflow-x:auto; white-space:pre; }
                .diff-toggle { cursor:pointer; color:#0060b0; font-size:.78rem; }
                .diff-inline { margin-top:.25rem; border-left:3px solid #d0d0d0; }
                .da { color:#1a7a1a; display:block; }
                .dr { color:#b01a1a; display:block; }
                .ai-notice { display:flex; align-items:center; gap:.6rem; background:#e8f2fc; border:1px solid #b3d1f0; border-radius:8px; padding:.6rem 1rem; margin-bottom:1.25rem; font-size:.83rem; color:#1a4a7a; }
                .ai-notice svg { flex-shrink:0; }
                @media print { .ai-notice { display:none; } }
              </style>
            </head>
            <body>
              <div class="page">
                <div class="header">
                  <div>
                    <h1>{{title}} — Change Report</h1>
                    <div class="meta">{{total}} event(s){{dateRange}} &nbsp;&middot;&nbsp; Generated {{genTimeStr}}</div>
                  </div>
                  <div class="actions">
                    <a class="btn" href="{{csvUrl}}">&#8595; Download CSV</a>
                  </div>
                </div>
                <div class="ai-notice">
                  <svg width="16" height="16" viewBox="0 0 16 16" fill="none" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
                    <circle cx="8" cy="8" r="7.5" stroke="#1a4a7a"/>
                    <rect x="7" y="6" width="2" height="6" rx="1" fill="#1a4a7a"/>
                    <circle cx="8" cy="4" r="1" fill="#1a4a7a"/>
                  </svg>
                  File descriptions and parameter change summaries are generated automatically and may not be fully accurate.
                </div>
                <div class="card">
                  <table>
                    <thead>
                      <tr>
                        <th>Time</th>
                        <th>User</th>
                        <th>Setup</th>
                        <th>Recipe</th>
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

        return html;
    }

    private static string FormatTime(string iso)
    {
        if (DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToLocalTime().ToString("yyyy-MM-dd  HH:mm:ss");
        return iso;
    }
}

