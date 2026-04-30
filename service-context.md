---
service_name: FalconAuditService
primary_language: csharp
runtime: dotnet
api_framework: aspnetcore-minimal-apis
deployment: windows-service
storage_technology: SQLite
api_binding: http://localhost:5100
api_auth: windows-negotiate
requirement_id_prefixes: [REQ, AUD]
---

# Service Context — FalconAuditService

## Overview

FalconAuditService is a Windows Service that monitors `c:\job\` for file changes, writes audit events to per-job SQLite shards (Option C: job-embedded shard + custody manifest), and exposes a read-only REST API on port 5100 via Kestrel (hosted inside the same process).

## Technology Stack

- **Language**: C# (.NET 8)
- **Runtime**: .NET 8 Windows Service (`UseWindowsService`)
- **Web framework**: ASP.NET Core Minimal APIs (Kestrel, port 5100)
- **Storage**: SQLite WAL mode — per-job shard at `<jobFolder>\.audit\audit.db`, global shard at `c:\bis\auditlog\global.db`
- **Logging**: Serilog (structured, file sink)
- **Auth**: Windows Authentication (Negotiate / Kerberos / NTLM); role `AuditorOnly` for sensitive endpoints

## Components

| Component | Type | Responsibility |
|---|---|---|
| `Worker` | BackgroundService | Startup orchestration; enumerates jobs; wires DirectoryWatcher; calls ManifestManager.RecordArrival |
| `FileMonitorService` | Singleton | FileSystemWatcher on `c:\job\`; 500 ms debounce |
| `FileChangeHandler` | Singleton | Extracts jobName from path; routes writes to ShardRegistry |
| `FileClassifier` | Singleton | Hot-reloadable glob-rule classifier; loads `FileClassificationRules.json`; lock-free ImmutableList swap |
| `ShardRegistry` | Singleton | GetOrCreate(jobName, jobPath) — caches per-job SqliteRepository instances |
| `SqliteRepository` | Per-shard | WAL-mode SQLite writer; SemaphoreSlim(1) write guard; parameterised inserts |
| `DirectoryWatcher` | Singleton | Watches `c:\job\` depth=1; fires on job folder arrive/depart; calls ManifestManager |
| `ManifestManager` | Singleton | Reads/writes `.audit\manifest.json`; atomic write via temp-file rename (NTFS) |
| `CatchUpScanner` | Singleton | Scoped per-job catch-up scan; optional `string? jobPath` (null = full scan) |
| `ContentCache` | Singleton | Caches P1 file content for diff generation |
| `ChangeDescriptionEnricher` | Singleton | Maps INI keys to human-readable labels |
| `HashHelper` | Static utility | SHA-256 file hashing |
| `DiffHelper` | Static utility | Unified diff generation |
| `JobDiscoveryService` | Singleton | Polls `c:\job\` every 30 s; updates job list for API layer |
| `QueryRepository` | Singleton | Read-only WAL SQLite connections per shard; never writes |
| `JobsEndpoints` | Minimal API | GET /api/jobs, GET /api/jobs/{jobName}/manifest |
| `EventsEndpoints` | Minimal API | GET /api/jobs/{jobName}/events, GET /api/jobs/{jobName}/events/{id}, GET /api/global/events |
| `FileHistoryEndpoints` | Minimal API | GET /api/jobs/{jobName}/history/{*filePath} |
| `MonitorConfig` | Singleton | Loaded from appsettings.json; exposes all config keys |
| `AuditLogEntry` | Model | Represents one audit event row |
| `FileBaseline` | Model | Represents current hash/mtime of a monitored file |

## Storage Schema (SQLite)

```sql
-- Per-job shard: <jobFolder>\.audit\audit.db
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;

CREATE TABLE IF NOT EXISTS audit_log (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp     TEXT    NOT NULL,
    filepath      TEXT    NOT NULL,
    event_type    TEXT    NOT NULL,
    module        TEXT,
    owner_service TEXT,
    priority      TEXT,
    machine_name  TEXT,
    file_hash     TEXT,
    old_content   TEXT,
    new_content   TEXT,
    diff_text     TEXT
);
CREATE INDEX IF NOT EXISTS idx_audit_log_filepath  ON audit_log(filepath);
CREATE INDEX IF NOT EXISTS idx_audit_log_timestamp ON audit_log(timestamp);

CREATE TABLE IF NOT EXISTS file_baseline (
    filepath      TEXT PRIMARY KEY,
    last_hash     TEXT,
    last_modified TEXT
);

-- Global shard: c:\bis\auditlog\global.db
-- Same DDL as above; stores events for c:\job\status.ini and other global-scope files
```

## API Endpoints

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | /api/jobs | Authenticated | List all jobs with stats (event count, last event time) |
| GET | /api/jobs/{jobName}/manifest | Authenticated | Raw manifest.json content |
| GET | /api/jobs/{jobName}/events | Authenticated | Paginated, filterable audit events |
| GET | /api/jobs/{jobName}/events/{id} | AuditorOnly | Full event detail including content/diff |
| GET | /api/global/events | Authenticated | Events from global.db, paginated |
| GET | /api/jobs/{jobName}/history/{*filePath} | Authenticated | All audit versions of one file |

### Pagination
Query params: `page` (default 1), `pageSize` (default 50, max 200).
Response envelope: `{ "data": [...], "page": 1, "pageSize": 50, "total": 1420 }`

### Filters (on /events endpoints)
`module`, `priority`, `ownerService`, `from` (ISO 8601), `to` (ISO 8601), `filePath` (substring match)

## Required Config Keys

| Key | Default | Description |
|---|---|---|
| `JobRootPath` | `c:\job\` | Root folder to monitor for jobs |
| `GlobalDbPath` | `c:\bis\auditlog\global.db` | Path to global SQLite shard |
| `ClassificationRulesPath` | `c:\bis\auditlog\FileClassificationRules.json` | Configurable file rules |
| `Port` | `5100` | Kestrel HTTP port |
| `MachineName` | `%COMPUTERNAME%` | Machine identifier written to audit rows |
| `DebouncePeriodMs` | `500` | FSW debounce in milliseconds |
| `JobDiscoveryPollSeconds` | `30` | How often JobDiscoveryService rescans c:\job\ |
| `Serilog:WriteTo:0:Args:path` | `c:\bis\auditlog\logs\service.log` | Serilog file sink path |

## Threat Model

- Internal LAN network; no exposure to public internet
- Windows Authentication (Negotiate) — all API requests require domain credentials
- `AuditorOnly` role required for full event content (diff/snapshot data may be sensitive)
- SQLite files on local NTFS; service account needs read/write to `c:\job\` and `c:\bis\auditlog\`
- No user-supplied content written back to filesystem
- SQLite parameterised queries throughout — no SQL injection surface

## Performance Targets

- File change → audit event written: < 1 second (after 500 ms debounce)
- API response (paginated list): < 200 ms for up to 10,000 rows
- CatchUpScanner (new job arrival, ~500 files): < 5 seconds
- SQLite WAL: concurrent API reads while worker writes — no blocking

## sensitive_fields

`old_content`, `new_content`, `diff_text` — only returned by `GET /api/jobs/{jobName}/events/{id}` (AuditorOnly role)
