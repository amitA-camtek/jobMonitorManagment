# FalconAuditService — Security Review

**Reviewed:** 2026-05-05  
**Reviewer:** Claude (security-reviewer agent)  
**Codebase root:** `C:\Amit\jobMonitorManagment\Production\FalconAuditService\FalconAuditWebServer\`  
**Design files present:** No — generic OWASP + supplied threat model applied.

---

## Summary

| Severity | Count |
|---|---|
| Critical | 3 |
| High | 6 |
| Medium | 5 |
| Low | 4 |
| **Total** | **18** |

---

## Critical Findings

---

### C-1 — Kestrel binds to `0.0.0.0:5100` over plain HTTP while Negotiate auth is in use

**File:** `FalconAuditWebServer/appsettings.json:12`  
**OWASP:** A02 Cryptographic Failures / A07 Authentication Failures

**Description:**  
The Kestrel endpoint is configured as `http://0.0.0.0:5100`. HTTP Negotiate (NTLM/Kerberos) transmits authentication tokens in the clear when the channel has no TLS. An attacker on the same network segment can capture the NTLM challenge/response exchange with a passive sniffer and crack credentials offline (NTLM), or relay the token to another host (NTLM relay attack). Furthermore, `MonitorConfig.ApiBindAddress` defaults to `"127.0.0.1"` but this value is never read or applied in `Program.cs` — the effective binding is `0.0.0.0`, exposing the service to every network interface.

**Vulnerable config:**
```json
"Kestrel": {
  "Endpoints": {
    "Http": { "Url": "http://0.0.0.0:5100" }
  }
}
```

**Recommendation:**  
- Change the URL to `https://127.0.0.1:5100` if the service is consumed only by local processes, or add TLS with a machine certificate if remote access is required.  
- Alternatively, if TLS is not feasible, restrict to loopback: `http://127.0.0.1:5100`. Callers on other hosts must then go through a TLS-terminating reverse proxy.  
- Wire `MonitorConfig.ApiBindAddress` in `Program.cs` or remove it to avoid misleading operators.

---

### C-2 — `GET /api/jobs/{jobName}/manifest` performs unrestricted path traversal via `jobName`

**File:** `FalconAuditWebServer/Endpoints/JobsEndpoints.cs:14`  
**OWASP:** A01 Broken Access Control / A03 Injection (Path Traversal)

**Description:**  
The endpoint builds a filesystem path by concatenating the user-supplied `{jobName}` route parameter directly with the watch root:

```csharp
var manifestPath = Path.Combine(discovery.WatchPath, jobName, ".audit", "manifest.json");
```

`Path.Combine` on Windows does **not** reject `..` segments. A caller can supply `jobName = ".."` or `"..%2F.."` (URL-decoded by routing before the handler sees it), yielding a path outside `C:\job\`. For example:

- `GET /api/jobs/..%2Fbis%2Fdata%2FApps%2FFileClassificationRules.json%2F../manifest`  
- `GET /api/jobs/..\..\bis\bin\FalconAuditService\appsettings/manifest` (URL path traversal)

The handler returns the full JSON of any reachable file named `manifest.json` on the machine, including configuration or credential files placed in nested `.audit` folders by an attacker.

Additionally, the endpoint has **no `[Authorize]` attribute** and no call to `RequireAuthorization()`, so only the global `FallbackPolicy` (RequireAuthenticatedUser) applies — **no role check** — meaning any authenticated domain user can reach it.

**Recommendation:**  
Add a canonical-path guard identical to the one already present in `FileHistoryEndpoints.cs`:
```csharp
var jobRoot = Path.GetFullPath(Path.Combine(discovery.WatchPath, jobName));
if (!jobRoot.StartsWith(
        Path.GetFullPath(discovery.WatchPath),
        StringComparison.OrdinalIgnoreCase))
    return Results.BadRequest("Invalid job name.");
```
Require the `AuditorOnly` policy on this endpoint.

---

### C-3 — `JobSummary` leaks absolute shard path to all authenticated users via `GET /api/jobs`

**File:** `FalconAuditWebServer/Models/JobSummary.cs:8` / `FalconAuditWebServer/Services/QueryRepository.cs:89`  
**OWASP:** A01 Broken Access Control / Information Disclosure

**Description:**  
`ListJobs()` populates and `GET /api/jobs` returns the `ShardPath` field, which is the absolute filesystem path of the per-job SQLite database (e.g. `C:\job\JOB001\.audit\audit.db`). This information is returned to **any authenticated domain user** because the endpoint has no `[Authorize(Policy = "AuditorOnly")]` attribute. Exposing the exact shard path:

1. Reveals the internal directory layout to attackers, reducing effort for privilege-escalation or lateral movement.  
2. Could be combined with C-2 to construct precise traversal strings.

**Recommendation:**  
- Remove `ShardPath` from `JobSummary` or mark it `[JsonIgnore]` so it is never serialised to API consumers.  
- Apply `RequireAuthorization("AuditorOnly")` to the `/api/jobs` route, or at minimum ensure the role policy is enforced.

---

## High Findings

---

### H-1 — Swagger UI unconditionally exposed in production

**File:** `FalconAuditWebServer/Program.cs:125-126`  
**OWASP:** A05 Security Misconfiguration

**Description:**  
`app.UseSwagger()` and `app.UseSwaggerUI()` are called without any environment guard. In production the Swagger UI at `/swagger` enumerates every endpoint, accepted parameters, and response schemas. It provides an interactive client that lowers the bar for reconnaissance and automated exploitation.

**Recommendation:**  
```csharp
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
```
If Swagger is intentionally kept for an intranet environment, it should at minimum require authentication via `app.UseAuthorization()` before the Swagger middleware, or be placed behind the `AuditorOnly` policy.

---

### H-2 — `GET /api/jobs` and `GET /api/jobs/{jobName}/events` lack `AuditorOnly` role enforcement

**File:** `FalconAuditWebServer/Endpoints/JobsEndpoints.cs:9` / `FalconAuditWebServer/Endpoints/EventsEndpoints.cs:11`  
**OWASP:** A01 Broken Access Control

**Description:**  
Only `GetEvent` (single event detail, line 37) carries `[Authorize(Policy = "AuditorOnly")]`. The list endpoints — `GET /api/jobs` (all job summaries), `GET /api/jobs/{jobName}/events` (paginated events), and `GET /api/jobs/{jobName}/report` — are protected only by the fallback `RequireAuthenticatedUser` policy. Any domain account that can reach port 5100 (not just Auditor-role members) can retrieve the full audit event list and download CSV reports containing file change histories, usernames, and file content diffs.

**Recommendation:**  
Apply `.RequireAuthorization("AuditorOnly")` to every route group or individual handler in `JobsEndpoints`, `EventsEndpoints`, and `FileHistoryEndpoints`. Since `Map()` uses a `RouteGroupBuilder`, the simplest fix is:
```csharp
public static void Map(RouteGroupBuilder api)
{
    var g = api.MapGroup("/jobs").RequireAuthorization("AuditorOnly");
    g.MapGet("", ...);
    // ...
}
```

---

### H-3 — `AlterTableAddColumns` interpolates column definition strings into SQL without parameterisation

**File:** `FalconAuditWebServer/SqliteRepository.cs:168`  
**OWASP:** A03 Injection (SQL Injection)

**Description:**  
Schema migration injects column-definition strings directly into `ALTER TABLE` statements:
```csharp
ac.CommandText = $"ALTER TABLE audit_log ADD COLUMN {col}";
```
`col` is sourced from the hard-coded `string[]` arrays inside `MigrateSchema()` and is not user-supplied today. However, this pattern is fragile: if a future developer passes any externally influenced value through this method, or if the strings are ever externalised to configuration, it becomes a direct SQL injection channel. SQLite's `ALTER TABLE ADD COLUMN` does not support parameter binding for the column definition.

**Recommendation:**  
Document with a prominent code comment that `columnDefs` must only ever contain compile-time constants and must never be derived from user input or configuration. Consider replacing the string-interpolation pattern with a per-column whitelist check (validate the column name portion against a regex `^[a-zA-Z_][a-zA-Z0-9_ ]*$` before interpolation).

---

### H-4 — `sort` query parameter interpolated into SQL `ORDER BY` clause without validation

**File:** `FalconAuditWebServer/Services/QueryRepository.cs:123` and `140`  
**OWASP:** A03 Injection (SQL Injection)

**Description:**  
`GetEventsFromDb` uses `f.Sort` in a string-interpolated ORDER BY:
```csharp
var order = f.Sort == "asc" ? "ASC" : "DESC";
// ...
cmd.CommandText = $@"... ORDER BY changed_at {order} ...";
```
While the ternary expression neutralises free-form injection for `f.Sort`, the `sort` query string parameter in `EventsEndpoints.GetEvents` (line 22) is passed into `EventFilter.Sort` without validation before reaching `QueryRepository`. If the ternary were ever removed or bypassed (e.g. another call site), the raw value would be interpolated. The `sort` parameter also has no length limit.

**Recommendation:**  
Validate `sort` at the endpoint boundary before constructing `EventFilter`:
```csharp
var safeSortDir = string.Equals(sort, "asc", StringComparison.OrdinalIgnoreCase) ? "asc" : "desc";
```
Then in `BuildWhere`/`GetEventsFromDb` retain the ternary but also assert the value is one of the two expected strings and throw otherwise, making the contract explicit.

---

### H-5 — `LoginReader` reads credentials from a world-readable flat file; no integrity validation

**File:** `FalconAuditWebServer/LoginReader.cs:5`  
**OWASP:** A07 Authentication Failures / A08 Data Integrity

**Description:**  
`LoginReader.GetCurrentUser()` reads the username that is recorded in audit log entries from `C:\bis\data\lastLogin.json`, a plain JSON file on disk. Any local process — or a compromised job that can write to `C:\bis\data\` — can forge this file and inject an arbitrary username into every subsequent audit record. This defeats the non-repudiation purpose of the audit log: the `login_user` column cannot be trusted.

The returned value flows directly into `AuditLogEntry.LoginUser` (FileChangeHandler.cs line 172) and is written verbatim to the database. It is also returned in API responses and CSV exports (EventsEndpoints.cs line 102).

**Recommendation:**  
Replace the flat-file login reader with the authenticated identity from the Windows session or, where feasible, the Windows Security event log (recent interactive logon events). At minimum, record `Environment.UserName` or the current thread's `WindowsIdentity.GetCurrent().Name` as a cross-check and flag discrepancies. Apply an NTFS ACL on `C:\bis\data\lastLogin.json` so only the BIS desktop process (not the service account) can write to it.

---

### H-6 — `FileHistoryEndpoints` path-traversal check uses `StartsWith` without trailing separator, allowing sibling-directory bypass

**File:** `FalconAuditWebServer/Endpoints/FileHistoryEndpoints.cs:20`  
**OWASP:** A01 / A03 Path Traversal

**Description:**  
The guard is:
```csharp
if (!full.StartsWith(jobRoot, StringComparison.OrdinalIgnoreCase))
    return Results.BadRequest("Invalid file path.");
```
`jobRoot` is `Path.GetFullPath(Path.Combine(watchPath, jobName))` — for example `C:\job\JOB001`. If a second job is named `JOB001Evil`, then `Path.GetFullPath("C:\job\JOB001Evil\somefile")` starts with `"C:\job\JOB001"` and **passes the check**, allowing cross-job data access.

**Recommendation:**  
Append a path separator before testing:
```csharp
var jobRootWithSep = jobRoot.TrimEnd('\\') + '\\';
if (!full.StartsWith(jobRootWithSep, StringComparison.OrdinalIgnoreCase))
    return Results.BadRequest("Invalid file path.");
```
The same fix should be applied to the manifest endpoint once the traversal guard from C-2 is added.

---

## Medium Findings

---

### M-1 — Unhandled exceptions in endpoint handlers may expose stack traces or absolute paths

**File:** `FalconAuditWebServer/Endpoints/JobsEndpoints.cs:20` / `Program.cs`  
**OWASP:** A05 Security Misconfiguration / Information Disclosure

**Description:**  
The manifest endpoint swallows all exceptions with a bare `catch { return Results.StatusCode(500); }`, which prevents stack-trace disclosure from that specific path. However, the other endpoints (`EventsEndpoints`, `FileHistoryEndpoints`) have no try/catch — unhandled exceptions from `QueryRepository` will propagate to ASP.NET Core's default exception handler. Without `app.UseProblemDetails()` or `UseExceptionHandler` configured in `Program.cs`, the default development exception page may be active, or the full `SqliteException` message (which includes the database file path) may leak in `500` responses depending on the hosting environment.

**Recommendation:**  
Add a global exception handler in `Program.cs`:
```csharp
app.UseExceptionHandler(err => err.Run(async ctx => {
    ctx.Response.StatusCode = 500;
    await ctx.Response.WriteAsJsonAsync(new { error = "Internal server error." });
}));
```
Ensure `ASPNETCORE_ENVIRONMENT` is set to `Production` on the Windows Service host so the developer exception page is never active.

---

### M-2 — `AllowedHosts: "*"` disables host-header validation

**File:** `FalconAuditWebServer/appsettings.json:54`  
**OWASP:** A05 Security Misconfiguration

**Description:**  
With `AllowedHosts: "*"`, ASP.NET Core accepts requests carrying any `Host` header. When combined with `0.0.0.0` binding, this allows DNS rebinding attacks: a malicious page in the browser of a machine running the service can rebind its domain to `127.0.0.1` and issue authenticated requests (the browser sends Negotiate tokens for the bound hostname). Although the practical risk is lower in a Windows-only Negotiate environment, it is unnecessary exposure.

**Recommendation:**  
Set `AllowedHosts` to the actual expected hostname(s):
```json
"AllowedHosts": "localhost;127.0.0.1;<machine-hostname>"
```

---

### M-3 — `ContentCache` has no per-entry size limit; a single large file can exhaust the 200 MB budget

**File:** `FalconAuditWebServer/ContentCache.cs:18`  
**OWASP:** A04 Insecure Design (DoS / resource exhaustion)

**Description:**  
The LRU cache enforces a total byte budget (`200 MB` default) but does not limit the size of a single entry. `MaxContentBytes` in `MonitorConfig` caps what is read from disk (default 1 MB), but if this value is raised in configuration (it is an operator-visible setting), a single P1 file could consume the entire cache budget in one `Set()` call, evicting all other cached content. Additionally, the cache key is the **full absolute path** — a path-traversal exploit that writes many large files outside the watch root would not be cached (the prefix check in `FileChangeHandler` blocks it), but careless future code could bypass the prefix check.

**Recommendation:**  
Add a per-entry cap in `ContentCache.Set()`:
```csharp
if (newBytes > _maxBytes / 4)   // reject entries > 25% of budget
    return;
```
Document that the `MaxContentBytes` config value must not exceed `ContentCache.MaxBytes / 4`.

---

### M-4 — Log injection via user-controlled file paths and file content logged at Debug level

**File:** `FalconAuditWebServer/FileChangeHandler.cs:39`, `FileMonitorService.cs:100`, `CatchUpScanner.cs:124`  
**OWASP:** A09 Logging Failures

**Description:**  
Multiple log statements emit user-controlled filesystem paths without sanitisation:
```csharp
_logger.LogDebug("Processing change. Path={P} ChangeType={T}", ev.FullPath, ev.ChangeType);
_logger.LogDebug("FSW event received. Type={T} Path={P}", e.ChangeType, e.FullPath);
_logger.LogInformation("CatchUpScanner: starting reconciliation scan. Root={R}", scanRoot);
```
Serilog's structured logging with `{Property}` syntax is largely safe against log injection because the value is rendered as a quoted string rather than interpolated as format text. However, the rolling file sink writes plain text with `{Message:lj}`, meaning a `\n` or `\r\n` embedded in a crafted path or filename (possible via FSW on some edge cases) could inject fake log lines. An attacker who can create files with newline characters in their names (on NTFS, disallowed in most shells but possible programmatically) could inject arbitrary structured log entries.

**Recommendation:**  
Sanitise path values before logging by replacing control characters:
```csharp
static string SanitiseForLog(string? v) =>
    v is null ? "" : v.Replace("\r", "\\r").Replace("\n", "\\n");
```
Apply to all path parameters in log calls. Alternatively, consider raising the minimum file-sink log level to `Information` to reduce the volume of debug-level entries that contain raw paths.

---

### M-5 — `manageEventSource: false` for the EventLog sink silently drops Warning/Error entries when the source does not exist

**File:** `FalconAuditWebServer/appsettings.json:46`  
**OWASP:** A09 Logging Failures

**Description:**  
With `manageEventSource: false`, Serilog will throw (and swallow) an exception if the `FalconAuditService` Windows EventLog source has not been pre-registered. The `install.ps1` script does not register the event source via `New-EventLog` or `sc.exe`. As a result, Warning and Error events — including authentication failures — may be silently lost from the Application event log, reducing visibility of attacks.

**Recommendation:**  
Add to `install.ps1`:
```powershell
if (-not [System.Diagnostics.EventLog]::SourceExists('FalconAuditService')) {
    New-EventLog -LogName Application -Source 'FalconAuditService'
}
```
Or set `manageEventSource: true` in `appsettings.json` and ensure the service account has the necessary registry write permission on first run.

---

## Low Findings

---

### L-1 — Target framework `net7.0` is out of Microsoft support (EOL May 2024)

**File:** `FalconAuditWebServer/FalconAuditWebServer.csproj` (inferred from `obj/` paths)  
**OWASP:** A06 Vulnerable Components

**Description:**  
.NET 7 reached end of life on 14 May 2024. No further security patches are available from Microsoft. Known CVEs in the .NET 7 runtime or ASP.NET Core 7 will remain unpatched on this service. Current known issues include multiple ASP.NET Core denial-of-service and information-disclosure CVEs patched in .NET 8.x.

**Recommendation:**  
Migrate to .NET 8 (LTS, supported until November 2026) or .NET 9 (STS). The minimal-API surface used by this project requires only minor changes.

---

### L-2 — No rate limiting on any API endpoint

**File:** `FalconAuditWebServer/Program.cs` (no `AddRateLimiter` present)  
**OWASP:** A07 Authentication Failures

**Description:**  
There is no rate limiting on authentication challenges or API endpoints. An attacker with network access to port 5100 can issue unlimited requests, enabling brute-force or credential-stuffing attacks against the Negotiate endpoint, and denial-of-service via large `pageSize` (capped at 500 for events, 5000 for report) queries that execute repeated SQLite scans.

**Recommendation:**  
Add ASP.NET Core rate limiting (available in .NET 7+):
```csharp
builder.Services.AddRateLimiter(o => o.AddFixedWindowLimiter("api",
    cfg => { cfg.PermitLimit = 100; cfg.Window = TimeSpan.FromMinutes(1); }));
app.UseRateLimiter();
```
Apply to the `/api` group.

---

### L-3 — `Content-Disposition` header in CSV response uses unsanitised `jobName` — potential header injection

**File:** `FalconAuditWebServer/Endpoints/EventsEndpoints.cs:110`  
**OWASP:** A03 Injection (HTTP Header Injection)

**Description:**  
```csharp
ctx.Response.Headers["Content-Disposition"] =
    $"attachment; filename=\"{jobName}-report.csv\"";
```
`jobName` comes from the route parameter without sanitisation. A job name containing a double-quote or CRLF sequence (unlikely in practice but not validated) could break out of the `filename` field and inject additional response headers. Most modern browsers mitigate this, but server-side sanitisation is still required per RFC 6266.

**Recommendation:**  
Sanitise `jobName` before use in headers:
```csharp
var safeJobName = Regex.Replace(jobName, @"[^\w\-]", "_");
ctx.Response.Headers["Content-Disposition"] =
    $"attachment; filename=\"{safeJobName}-report.csv\"";
```

---

### L-4 — `LoginReader` path hardcoded to `C:\bis\data\lastLogin.json`; no configuration override

**File:** `FalconAuditWebServer/LoginReader.cs:5`  
**OWASP:** A05 Security Misconfiguration

**Description:**  
The file path `C:\bis\data\lastLogin.json` is a compile-time constant with no appsettings override. If the BIS installation uses a non-default data directory, the service silently returns `null` for every `LoginUser` field without alerting the operator, producing an audit log with missing user attribution that is indistinguishable from a tampered log.

**Recommendation:**  
Move the path to `appsettings.json` under the `AuditService` section and inject it via `IConfiguration` in the `LoginReader` constructor, consistent with how `WatchPath` and classification rules paths are handled.

---

## Clean Areas

The following areas were reviewed and no actionable findings were identified:

- **SQL parameterisation (write path):** `SqliteRepository.InsertAuditEventAsync`, `UpsertBaselineAsync`, `DeleteBaselineAsync`, `SetConfigValueAsync`, `GetBaselineAsync` — all use `AddWithValue` parameterised queries consistently.
- **SQL parameterisation (read path):** `QueryRepository.GetEvent`, `GetFileHistory`, `GetJobFirstEventTime` — parameterised correctly. `BuildWhere` / `BindFilter` correctly pair every `@param` placeholder with a bound value; no user input is interpolated into query text.
- **Hash algorithm:** `HashHelper.cs` uses `SHA256.Create()` — appropriate for integrity fingerprinting (non-cryptographic password use).
- **ContentCache thread safety:** `ContentCache` consistently acquires `_lock` for all mutations and reads; the LRU eviction logic is correct.
- **FileHistoryEndpoints path normalisation (partial):** `Path.GetFullPath` is correctly used to resolve the canonical path before comparison; the logic is sound modulo the separator issue documented in H-6.
- **ShardRegistry race condition:** The double-checked locking pattern and `ConcurrentDictionary.GetOrAdd` usage in `ShardRegistry.GetOrCreate` correctly handles concurrent shard creation without leaking connections.
- **LDAP / DirectoryServices:** No LDAP queries were found in `LoginReader.cs` or elsewhere; it only reads a local file. No injection surface is present.
- **SSRF:** No `HttpClient` or outbound HTTP calls driven by user input were found in the codebase.
- **install.ps1 command injection:** PowerShell variables in `sc.exe` invocations are quoted correctly; no user-supplied input flows into shell commands.
- **Swagger authentication bypass:** While Swagger is improperly exposed (H-1), the Swagger UI itself does not bypass authentication — all API calls made through it still go through the Negotiate middleware.
