# FalconAuditService — API Contract Review

**Reviewed:** 2026-05-05  
**Reviewer:** api-contract-reviewer (claude-sonnet-4-6)  
**Source root:** `C:\Amit\jobMonitorManagment\Production\FalconAuditService\FalconAuditWebServer`  
**Design file:** _none — context derived from source_

---

## Endpoint Compliance Table

| Endpoint | Present | Correct Method | Auth correct | Sensitive field isolation | Status codes correct |
|---|---|---|---|---|---|
| GET /api/jobs | Yes | Yes | Fallback (authenticated user only — no AuditorOnly) | **FAIL** — `ShardPath` absolute server path in response | Yes |
| GET /api/jobs/{jobName}/manifest | Yes | Yes | Fallback only | OK | **PARTIAL** — 500 returns no body |
| GET /api/jobs/{jobName}/events | Yes | Yes | Fallback only | **FAIL** — `Filepath` absolute path + `Sha256Hash` in list response | Yes |
| GET /api/jobs/{jobName}/events/{id} | Yes | Yes | **FAIL** — `[Authorize]` on method is ineffective in minimal APIs | **FAIL** — `Filepath`, `OldContent`, `Sha256Hash` exposed | Yes |
| GET /api/jobs/{jobName}/report | Yes | Yes | Fallback only | **FAIL** — `Filepath` absolute path + `Sha256Hash` in response | Yes |
| GET /api/jobs/{jobName}/history/{*filePath} | Yes | Yes | Fallback only | **FAIL** — `OldContent` (full file text), `Sha256Hash` exposed | Yes |
| Health check | **MISSING** | N/A | N/A | N/A | N/A |

---

## Findings

### CRITICAL

---

**[CRITICAL]** `Endpoints/EventsEndpoints.cs:37`
**Issue:** `[Authorize(Policy = "AuditorOnly")]` applied to a `static` private method handler has **no effect** in ASP.NET Core minimal APIs. The attribute is read by the endpoint metadata pipeline only when it is placed on the method that is the direct delegate — but in the current code the attribute sits on the method while the route is registered as `api.MapGet("/jobs/{jobName}/events/{id:long}", GetEvent)` without chaining `.RequireAuthorization("AuditorOnly")`. ASP.NET Core minimal API endpoint metadata is collected from the method reference passed to `Map*`, which **does not inspect `[Authorize]` attributes on the handler method for `static` methods used as method groups** — the attribute is silently ignored. As a result `GET /api/jobs/{jobName}/events/{id}` is protected only by the fallback `RequireAuthenticatedUser` policy, not the elevated `AuditorOnly` (Auditor role) requirement.
**Fix:** Chain the authorization requirement on the route registration itself:
```csharp
api.MapGet("/jobs/{jobName}/events/{id:long}", GetEvent)
   .RequireAuthorization("AuditorOnly");
```

---

**[CRITICAL]** `Services/QueryRepository.cs:91` + `Models/JobSummary.cs:6`
**Issue:** `JobSummary.ShardPath` is the absolute filesystem path to the SQLite database file (e.g. `C:\job\JobName\.audit\audit.db`). This is serialised directly into the `GET /api/jobs` list response. Any authenticated user (fallback policy) on the LAN can read the full server filesystem layout, shard DB locations, and drive structure — an information-disclosure issue that aids lateral movement.
**Fix:** Remove `ShardPath` from `JobSummary` (or mark it `[JsonIgnore]`). Expose a calculated read-only field such as `HasData: bool` if callers need presence detection.

---

**[CRITICAL]** `Models/AuditEventSummary.cs:8` + `Services/QueryRepository.cs:149` + `Endpoints/EventsEndpoints.cs:34`
**Issue:** `AuditEventSummary.Filepath` (the absolute server-side file path, e.g. `C:\job\JobName\Setup1\R1\config.ini`) is included in every record returned by `GET /api/jobs/{jobName}/events` and `GET /api/jobs/{jobName}/report`. The list endpoint is accessible to any authenticated user. Absolute paths disclose internal directory structure. `RelFilepath` is sufficient for consumers.
**Fix:** Remove `Filepath` from `AuditEventSummary` (replace with `RelFilepath` only). If backward-compatibility is required, mark `Filepath` `[JsonIgnore]` and document that `RelFilepath` is the canonical field.

---

### HIGH

---

**[HIGH]** `Models/AuditEventDetail.cs:8,19` + `Services/QueryRepository.cs:183,186`
**Issue:** `AuditEventDetail.Filepath` and `AuditEventDetail.OldFilepath` (absolute server paths) are serialised in the `GET /api/jobs/{jobName}/events/{id}` response. Even after the CRITICAL fix above (adding `RequireAuthorization("AuditorOnly")`), broadcasting absolute server paths is unnecessary. `RelFilepath` carries the same business information.
**Fix:** Remove or `[JsonIgnore]` the `Filepath` and `OldFilepath` fields from `AuditEventDetail`; expose `RelFilepath` only.

---

**[HIGH]** `Models/AuditEventSummary.cs:14` + `Models/AuditEventDetail.cs:10` + `Models/FileHistoryItem.cs:9`
**Issue:** `Sha256Hash` of every monitored file is returned in all three list/detail/history responses (events list, event detail, file history). SHA-256 hashes of known good files are a minor but real information-disclosure item: they can assist an attacker in confirming whether a specific file version is present on the machine without having filesystem access. More critically, if any monitored file ever contains secrets (configuration files are a stated use-case), the hash itself leaks version identity. Hashes should be exposed only on the single-record detail endpoint, and only to AuditorOnly users.
**Fix:** Remove `Sha256Hash` from `AuditEventSummary` and `FileHistoryItem`. Keep it on `AuditEventDetail` which already targets AuditorOnly (after the CRITICAL fix). Add a `[JsonIgnore]` or projection-level exclusion.

---

**[HIGH]** `Endpoints/JobsEndpoints.cs:14,21`
**Issue:** `jobName` comes directly from the URL route parameter and is passed to `Path.Combine(discovery.WatchPath, jobName, ".audit", "manifest.json")` without any validation. A caller can send a `jobName` containing path-traversal sequences such as `../../../Windows/win.ini`. Although `File.Exists` limits impact to an information oracle (does a file exist at an arbitrary path?), combined with the `Results.Content(json, "application/json")` on line 19 this allows reading **any arbitrary file** on the server as long as its path can be predicted — the file content is returned verbatim. This is a path-traversal / arbitrary file read vulnerability.
**Fix:** Validate `jobName` against the known job list before constructing the path: check `discovery.KnownJobs.Contains(jobName, StringComparison.OrdinalIgnoreCase)` and return `404 Not Found` if it is not recognised. Also add an allowlist regex (`^[A-Za-z0-9_\-\.]{1,64}$`) to reject any name with separator characters.

---

**[HIGH]** `Models/FileHistoryItem.cs:10` + `Services/QueryRepository.cs:217`
**Issue:** `FileHistoryItem.OldContent` contains the **full pre-change text of every monitored file**. This is returned in the `GET /api/jobs/{jobName}/history/{*filePath}` response to any authenticated user (fallback policy only). If any monitored file ever contained passwords, certificates, or other secrets, the full historical content of those files is disclosed to all authenticated users — not only Auditors.
**Fix:** Apply `.RequireAuthorization("AuditorOnly")` to the file history endpoint. Consider also requiring AuditorOnly for `OldContent` on `AuditEventDetail`.

---

**[HIGH]** `Endpoints/EventsEndpoints.cs:46-47` + line 63
**Issue:** The `GET /api/jobs/{jobName}/report` endpoint accepts `pageSize` up to 5000 (line 49) with no pagination and no page parameter — it fetches all matching records in a single response. A caller can trigger a response containing up to 5000 full event records including `DiffText` (file diffs), which may be megabytes per record, leading to a denial-of-service via memory exhaustion or very large responses.
**Fix:** Reduce the maximum for the report endpoint or require explicit pagination. The `/events` endpoint already caps at 500 items; the report endpoint's separate higher cap of 5000 is inconsistent and risky.

---

**[HIGH]** `Endpoints/EventsEndpoints.cs:110-112`
**Issue:** The `Content-Disposition` header for the CSV report uses `jobName` directly from the URL in an interpolated string: `$"attachment; filename=\"{jobName}-report.csv\""`. If `jobName` contains double-quote characters or semicolons, the header value is malformed and may be exploitable in some HTTP clients (header injection). The `filename` value should be sanitised.
**Fix:** Strip or encode non-ASCII/special characters from `jobName` before embedding in the header, or use `ContentDispositionHeaderValue` from `System.Net.Http.Headers` to construct the header safely.

---

### MEDIUM

---

**[MEDIUM]** `Endpoints/EventsEndpoints.cs:19,20,27-32`
**Issue:** Filter parameters `priority`, `eventType`, `sort`, and `fileEra` are enumerated/finite-value fields but are **not validated against an allowlist**. They are passed directly into `EventFilter` and used in parameterised SQL queries (safe from injection), but there is no input validation:
- `sort` is only tested `== "asc"` (defaults to DESC), so any string is silently accepted — no 400 response for invalid values.
- `priority` accepts arbitrary strings (valid values are likely `P1`/`P2`/`P3`).
- `eventType` accepts arbitrary strings (valid values: `Created`/`Modified`/`Deleted`/`Renamed`).
- `fileEra` accepts arbitrary strings (valid values: `JobInit`/`Runtime`).
These are not SQL-injection risks (parameterised), but returning empty results silently rather than a `400 Bad Request` for invalid enum values makes the API harder to use correctly and hides client bugs.
**Fix:** Validate enumerated parameters against allowlists and return `400 Bad Request` with a descriptive message on invalid values.

---

**[MEDIUM]** `Endpoints/EventsEndpoints.cs:19` — `from` and `to` date filter parameters
**Issue:** The `from` and `to` query string parameters are accepted as raw `string?` values and passed directly as SQLite parameter values for string comparison (`changed_at >= @from`). There is no date parsing or format validation — any string is accepted. A caller sending a non-ISO-8601 string will silently get wrong results rather than a `400 Bad Request`. Additionally, the comparison relies on SQLite's lexicographic ordering of ISO strings, which works only if the stored format is consistent. The contract is implicit and fragile.
**Fix:** Parse `from`/`to` with `DateTime.TryParse` (or `DateTimeOffset`) before use. Return `400 Bad Request` if parsing fails.

---

**[MEDIUM]** `Program.cs:125` + `appsettings.json:53`
**Issue:** `app.UseSwagger()` and `app.UseSwaggerUI()` are unconditionally registered — there is no environment check. The Swagger UI and the machine-readable OpenAPI JSON document are exposed in production at `/swagger`. This exposes the full API surface, parameter names, and response schemas to any authenticated user on the LAN (or unauthenticated user if auth is ever relaxed). For an internal audit service this is a medium risk.
**Fix:** Wrap in `if (app.Environment.IsDevelopment())` or protect the Swagger endpoint with `RequireAuthorization`.

---

**[MEDIUM]** `Endpoints/EventsEndpoints.cs:11` / `Endpoints/FileHistoryEndpoints.cs:9` / `Endpoints/JobsEndpoints.cs:9,12`
**Issue:** No OpenAPI response-type metadata is attached to any endpoint. None of the `Map*` calls chain `.Produces<T>()`, `.ProducesProblem()`, or `.WithOpenApi()`. As a result the Swashbuckle-generated spec declares all responses as status 200 with an untyped schema. This means the API contract is invisible to API consumers and automated tools.
**Fix:** Add `.Produces<JobSummary[]>(200)`, `.ProducesProblem(400)`, etc. to each endpoint registration.

---

**[MEDIUM]** `appsettings.json:53`
**Issue:** `"AllowedHosts": "*"` — the `AllowedHosts` filter is set to wildcard. On Windows/Kestrel this means the `Host` header is not validated. While the service binds to `0.0.0.0:5100` (all interfaces), any `Host` header value is accepted, which can enable Host header injection attacks in some proxy/redirect scenarios.
**Fix:** Set `"AllowedHosts": "localhost;127.0.0.1;<machine-hostname>"` to match the intended deployment surface.

---

**[MEDIUM]** `Program.cs` (absence)
**Issue:** No rate limiting is configured. .NET 7 ships `Microsoft.AspNetCore.RateLimiting` built in. The report endpoint (`/report`) in particular can be called repeatedly to generate large responses. There is no throttle.
**Fix:** Add `builder.Services.AddRateLimiter(...)` with a fixed-window or token-bucket policy and call `app.UseRateLimiter()`. Prioritise the `/report` and `/history` endpoints.

---

**[MEDIUM]** `Program.cs` (absence)
**Issue:** No global exception handler is registered (`UseExceptionHandler` / `AddProblemDetails`). If a repository method or an endpoint handler throws an unhandled exception, ASP.NET Core returns a plain-text `500` response (in Development mode, with a full stack trace; in Production with just `An error occurred`). Error responses are inconsistent with the structured JSON returned by successful endpoints.
**Fix:** Register `app.UseExceptionHandler(...)` or `app.UseProblemDetails()` to return a consistent `application/problem+json` shape for all 4xx/5xx errors. This also prevents Development stack traces leaking if `ASPNETCORE_ENVIRONMENT` is accidentally left as `Development` in production.

---

**[MEDIUM]** `Endpoints/JobsEndpoints.cs:21`
**Issue:** `catch { return Results.StatusCode(500); }` swallows the exception silently and returns a bare `500` with no body. The caller receives no indication of what went wrong, and no ProblemDetails envelope. The exception is also not logged.
**Fix:** Log the exception (`_logger.LogError(ex, ...)`) before returning the 500, and return a `Results.Problem(...)` response for consistency.

---

### LOW

---

**[LOW]** `Endpoints/EventsEndpoints.cs:24` vs `Endpoints/EventsEndpoints.cs:49`
**Issue:** The `/events` endpoint caps `pageSize` at 500 while the `/report` endpoint caps it at 5000. The inconsistency is confusing for API consumers and the `/report` endpoint's higher cap is an amplification risk (covered under HIGH above). At minimum, document the intentional difference.

---

**[LOW]** `Program.cs:131`
**Issue:** The route group is `/api` with no version prefix (`/api/v1/...`). The API is already in production use. Any future breaking change to a response schema or parameter will require a new path version with no migration path. This is a design debt item, not an immediate bug.
**Fix:** Consider routing under `/api/v1/` now and aliasing `/api` → `/api/v1` for backward compatibility, so that future v2 endpoints can be added without breaking existing clients.

---

**[LOW]** `Program.cs` (absence)
**Issue:** No CORS policy is configured. If future web-based consumers (browser frontends or dashboards) need to call this service, CORS will need to be added. The current absence is safe for a server-to-server API, but should be intentional and documented.

---

**[LOW]** `Endpoints/FileHistoryEndpoints.cs:15`
**Issue:** `jobName` in the file history endpoint is used in `Path.Combine(discovery.WatchPath, jobName)` without validation against the known job list — the same path-traversal surface as the manifest endpoint (HIGH finding above). The `GetFullPath` + `StartsWith(jobRoot)` guard on `filePath` only protects the file sub-path; it does not protect against a malicious `jobName` that itself contains `..`. However the impact here is limited to `GetFileHistory(jobName, relPath)` → `ShardPath(jobName)` which calls `File.Exists` on a constructed DB path — no file content is read directly from `jobName`. Nonetheless, the `jobRoot` computation on line 15 does use `jobName` in a path before any job-list membership check.
**Fix:** Apply the same allowlist check for `jobName` as recommended for the manifest endpoint.

---

## Clean Areas

- **SQL parameterisation:** All filter values in `QueryRepository` use `cmd.Parameters.AddWithValue` — no string interpolation into query text. Parameterised throughout.
- **SQLite read-only mode:** `QueryRepository.GetConnection` opens every connection with `Mode=ReadOnly` (line 35). No write path exists in the query layer.
- **`sort` SQL injection:** Although `sort` is unvalidated as an enum, the `BuildWhere` / SQL interpolation of `order` uses a ternary that resolves to either the literal `"ASC"` or `"DESC"` (line 122) — not the raw user string. No SQL injection is possible here.
- **File path traversal (`filePath`):** `FileHistoryEndpoints` correctly resolves both the job root and the supplied path with `Path.GetFullPath` and performs a prefix check (`StartsWith(jobRoot)`) before trusting the combined path.
- **Pagination implementation:** `GetEventsFromDb` uses `LIMIT @ps OFFSET @off` in the SQL query — no in-memory pagination. Pagination is DB-side.
- **Maximum page size:** `/events` enforces a maximum of 500; `/report` enforces 5000 (see HIGH finding for the inconsistency, but neither fetches unbounded rows).
- **HTTP method correctness:** All endpoints are `GET` for read-only data retrieval. No state-changing operation is performed via `GET`.
- **Authentication middleware order:** `app.UseAuthentication()` is called before `app.UseAuthorization()` — correct middleware order.
- **Fallback policy:** `FallbackPolicy = RequireAuthenticatedUser` is set, so all endpoints require Windows authentication at minimum with no opt-out path.
- **`[Authorize]` role name vs policy name:** The policy `"AuditorOnly"` maps to `RequireRole("Auditor")` (line 121 of Program.cs) — the role name used in the AD group and the policy are consistent.
- **Content negotiation:** The API returns only JSON (and CSV for the report) — no XML content negotiation is enabled, which is appropriate for this use case.
- **No write endpoints:** Confirmed. All six registered endpoints are `MapGet`. There are no `MapPost`, `MapPut`, `MapDelete`, or `MapPatch` registrations.
