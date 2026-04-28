# Appendix C — Web Server: API, Queries & Flows

> **Part of:** `jobMonitorManagmentDesign.md` stand-alone package  
> **Scope:** Read-only HTTP query layer over the per-job SQLite shards. The REST API is hosted **inside the same Windows Service executable** as the audit worker (`FalconAuditService.exe`). There is no separate web server process.

---

## C.1 — System Overview with Web Server

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  FALCON MACHINE                                                               │
│                                                                               │
│  ┌───────────────────────────────────────────────────────────────────────┐   │
│  │   FalconAuditService.exe  (single Windows Service, port 5100)         │   │
│  │   Project: FalconAuditWebServer.csproj  (Microsoft.NET.Sdk.Web)       │   │
│  │                                                                        │   │
│  │   ── Audit Worker (BackgroundService) ─────────────────────────────   │   │
│  │   │  FileSystemWatcher → FileChangeHandler → ShardRegistry            │   │
│  │   │  CatchUpScanner   ManifestManager   DirectoryWatcher              │   │
│  │   │           │ writes                                                 │   │
│  │   │           ▼                                                        │   │
│  │   │  ── Kestrel REST API (port 5100) ───────────────────────────────  │   │
│  │   │  │  JobDiscoveryService  (polls c:\job\ every 30 s)               │   │
│  │   │  │  QueryRepository     (read-only WAL connections)               │   │
│  │   │  │  JobsEndpoints  EventsEndpoints  FileHistoryEndpoints          │   │
│  │   │  │           │ reads (Mode=ReadOnly, WAL)                         │   │
│  │   │  ▼           ▼                                                     │   │
│  └───────────────────────────────────────────────────────────────────────┘   │
│             │                                                                  │
│             ▼                                                                  │
│  ┌──────────────────────────────────────────────────────────────────────┐    │
│  │  c:\job\                                                              │    │
│  │    Diced_10.0.4511\                                                   │    │
│  │      .audit\audit.db   ◄── shard (WAL mode, concurrent read+write)  │    │
│  │      .audit\manifest.json                                             │    │
│  │    AnotherJob\                                                        │    │
│  │      .audit\audit.db                                                  │    │
│  │  c:\bis\auditlog\                                                     │    │
│  │    global.db                                                          │    │
│  └──────────────────────────────────────────────────────────────────────┘    │
│                                                                               │
└───────────────────────────────────────┬──────────────────────────────────────┘
                                        │ HTTP / LAN (port 5100)
                              ┌─────────▼──────────┐
                              │  Browser / Client   │
                              │  (Engineer laptop,  │
                              │   QA dashboard,     │
                              │   support tool)     │
                              └─────────────────────┘
```

**Key constraint:** `QueryRepository` opens all SQLite shards in **read-only WAL mode** (`Mode=ReadOnly; PRAGMA journal_mode=WAL; PRAGMA busy_timeout=3000`). It never writes. The audit worker writes concurrently in the same process — SQLite WAL allows unlimited concurrent readers alongside a single writer.

---

## C.2 — Component Diagram

```
FalconAuditService.exe  (merged single exe)
─────────────────────────────────────────────────────────────────
 Program.cs
 └─ WebApplication.CreateBuilder()
    ├─ UseWindowsService("FalconAuditService")    ← SCM registration
    ├─ UseSerilog()
    │
    ├─ ── Audit worker DI ────────────────────────────────────────
    │  ├─ AddSingleton<SqliteRepository>          ← global.db writer
    │  ├─ AddSingleton<MonitorConfig>             ← loaded from SQL + appsettings
    │  ├─ AddSingleton<ContentCache>
    │  ├─ AddSingleton<ShardRegistry>             ← per-job shard factory
    │  ├─ AddSingleton<ManifestManager>           ← manifest read/write
    │  ├─ AddSingleton<FileClassifier>            ← 69 glob rules
    │  ├─ AddSingleton<ChangeDescriptionEnricher> ← INI key → human label
    │  ├─ AddSingleton<DirectoryWatcher>          ← job folder arrive/depart
    │  ├─ AddSingleton<FileChangeHandler>
    │  ├─ AddSingleton<CatchUpScanner>
    │  ├─ AddSingleton<FileMonitorService>
    │  └─ AddHostedService<Worker>                ← BackgroundService
    │
    ├─ ── Web server DI ─────────────────────────────────────────
    │  ├─ AddSingleton<JobDiscoveryService>       ← polls c:\job\ every 30 s
    │  ├─ AddSingleton<QueryRepository>           ← read-only SQLite connections
    │  ├─ AddAuthentication(Negotiate)            ← Windows Auth (Kerberos/NTLM)
    │  └─ AddAuthorization(FallbackPolicy=Authenticated, "AuditorOnly" role)
    │
    └─ MapGroup("/api")
       ├─ JobsEndpoints.cs
       │   GET /api/jobs                                 ← list jobs with stats
       │   GET /api/jobs/{jobName}/manifest              ← raw manifest.json
       │
       ├─ EventsEndpoints.cs
       │   GET /api/jobs/{jobName}/events                ← paginated, filterable
       │   GET /api/jobs/{jobName}/events/{id}           ← full detail [AuditorOnly]
       │   GET /api/global/events                        ← events from global.db
       │
       └─ FileHistoryEndpoints.cs
           GET /api/jobs/{jobName}/history/{*filePath}   ← all versions of one file


QueryRepository
─────────────────────────────────────────────────────────────────
  ConcurrentDictionary<string, SqliteConnection> _connections
  │
  ├─ GetConnection(dbPath)
  │   → _connections.GetOrAdd(path, OpenReadOnly)
  │     PRAGMA journal_mode=WAL; PRAGMA busy_timeout=3000
  │
  ├─ ListJobs()                     → SELECT COUNT/MIN/MAX + GROUP_CONCAT per shard
  ├─ GetEvents(jobName, filter)      → parameterised SELECT with WHERE clause builder
  ├─ GetEventsFromDb(dbPath, filter) → same, accepts raw path (used for global.db)
  ├─ GetEvent(jobName, id)          → SELECT * WHERE id=@id (includes old_content, diff_text)
  └─ GetFileHistory(jobName, relPath) → SELECT * WHERE rel_filepath=@p ORDER BY changed_at ASC


JobDiscoveryService
─────────────────────────────────────────────────────────────────
  ├─ Refresh()            → Directory.EnumerateDirectories(watchPath)
  │                          filter: .audit\audit.db exists
  │                          _knownJobs = volatile IReadOnlyList<string>
  └─ Timer                → Refresh() every 30 s
```

---

## C.3 — API Endpoint Map

```
BASE URL: http://falcon-machine:5100/api

┌──────────────────────────────────────────────────────────┬────────────────────────────────────────────────┐
│ Endpoint                                                 │ Description                                    │
├──────────────────────────────────────────────────────────┼────────────────────────────────────────────────┤
│ GET /api/jobs                                            │ List all jobs with shard stats                 │
│ GET /api/jobs/{jobName}/manifest                         │ Chain-of-custody manifest (raw JSON)           │
│ GET /api/jobs/{jobName}/events                           │ Paginated events (see query params below)      │
│ GET /api/jobs/{jobName}/events/{id}                      │ Full event detail (content+diff) — AuditorOnly │
│ GET /api/jobs/{jobName}/history/{*filePath}              │ All versions of one specific file              │
│ GET /api/global/events                                   │ Events from global.db (status.ini etc.)        │
└──────────────────────────────────────────────────────────┴────────────────────────────────────────────────┘

Query parameters for /events:
  ?module=Recipe                 filter by module (Recipe|Job|Config|AlignmentData|DieMap|Log|ScanResult)
  ?priority=P1                   filter by monitor priority (P1|P2|P3)
  ?service=RMS                   filter by ownerService
  ?eventType=Modified            filter by event_type (Created|Modified|Deleted|Renamed)
  ?from=2026-04-01T00:00:00Z     changed_at >= from (ISO 8601)
  ?to=2026-04-23T23:59:59Z       changed_at <= to
  ?machine=FALCON-01             filter by machine_name
  ?path=Recipe.ini               substring match on filepath
  ?page=1                        page number (1-based, default 1)
  ?pageSize=50                   rows per page (default 50, max 500)
  ?sort=desc                     changed_at sort direction (asc|desc, default desc)
```

---

## C.4 — Query Flow (HTTP Request → SQLite → JSON Response)

```
Client                 EventsEndpoints        QueryRepository         SQLite Shard
  │                         │                       │                      │
  │  GET /api/jobs/          │                       │                      │
  │  Diced_10.0.4511/events  │                       │                      │
  │  ?priority=P1&page=2     │                       │                      │
  │─────────────────────────►│                       │                      │
  │                          │                       │                      │
  │                          │ Parse & validate       │                      │
  │                          │ query params           │                      │
  │                          │ Build EventFilter      │                      │
  │                          │                       │                      │
  │                          │ GetEvents(             │                      │
  │                          │   "Diced_10.0.4511",   │                      │
  │                          │   filter)              │                      │
  │                          │──────────────────────►│                      │
  │                          │                       │ GetOrAdd connection   │
  │                          │                       │──────────────────────►│
  │                          │                       │  (WAL read-only)      │
  │                          │                       │◄──────────────────────│
  │                          │                       │                       │
  │                          │                       │ SELECT id,            │
  │                          │                       │   changed_at,         │
  │                          │                       │   event_type,         │
  │                          │                       │   filepath,           │
  │                          │                       │   module,             │
  │                          │                       │   monitor_priority,   │
  │                          │                       │   owner_service,      │
  │                          │                       │   machine_name,       │
  │                          │                       │   sha256_hash         │
  │                          │                       │ FROM audit_log        │
  │                          │                       │ WHERE monitor_priority│
  │                          │                       │   = 'P1'              │
  │                          │                       │ ORDER BY changed_at   │
  │                          │                       │   DESC                │
  │                          │                       │ LIMIT 50 OFFSET 50    │
  │                          │                       │──────────────────────►│
  │                          │                       │◄──────────────────────│
  │                          │                       │  rows (no content)    │
  │                          │◄──────────────────────│                       │
  │                          │                       │                       │
  │                          │ Serialize to JSON      │                       │
  │                          │ Add pagination headers │                       │
  │◄─────────────────────────│                       │                       │
  │  200 OK                  │                       │                       │
  │  X-Total-Count: 142      │                       │                       │
  │  X-Page: 2               │                       │                       │
  │  X-PageSize: 50          │                       │                       │
  │  [ { id, changed_at, ... }, ... ]                │                       │
```

> `old_content` and `diff_text` are **not** returned in list queries — only in the single-event endpoint `GET /api/jobs/{job}/events/{id}`. This keeps list responses small.

---

## C.5 — File History Query Flow

```
Client                 FileHistoryEndpoints    QueryRepository         SQLite Shard
  │                         │                       │                      │
  │  GET /api/jobs/          │                       │                      │
  │  Diced_10.0.4511/        │                       │                      │
  │  history/                │                       │                      │
  │  S1/Recipes/R1/Recipe.ini│                       │                      │
  │─────────────────────────►│                       │                      │
  │                          │ Decode URL path        │                      │
  │                          │ → rel_filepath =       │                      │
  │                          │ "S1\Recipes\R1\        │                      │
  │                          │  Recipe.ini"           │                      │
  │                          │──────────────────────►│                      │
  │                          │                       │ SELECT * FROM         │
  │                          │                       │   audit_log           │
  │                          │                       │ WHERE rel_filepath    │
  │                          │                       │   = @p                │
  │                          │                       │ ORDER BY changed_at   │
  │                          │                       │   ASC                 │
  │                          │                       │──────────────────────►│
  │                          │                       │◄──────────────────────│
  │                          │◄──────────────────────│                       │
  │                          │                       │                       │
  │◄─────────────────────────│                       │                       │
  │  200 OK                  │                       │                       │
  │  [                       │                       │                       │
  │    { id:1,  event:"Created",  hash:"a1b2...",    │                       │
  │      machine:"FALCON-01", changed_at:"..." },    │                       │
  │    { id:7,  event:"Modified", hash:"c3d4...",    │                       │
  │      machine:"FALCON-01", diff_text:"@@ -1..." },│                       │
  │    { id:38, event:"Modified", hash:"e5f6...",    │                       │
  │      machine:"FALCON-02", diff_text:"@@ -3..." } │                       │
  │  ]                       │                       │                       │
```

---

## C.6 — Job Discovery Flow (Startup + Refresh)

```
Service startup
      │
      ▼
JobDiscoveryService constructor
      │
      ├─ Refresh()   (immediate, synchronous)
      │     Directory.EnumerateDirectories(watchPath)
      │       returns: ["Diced_10.0.4511", "AnotherJob", "OldJob_archived"]
      │
      │     For each subdirectory:
      │         does  <dir>\.audit\audit.db  exist?
      │         Yes → include in _knownJobs
      │         No  → skip (not a managed job)
      │
      └─ Start Timer (30 s interval)
             On each tick: Refresh() — re-enumerate, atomic swap of _knownJobs


QueryRepository.GetConnection(dbPath)
      │
      └─ _connections.GetOrAdd(dbPath,
             path => {
               conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly")
               conn.Open()
               PRAGMA journal_mode=WAL; PRAGMA busy_timeout=3000
               return conn
             })
         → one connection per shard, opened on first query, reused thereafter
```

---

## C.7 — Manifest Query Flow

```
Client                   JobsEndpoints          ManifestReader       File System
  │                           │                      │                    │
  │  GET /api/jobs/           │                      │                    │
  │  Diced_10.0.4511/manifest │                      │                    │
  │──────────────────────────►│                      │                    │
  │                           │ ReadManifest(         │                    │
  │                           │  "Diced_10.0.4511")   │                    │
  │                           │─────────────────────►│                    │
  │                           │                      │ Read               │
  │                           │                      │ c:\job\Diced_...\  │
  │                           │                      │ .audit\manifest.json│
  │                           │                      │───────────────────►│
  │                           │                      │◄───────────────────│
  │                           │                      │ Deserialize        │
  │                           │◄─────────────────────│                    │
  │                           │                      │                    │
  │◄──────────────────────────│                      │                    │
  │  200 OK                   │                      │                    │
  │  {                        │                      │                    │
  │    "jobName": "Diced_10.0.4511",                 │                    │
  │    "created": {           │                      │                    │
  │      "machine": "FALCON-01",                     │                    │
  │      "at": "2026-03-10T08:00:00Z"                │                    │
  │    },                     │                      │                    │
  │    "history": [           │                      │                    │
  │      { "machine": "FALCON-01",                   │                    │
  │        "from": "2026-03-10T08:00:00Z",           │                    │
  │        "to":   "2026-04-15T14:00:00Z",           │                    │
  │        "events": 1420 },  │                      │                    │
  │      { "machine": "FALCON-02",                   │                    │
  │        "from": "2026-04-15T14:05:00Z",           │                    │
  │        "to":   null,      │                      │                    │
  │        "events": 38 }     │                      │                    │
  │    ]                      │                      │                    │
  │  }                        │                      │                    │
```

---

## C.8 — Response Schemas

### GET /api/jobs

```json
[
  {
    "jobName": "Diced_10.0.4511",
    "shardPath": "c:\\job\\Diced_10.0.4511\\.audit\\audit.db",
    "totalEvents": 1458,
    "firstEvent": "2026-03-10T08:01:32Z",
    "lastEvent":  "2026-04-23T09:14:05Z",
    "machines": ["FALCON-01", "FALCON-02"],
    "shardSizeBytes": 2097152
  }
]
```

### GET /api/jobs/{jobName}/events (list item — no content)

`fileDescription` and `changeSummary` are included in list responses because they are not sensitive (unlike `oldContent`/`diffText`) and are the primary driver of user-friendly display.

```json
{
  "id": 38,
  "changedAt": "2026-04-16T07:42:11Z",
  "eventType": "Modified",
  "filepath": "c:\\job\\Diced_10.0.4511\\S1\\Recipes\\R1\\Recipe.ini",
  "relFilepath": "S1\\Recipes\\R1\\Recipe.ini",
  "module": "Recipe",
  "ownerService": "RMS",
  "monitorPriority": "P1",
  "machineName": "FALCON-02",
  "sha256Hash": "e5f6a7b8c9d0e1f2...",
  "fileDescription": "Top-level recipe control file governing auto-cycle behavior — autofocus and clean-reference frequencies, post-processing flags, and recipe identity.",
  "changeSummary": "Autofocus frequency: 1 → 3; Die-level post-processing: 0 → 1"
}
```

### GET /api/jobs/{jobName}/events/{id} (single event — includes content)

```json
{
  "id": 38,
  "changedAt": "2026-04-16T07:42:11Z",
  "eventType": "Modified",
  "filepath": "c:\\job\\Diced_10.0.4511\\S1\\Recipes\\R1\\Recipe.ini",
  "relFilepath": "S1\\Recipes\\R1\\Recipe.ini",
  "module": "Recipe",
  "ownerService": "RMS",
  "monitorPriority": "P1",
  "machineName": "FALCON-02",
  "sha256Hash": "e5f6a7b8c9d0e1f2...",
  "fileDescription": "Top-level recipe control file governing auto-cycle behavior — autofocus and clean-reference frequencies, post-processing flags, and recipe identity.",
  "changeSummary": "Autofocus frequency: 1 → 3; Die-level post-processing: 0 → 1",
  "oldContent": "[AutoCycle]\nAutoFocusEvery=1\nEnableDieLevelPostProcessing=0\n...",
  "diffText": "@@ -1,3 +1,3 @@\n [AutoCycle]\n-AutoFocusEvery=1\n+AutoFocusEvery=3\n-EnableDieLevelPostProcessing=0\n+EnableDieLevelPostProcessing=1\n"
}
```

---

## C.9 — SQLite Queries Reference

### List jobs with stats
```sql
-- Run against each shard: c:\job\{jobName}\.audit\audit.db
SELECT
    COUNT(*)                          AS total_events,
    MIN(changed_at)                   AS first_event,
    MAX(changed_at)                   AS last_event,
    GROUP_CONCAT(DISTINCT machine_name) AS machines
FROM audit_log;
```

### Paginated events with filter
```sql
SELECT
    id, changed_at, event_type, filepath, rel_filepath,
    module, owner_service, monitor_priority, machine_name, sha256_hash,
    file_description, change_summary
FROM audit_log
WHERE
    (@module   IS NULL OR module            = @module)
    AND (@priority IS NULL OR monitor_priority = @priority)
    AND (@service  IS NULL OR owner_service    = @service)
    AND (@type     IS NULL OR event_type       = @type)
    AND (@machine  IS NULL OR machine_name     = @machine)
    AND (@from     IS NULL OR changed_at      >= @from)
    AND (@to       IS NULL OR changed_at      <= @to)
    AND (@path     IS NULL OR instr(filepath, @path) > 0)
ORDER BY changed_at DESC
LIMIT @pageSize OFFSET @offset;
```

### Count for pagination header
```sql
SELECT COUNT(*) FROM audit_log
WHERE
    (@module   IS NULL OR module            = @module)
    AND (@priority IS NULL OR monitor_priority = @priority)
    AND (@service  IS NULL OR owner_service    = @service)
    AND (@type     IS NULL OR event_type       = @type)
    AND (@machine  IS NULL OR machine_name     = @machine)
    AND (@from     IS NULL OR changed_at      >= @from)
    AND (@to       IS NULL OR changed_at      <= @to)
    AND (@path     IS NULL OR instr(filepath, @path) > 0);
```

### Full file history (single file, oldest-first)
```sql
SELECT
    id, changed_at, event_type, machine_name,
    sha256_hash, old_content, diff_text
FROM audit_log
WHERE rel_filepath = @relFilepath
ORDER BY changed_at ASC;
```

### Distinct files ever changed in a job
```sql
SELECT DISTINCT rel_filepath, module, owner_service, monitor_priority
FROM audit_log
ORDER BY rel_filepath;
```

---

## C.10 — Thread Model & Connection Safety

```
HTTP Thread Pool (ASP.NET Core Kestrel)
─────────────────────────────────────────────────────────────────────
  Request 1 ──► QueryRepository.GetEvents("Diced")
  Request 2 ──► QueryRepository.GetEvents("Diced")    ← concurrent OK
  Request 3 ──► QueryRepository.GetFileHistory("Diced", ...)

QueryRepository
  _connections["Diced"] = SqliteConnection (ReadOnly, WAL)
       │
       │  SQLite WAL mode: unlimited concurrent readers
       │  ReadOnly connection: cannot issue writes → no locking conflict
       │  with FalconAuditService writer
       ▼
  c:\job\Diced_10.0.4511\.audit\audit.db   ← WAL reader sees consistent snapshot
                                              of all committed rows


 Rule: one SqliteConnection per shard, opened once, reused for all reads.
 SqliteConnection in Microsoft.Data.Sqlite is NOT thread-safe — each
 endpoint handler acquires a per-query SqliteCommand from the shared
 connection inside a lock, or uses connection pooling (one conn per thread).

 Recommended pattern: SqliteConnectionPool per shard
   → _pools[jobName] = new DbConnectionPool(shardPath, Mode=ReadOnly, size=4)
```

---

## C.11 — Project Structure (Merged Single Exe)

```
FalconAuditWebServer\                    ← project root (assembles FalconAuditService.exe)
├─ FalconAuditWebServer.csproj
│     Sdk="Microsoft.NET.Sdk.Web"
│     TargetFramework="net8.0-windows"
│     AssemblyName="FalconAuditService"
│     <PackageReference Include="Microsoft.Data.Sqlite"                  8.0.*/>
│     <PackageReference Include="Microsoft.AspNetCore.Authentication.Negotiate" 8.0.*/>
│     <PackageReference Include="Microsoft.Extensions.Hosting.WindowsServices"  8.0.*/>
│     <PackageReference Include="DiffPlex"                               1.7.*/>
│     <PackageReference Include="Serilog.AspNetCore"                     8.0.*/>
│     <PackageReference Include="Serilog.Settings.Configuration"         8.0.*/>
│     <PackageReference Include="Serilog.Sinks.File"                     5.0.*/>
│     <PackageReference Include="Serilog.Sinks.EventLog"                 3.1.*/>
│
├─ Program.cs                         ← merged startup: audit DI + web DI + UseWindowsService
├─ appsettings.json                   ← AuditService paths + Kestrel port 5100 + Serilog
│
├─ ── Audit worker source (namespace FalconAuditService) ───────────────────
├─ Worker.cs                          ← BackgroundService
├─ FileMonitorService.cs
├─ FileChangeHandler.cs
├─ CatchUpScanner.cs
├─ DirectoryWatcher.cs
├─ ShardRegistry.cs
├─ ManifestManager.cs
├─ FileClassifier.cs
├─ ChangeDescriptionEnricher.cs
├─ ContentCache.cs
├─ HashHelper.cs
├─ DiffHelper.cs
├─ SqliteRepository.cs
├─ ChangeEvent.cs
│
├─ Models\                            ← shared models (FalconAuditService.Models)
│   ├─ AuditLogEntry.cs
│   ├─ FileBaseline.cs
│   ├─ MonitorConfig.cs
│   ├─ JobManifest.cs
│   │
│   └─ ── Web-only response models (FalconAuditWebServer.Models) ──────────
│       ├─ JobSummary.cs
│       ├─ AuditEventSummary.cs       ← list item (no content fields)
│       ├─ AuditEventDetail.cs        ← single item (content+diff, AuditorOnly)
│       ├─ FileHistoryItem.cs
│       └─ EventFilter.cs             ← parsed query params
│
├─ Services\                          ← namespace FalconAuditWebServer.Services
│   ├─ JobDiscoveryService.cs         ← polls c:\job\*\.audit\audit.db every 30 s
│   └─ QueryRepository.cs            ← read-only SQLite access layer
│
└─ Endpoints\                         ← namespace FalconAuditWebServer.Endpoints
    ├─ JobsEndpoints.cs               ← /api/jobs + /api/jobs/{j}/manifest
    ├─ EventsEndpoints.cs             ← /api/jobs/{j}/events[/{id}], /global/events
    └─ FileHistoryEndpoints.cs        ← /api/jobs/{j}/history/{*path}
```

---

## C.12 — Hosting

```
Current implementation: merged single Windows Service
──────────────────────────────────────────────────────────────────────────
  FalconAuditService.exe  (one exe, one SCM registration)
    ├─ Worker (BackgroundService) — file watcher + event writer
    └─ Kestrel — REST API on port 5100

  Service registration:
    sc create FalconAuditService binPath="C:\bis\bin\FalconAuditService.exe" start=auto
    sc description FalconAuditService "Falcon Audit Log Service with REST API"
    sc failure FalconAuditService reset=86400 actions=restart/5000/restart/10000/restart/30000

  Pros:  single install; ShardRegistry shared in-process; no inter-process
         file locking; Kestrel thread pool is separate from audit worker
         thread pool — web requests do not delay audit writes
  Cons:  a hard crash takes both writer and API offline simultaneously


Alternative: reverse proxy (multi-machine dashboard)
──────────────────────────────────────────────────────────────────────────
  Each Falcon machine runs FalconAuditService.exe on port 5100.
  Central dashboard machine runs nginx/IIS reverse proxy:
    /falcon-01/api/* → http://FALCON-01:5100/api/*
    /falcon-02/api/* → http://FALCON-02:5100/api/*

  Client queries all machines from one UI without any code changes to the service.
```

---

## C.13 — Security & Access

```
┌──────────────────────┬────────────────────────────────────────────────────┐
│ Concern              │ Recommendation                                     │
├──────────────────────┼────────────────────────────────────────────────────┤
│ Network binding      │ Bind to 127.0.0.1 (loopback) by default;           │
│                      │ expose on LAN only when explicitly configured       │
├──────────────────────┼────────────────────────────────────────────────────┤
│ Authentication       │ Windows Authentication implemented in Program.cs    │
│                      │ (see snippet below); single-event endpoint          │
│                      │ restricted to Auditor role (protects old_content)   │
├──────────────────────┼────────────────────────────────────────────────────┤
│ Read-only guarantee  │ SQLite Mode=ReadOnly; no INSERT/UPDATE/DELETE       │
│                      │ routes exposed                                      │
├──────────────────────┼────────────────────────────────────────────────────┤
│ old_content exposure │ P1 file content may contain recipe IP — single-    │
│                      │ event endpoint requires [Authorize(Roles="Auditor")]│
├──────────────────────┼────────────────────────────────────────────────────┤
│ Path traversal       │ rel_filepath decoded from URL, then validated:      │
│                      │ Path.GetFullPath(rel).StartsWith(jobRoot)           │
│                      │ before use in query (regex ^[\w\-. \\\/]+$ is      │
│                      │ insufficient — it permits "..\..\" sequences)      │
└──────────────────────┴────────────────────────────────────────────────────┘
```

**Authentication middleware (Program.cs snippet):**

```csharp
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;

builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme)
    .AddNegotiate();
builder.Services.AddAuthorization(o =>
{
    o.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    o.AddPolicy("AuditorOnly", p => p.RequireRole("Auditor"));
});

// ... after app.Build():
app.UseAuthentication();
app.UseAuthorization();
```

Apply `[Authorize(Policy = "AuditorOnly")]` to the single-event endpoint handler that returns `old_content`.

---

## C.14 — Web Server Model Classes

Full model source is in [Appendix B §B.27](appendix_B_code.md#b27--web-api-models-new). Key points:

- `AuditEventSummary` — list view; omits `old_content` and `diff_text` to keep list responses small. Includes `fileDescription`, `changeSummary`, and `isBackfill`.
- `AuditEventDetail` — single-event detail; adds `oldContent`, `diffText`, `oldFilepath`. Returned only by `GET /api/jobs/{job}/events/{id}` which requires `AuditorOnly` role.
- `FileHistoryItem` — returned by file history endpoint; includes `oldContent` and `diffText` for each entry in a single file's timeline.
- `JobSummary` — returned by `GET /api/jobs`; includes `totalEvents`, `firstEvent`, `lastEvent`, `machines`, `shardSizeBytes`.
- `EventFilter` — query-string parameters bound by Minimal API; all fields nullable except `Page`, `PageSize`, `Sort`.

### Column-to-field mapping reference

| SQL column | AuditEventSummary | AuditEventDetail | FileHistoryItem |
|---|---|---|---|
| id | Id | Id | Id |
| changed_at | ChangedAt | ChangedAt | ChangedAt |
| event_type | EventType | EventType | EventType |
| filepath | Filepath | Filepath | — |
| rel_filepath | RelFilepath | RelFilepath | — |
| module | Module | Module | — |
| owner_service | OwnerService | OwnerService | — |
| monitor_priority | MonitorPriority | MonitorPriority | — |
| machine_name | MachineName | MachineName | MachineName |
| sha256_hash | Sha256Hash | Sha256Hash | Sha256Hash |
| file_description | FileDescription | FileDescription | — |
| change_summary | ChangeSummary | ChangeSummary | — |
| old_content | — | OldContent | OldContent |
| diff_text | — | DiffText | DiffText |
| old_filepath | — | OldFilepath | — |
| is_backfill | IsBackfill | IsBackfill | IsBackfill |
