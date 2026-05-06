# Configuration Validation — FalconAuditService

> Note: No `architecture-design.md` was present. Required keys were derived from
> `Program.cs`, `MonitorConfig.cs`, and all service consumers.
> Reviewed files:
> - `FalconAuditWebServer/appsettings.json`
> - `FalconAuditWebServer/appsettings.Development.json`
> - `FalconAuditWebServer/install.ps1`
> - `FalconAuditWebServer/Properties/launchSettings.json`
> - `FalconAuditWebServer/FalconAuditWebServer.csproj`

---

## Configuration Key Completeness

| Key | Present in appsettings.json | Default correct | Sensitive (hardcoded?) | Notes |
|-----|-----------------------------|-----------------|------------------------|-------|
| AuditService:WatchPath | Yes (line 3) | `C:\job\` matches MonitorConfig default | No | Hardcoded absolute path — acceptable for a fixed-installation Windows service |
| AuditService:ClassificationRulesPath | Yes (line 4) | `C:\bis\data\Apps\FileClassificationRules.json` matches MonitorConfig default | No | install.ps1 copies to `C:\bis\auditlog\` not `C:\bis\data\Apps\` — path mismatch (see Finding H-1) |
| AuditService:ParameterDescriptionsPath | Yes (line 5) | `C:\bis\data\Apps\ParameterDescriptions.json` matches MonitorConfig default | No | Same mismatch as above |
| AuditService:JobSettleTimeSeconds | Yes (line 6) | `30` — matches MonitorConfig default | No | OK |
| AuditService:OriginSampleSize | Yes (line 7) | `10` — matches MonitorConfig default | No | OK |
| AuditService:OriginDeltaMinutes | Yes (line 8) | `5` — matches MonitorConfig default | No | OK |
| AuditService:OriginCopiedRatio | Yes (line 9) | `0.6` — matches MonitorConfig default | No | OK |
| Kestrel:Endpoints:Http:Url | Yes (line 13) | `http://0.0.0.0:5100` | No | Binds all interfaces over plain HTTP — see Findings H-2, H-3 |
| Serilog (Console sink) | Yes (line 27–33) | Configured | No | Console sink on a Windows Service is only useful when SCM captures stdout — see Finding M-1 |
| Serilog (File sink) | Yes (line 34–41) | path `C:\bis\ErrorLog\AuditLog\`, rollingInterval=Day, retainedFileCountLimit=31 | No | Directory not created by install.ps1 — see Finding H-4 |
| Serilog (EventLog sink) | Yes (line 42–50) | source=FalconAuditService, manageEventSource=false | No | EventLog source not registered in install.ps1 — see Finding C-1 |
| AllowedHosts | Yes (line 54) | `"*"` | No | Accepts any Host header — see Finding M-2 |

**Keys present in MonitorConfig.cs but absent from appsettings.json (rely on code defaults only):**

| Key | MonitorConfig default | Risk |
|-----|-----------------------|------|
| AuditService:ApiPort | 5100 | Unused by Kestrel directly (Kestrel uses its own section) — dead field, no risk |
| AuditService:ApiBindAddress | `127.0.0.1` | Unused by Kestrel directly — dead field, but misleadingly implies loopback while Kestrel binds `0.0.0.0` |
| AuditService:DebounceMs | 500 | Absent from appsettings — defaults silently; Medium risk if tuning needed on a specific machine |
| AuditService:FswBufferBytes | 65536 | Absent — FSW overflow risk on high-churn jobs if not tunable without redeployment |
| AuditService:MaxContentBytes | 1048576 | Absent — silently capped at 1 MB |
| AuditService:CaptureContent | true | Absent — no ops toggle without code change |
| AuditService:CatchUpYieldThreshold | 50 | Absent |
| AuditService:RecoveryDelayMs | 30000 | Absent |
| AuditService:MachineName | `Environment.MachineName` | Absent — dynamic default is correct, no issue |

---

## Logging Configuration

| Sink / Target | Configured | Correct for deployment | Notes |
|---|---|---|---|
| Console | Yes | Marginal | Windows Service stdout is not captured by SCM by default. Console output is only visible when running interactively or when a wrapper (NSSM, etc.) redirects it. Not harmful, but produces no value in normal service operation. |
| File (rolling daily) | Yes | Yes | Path `C:\bis\ErrorLog\AuditLog\`, 31-day retention, correct template. Directory not created by install.ps1 (Finding H-4). |
| EventLog | Yes | Yes | `manageEventSource: false` means the source must exist before first Warning/Error log. Not created by install.ps1 (Finding C-1). |
| Minimum level (Default) | Information | Yes | Correct for production. |
| Microsoft/AspNetCore overrides | Warning | Yes | Appropriate. |
| `Microsoft.Hosting` override | Information | Yes | Appropriate — captures host lifecycle events. |

---

## Findings

### Critical

**[SEVERITY: Critical] install.ps1 — EventLog source never registered**

- **File:** `install.ps1` (entire file — no `New-EventLog` call present)
- **Issue:** `appsettings.json:46` sets `"manageEventSource": false`, which means Serilog will NOT attempt to create the event log source itself. If the source `FalconAuditService` does not already exist in the Windows Application Event Log, every attempt to write a Warning or Error will throw an `InvalidOperationException` internally, silently suppressing those log entries and potentially crashing the sink. The install script creates directories and ACLs but never calls `New-EventLog -Source "FalconAuditService" -LogName "Application"`.
- **Fix:** Add to the Install block of `install.ps1`, after ACL setup:
  ```powershell
  if (-not [System.Diagnostics.EventLog]::SourceExists('FalconAuditService')) {
      New-EventLog -LogName 'Application' -Source 'FalconAuditService'
      Write-Host "EventLog source 'FalconAuditService' registered."
  }
  ```
  Alternatively, set `"manageEventSource": true` in `appsettings.json:46` and ensure the service account has the `HKLM\SYSTEM\CurrentControlSet\Services\EventLog\Application` registry write permission.

---

### High

**[SEVERITY: High] appsettings.json:13 — Kestrel bound to all interfaces (0.0.0.0) over plain HTTP**

- **File:** `appsettings.json`, line 13: `"Url": "http://0.0.0.0:5100"`
- **Issue:** The service exposes port 5100 on every network interface of the machine. Because it uses Windows Negotiate (Kerberos/NTLM) authentication, credentials are not sent in plaintext, but the audit data returned by the API is transmitted over unencrypted HTTP. Any host on the same subnet can reach the endpoint and, if authenticated, read audit log data. On an isolated factory/lab network this may be intentional, but it should be a conscious decision rather than an unchecked default.
- **Fix (preferred):** Restrict to loopback (`http://127.0.0.1:5100`) if callers are only on the same machine, or add HTTPS with a certificate. If multi-machine access is required, document the network-isolation assumption explicitly.
- **Also note:** `MonitorConfig.ApiBindAddress` defaults to `127.0.0.1` (MonitorConfig.cs:9), which is the correct loopback default, but this field is not wired into Kestrel — only the `Kestrel:Endpoints:Http:Url` key controls the binding. The field is dead code and creates a misleading impression of security.

---

**[SEVERITY: High] appsettings.json:35–36 — Log directory not created by install.ps1**

- **File:** `appsettings.json`, lines 35–36; `install.ps1` (no `C:\bis\ErrorLog\AuditLog\` creation)
- **Issue:** The File sink writes to `C:\bis\ErrorLog\AuditLog\falconaudit-.log`. If this directory does not exist when the service starts, Serilog will fail to open the file sink. In that scenario the File sink is silently dropped (Serilog self-log), and all structured log output is lost except the Console sink (which is not captured in service mode — see M-1). This is a silent data-loss scenario.
- **Fix:** Add directory creation to `install.ps1`:
  ```powershell
  $LogDir = 'C:\bis\ErrorLog\AuditLog'
  if (-not (Test-Path $LogDir)) {
      New-Item -ItemType Directory -Path $LogDir | Out-Null
      Write-Host "Created log directory: $LogDir"
  }
  icacls $LogDir /grant "NT SERVICE\FalconAuditSvc:(OI)(CI)M" /T | Out-Null
  ```

---

**[SEVERITY: High] install.ps1 — ClassificationRulesPath / ParameterDescriptionsPath mismatch**

- **File:** `install.ps1`, lines 36–46; `appsettings.json`, lines 4–5
- **Issue:** `install.ps1` copies `FileClassificationRules.json` and `ParameterDescriptions.json` to `$DbDir` which defaults to `C:\bis\auditlog`. `appsettings.json` configures `ClassificationRulesPath` as `C:\bis\data\Apps\FileClassificationRules.json` and `ParameterDescriptionsPath` as `C:\bis\data\Apps\ParameterDescriptions.json`. These paths do not match. At startup `FileClassifier.LoadRules(config.ClassificationRulesPath)` and `ChangeDescriptionEnricher.Load(config.ParameterDescriptionsPath)` will attempt to load from `C:\bis\data\Apps\` which the install script never populates. Unless that directory is pre-populated by a separate installer step, this will cause a startup failure or fall back to empty rule sets.
- **Fix:** Either update `appsettings.json` to point to `C:\bis\auditlog\FileClassificationRules.json` (matching where install.ps1 puts them), or update `install.ps1` to copy files to `C:\bis\data\Apps\` and create that directory.

---

**[SEVERITY: High] FalconAuditWebServer.csproj:4 — Target framework net7.0-windows is out of support**

- **File:** `FalconAuditWebServer.csproj`, line 4: `<TargetFramework>net7.0-windows</TargetFramework>`
- **Issue:** .NET 7 reached end-of-life on 14 May 2024. It no longer receives security patches. Running a service that handles authentication tokens (Negotiate/Kerberos) and audit data on an unsupported runtime exposes the application to known, unpatched CVEs.
- **Fix:** Migrate to .NET 8 (LTS, supported until November 2026) or .NET 9. All referenced NuGet packages have equivalents targeting .NET 8. The `net7.0-windows` TFM becomes `net8.0-windows`. Version constraints in the `.csproj` (`7.0.*`) must be updated to `8.0.*` for Microsoft packages.

---

### Medium

**[SEVERITY: Medium] appsettings.json:54 — AllowedHosts: "*"**

- **File:** `appsettings.json`, line 54
- **Issue:** `AllowedHosts: "*"` disables ASP.NET Core's Host header validation middleware. On an internal factory network this is low risk, but it removes one layer of defence against DNS-rebinding attacks. Any Host header value is accepted.
- **Fix:** Set `AllowedHosts` to the actual hostname(s) of the machine, e.g. `"AllowedHosts": "machine-name;localhost;127.0.0.1"`. For a single-machine service, `"localhost;127.0.0.1"` is sufficient.

---

**[SEVERITY: Medium] appsettings.json:27–33 — Console sink provides no value in Windows Service mode**

- **File:** `appsettings.json`, lines 27–33
- **Issue:** The Console sink writes to stdout. When hosted as a Windows Service under SCM, stdout is not captured or persisted. The sink contributes no observability in production and adds minor overhead per log event. It is only useful during interactive development runs (which `launchSettings.json` covers by setting `ASPNETCORE_ENVIRONMENT=Development`). There is no condition on this sink — it fires in both Development and Production environments.
- **Fix:** Either remove the Console sink from `appsettings.json` and add it only to `appsettings.Development.json`, or wrap it with a `"restrictedToMinimumLevel": "Warning"` to reduce noise.

---

**[SEVERITY: Medium] Program.cs:38–41 — Silent fallback to MonitorConfig defaults for WatchPath, ClassificationRulesPath, ParameterDescriptionsPath**

- **File:** `Program.cs`, lines 38–41
- **Issue:** The MonitorConfig factory uses `if (!string.IsNullOrEmpty(...))` guards before assigning values from configuration. If any of these three keys is absent or empty in appsettings.json, the code silently falls back to the hardcoded `MonitorConfig` property defaults (`C:\job\`, `C:\bis\data\Apps\...`). The service does not fail fast. For a required key, this masks configuration errors — the service starts, watches the wrong path, and the operator receives no error.
- **Fix:** Change the factory to throw on empty values:
  ```csharp
  cfg.WatchPath = section["WatchPath"]
      ?? throw new InvalidOperationException("AuditService:WatchPath is required.");
  cfg.ClassificationRulesPath = section["ClassificationRulesPath"]
      ?? throw new InvalidOperationException("AuditService:ClassificationRulesPath is required.");
  cfg.ParameterDescriptionsPath = section["ParameterDescriptionsPath"]
      ?? throw new InvalidOperationException("AuditService:ParameterDescriptionsPath is required.");
  ```
  Alternatively use `services.AddOptions<MonitorConfig>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`.

---

**[SEVERITY: Medium] Program.cs:15 — Bootstrap logger reads appsettings.json as optional**

- **File:** `Program.cs`, line 15: `.AddJsonFile("appsettings.json", optional: true)`
- **Issue:** The bootstrap logger (before the full DI host is built) silently loads no config if appsettings.json is missing. If the file is absent or malformed, Serilog falls back to a default minimal configuration, which for a Windows Service means no logging at all until the host crashes. The fatal exception at line 140 will be written to Serilog, but if the logger itself failed to initialise, this message is lost.
- **Fix:** Change `optional: true` to `optional: false`:
  ```csharp
  .AddJsonFile("appsettings.json", optional: false)
  ```
  Alternatively, keep `optional: true` but add a Serilog self-log output to capture sink initialisation failures: `Serilog.Debugging.SelfLog.Enable(msg => System.Diagnostics.Debug.WriteLine(msg));`

---

**[SEVERITY: Medium] install.ps1:49 — ACL grants to NT SERVICE\FalconAuditSvc but sc.exe creates NT SERVICE\FalconAuditSvc**

- **File:** `install.ps1`, lines 49–56
- **Issue:** The `sc.exe create` command at line 53 specifies `obj= "NT SERVICE\FalconAuditSvc"` (note: `FalconAuditSvc`, not `FalconAuditService`). The ACLs on lines 49–50 also use `FalconAuditSvc`. However, the service binary name registered is `FalconAuditService`. The virtual account name for a service registered as `FalconAuditService` using `NT VIRTUAL ACCOUNT\...` semantics would be `NT SERVICE\FalconAuditService`. Using the explicitly specified `FalconAuditSvc` means the service account name must be explicitly managed. This is internally consistent as written, but the discrepancy between `$ServiceName = 'FalconAuditService'` and the account name `FalconAuditSvc` is non-obvious and should be documented. If someone re-creates the service and omits `obj=`, Windows will default to `LocalSystem`, and the ACL grants will apply to a non-existent account.
- **Fix:** Either rename the virtual account to `NT SERVICE\FalconAuditService` (matching `$ServiceName`) or add an inline comment explaining the deliberate use of a shorter account alias.

---

### Low

**[SEVERITY: Low] launchSettings.json:17 — Development URL (localhost:5052) differs from production Kestrel URL (0.0.0.0:5100)**

- **File:** `Properties/launchSettings.json`, line 17: `"applicationUrl": "http://localhost:5052"`
- **Issue:** The development profile binds to port 5052 while production uses port 5100. This is a minor inconsistency: developers testing on port 5052 will miss any port-specific firewall or routing behaviour that would apply in production at port 5100. Not a security issue, but a source of "works on my machine" confusion.
- **Fix:** Align the dev port to 5100: `"applicationUrl": "http://localhost:5100"`.

---

**[SEVERITY: Low] appsettings.json — Several MonitorConfig tuning keys absent; rely on silent code defaults**

- **File:** `appsettings.json` (entire file)
- **Issue:** The following keys are consumed from `MonitorConfig` but have no entry in `appsettings.json`: `DebounceMs` (500 ms), `FswBufferBytes` (65536), `MaxContentBytes` (1048576), `CaptureContent` (true), `CatchUpYieldThreshold` (50), `RecoveryDelayMs` (30000). For a production service that may need tuning on specific hardware (e.g. high-churn job folders requiring a larger FSW buffer), these being absent from the config file means an operator cannot override them without redeployment.
- **Fix:** Add the keys to `appsettings.json` with their default values, making them discoverable and overridable:
  ```json
  "AuditService": {
    ...
    "DebounceMs":             500,
    "FswBufferBytes":         65536,
    "MaxContentBytes":        1048576,
    "CaptureContent":         true,
    "CatchUpYieldThreshold":  50,
    "RecoveryDelayMs":        30000
  }
  ```

---

**[SEVERITY: Low] appsettings.json — Reload-on-change behaviour vs Singleton services**

- **File:** `appsettings.json` (structural concern)
- **Issue:** ASP.NET Core's default `CreateBuilder` wires `appsettings.json` with `reloadOnChange: true`. All `AuditService` configuration is injected into singletons (`MonitorConfig`, `FileClassifier`, `ChangeDescriptionEnricher`, `DirectoryWatcher`, `JobOriginChecker`) that capture values at construction time. If an operator edits `appsettings.json` at runtime expecting values like `WatchPath` or rule file paths to update, they will not — the singletons hold snapshots. This is not a bug, but undocumented behaviour that creates a false expectation.
- **Fix:** Either (a) add an explicit comment in `appsettings.json` noting that a service restart is required for changes to take effect, or (b) inject `IOptionsMonitor<MonitorConfig>` in the affected services to support live reload (requires significant refactoring, likely not worth it for a Windows Service).

---

**[SEVERITY: Low] appsettings.Development.json — Uses default ASP.NET Core Logging section, not Serilog**

- **File:** `appsettings.Development.json`, lines 1–8
- **Issue:** The Development override configures `Logging:LogLevel` (Microsoft.Extensions.Logging) rather than `Serilog:MinimumLevel`. Because `Program.cs` calls `builder.Host.UseSerilog()`, the `Logging` section is superseded by Serilog's own configuration from `Serilog:*`. The Development overrides have no effect on actual log output. This is harmless but confusing.
- **Fix:** Replace `appsettings.Development.json` with a Serilog-based override if lower-level logging in Development is desired:
  ```json
  {
    "Serilog": {
      "MinimumLevel": {
        "Default": "Debug",
        "Override": {
          "Microsoft.AspNetCore": "Information"
        }
      }
    }
  }
  ```

---

**[SEVERITY: Low] install.ps1 — No DisplayName set via sc.exe; separate description call cannot re-use variable safely**

- **File:** `install.ps1`, lines 53–58
- **Issue:** `sc.exe create` does not include `DisplayName=` parameter; the display name will default to `$ServiceName` (`FalconAuditService`). `$DisplayName = 'Falcon Audit Log Service'` is assigned at line 18 but never passed to `sc.exe`. This is cosmetic (Services MMC will show the binary name), but the declared intent is to use the friendly display name.
- **Fix:** Add `DisplayName= "$DisplayName"` to the `sc.exe create` call.

---

## Clean Areas

- No hardcoded credentials, passwords, API keys, or connection strings found in any config file.
- Serilog minimum level is `Information` in production — correct.
- `retainedFileCountLimit: 31` is set — log rotation is bounded.
- Windows Event Log sink is restricted to `Warning` and above — avoids flooding the Application event log.
- `UseWindowsService()` with explicit `ServiceName = "FalconAuditService"` is correctly set in `Program.cs:27`.
- `builder.ContentRootPath = AppContext.BaseDirectory` ensures the service reads its own `appsettings.json` even when launched from `System32` by SCM.
- `AppContext.BaseDirectory` is also used for the bootstrap logger config builder (Program.cs:14) — correct SCM-launch handling.
- `sc.exe failure` sets three automatic restart actions with increasing delays — good service resilience.
- `#Requires -RunAsAdministrator` is present in `install.ps1` — prevents silent partial install.
- Negotiate authentication with a `FallbackPolicy` of `RequireAuthenticatedUser` means all endpoints require authentication by default — correct.
- `appsettings.Development.json` does not disable authentication, expand CORS, or expose sensitive data — no security downgrade in the Development override.
- No secrets, tokens, or passwords are present in `launchSettings.json`.
