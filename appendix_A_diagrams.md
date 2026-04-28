# Appendix A — Block Diagrams, Design & Event Flows

> **Belongs to:** `jobMonitorManagmentDesign.md`  
> **Design option implemented:** Option C — Job-Embedded Shard with Custody Manifest

---

## A.1 — Overall System Architecture

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  FALCON MACHINE  (Windows 10 LTSC)                                            │
│                                                                               │
│  ┌──────────────┐  writes  ┌────────────────────────────────────────────┐    │
│  │   AOI_Main   │─────────>│  c:\job\                                   │    │
│  │   RMS        │          │    Diced_10.0.4511\                        │    │
│  │   Falcon.Net │          │      .audit\                               │    │
│  │   DataServer │          │        audit.db  ◄──────────────────────┐ │    │
│  └──────────────┘          │        manifest.json                    │ │    │
│                            │      S1\Recipes\R1\Recipe.ini           │ │    │
│  ┌─────────────────────────┴───────────────────────────────────────┬─┴─┘    │
│  │  FalconAuditService.exe  (single exe — Windows Service +        │        │
│  │                           Kestrel REST API :5100)               │        │
│  │                                                                 │        │
│  │  ┌── FILE MONITORING ──────────────────────────────────────┐   │        │
│  │  │ FileMonitorService   DirectoryWatcher   FileChangeHandler│   │        │
│  │  │ FSW c:\job\**        c:\job\ depth-1    hash·diff·write──┼───┘        │
│  │  │ debounce 500 ms      job arrive/leave   ShardRegistry    │            │
│  │  │ Channel<ChangeEvent> → N consumer tasks routing          │            │
│  │  └─────────────────────────────────────────────────────────┘            │
│  │                                                                          │
│  │  ┌── CLASSIFICATION ──────────┐  ┌── STORAGE ─────────────────────────┐ │
│  │  │ FileClassifier             │  │ ShardRegistry · SqliteRepository    │ │
│  │  │ 69 glob rules, hot-reload  │  │ ManifestManager                    │ │
│  │  │ P1 / P2 / P3 / P4          │  │ WAL-mode SQLite, SemaphoreSlim(1)  │ │
│  │  └────────────────────────────┘  └────────────────────────────────────┘ │
│  │                                                                          │
│  │  ┌── RECONCILIATION ──────────┐  ┌── ORCHESTRATION ───────────────────┐ │
│  │  │ CatchUpScanner             │  │ Worker (BackgroundService)          │ │
│  │  │ parallel per-job on start  │  │ wires all components at startup     │ │
│  │  │ + after FSW overflow       │  └────────────────────────────────────┘ │
│  │  └────────────────────────────┘                                         │
│  │                                                                          │
│  │  ┌── REST API  http://0.0.0.0:5100 ──────────────────────────────────┐  │
│  │  │  Windows Authentication (Negotiate)  ·  role: Auditor             │  │
│  │  │                                                                    │  │
│  │  │  JobDiscoveryService              QueryRepository                  │  │
│  │  │  scan .audit\audit.db             read-only WAL connections        │  │
│  │  │  30 s refresh timer               ConcurrentDictionary<path,conn>  │  │
│  │  │                                                                    │  │
│  │  │  GET /api/jobs[/{job}/manifest]                                    │  │
│  │  │  GET /api/events/{job}[/{id}]   GET /api/filehistory/{job}         │  │
│  │  └────────────────────────────────────────────────────────────────────┘  │
│  └──────────────────────────────────────────────────────────────────────────┘
│                                                                               │
│  c:\bis\auditlog\                                                             │
│    global.db · FileClassificationRules.json · logs\falconaudit-*.log         │
│                                                                               │
│  Browser / API Consumer ──── HTTP :5100 ─────────────────────────────────►   │
└──────────────────────────────────────────────────────────────────────────────┘
```

---

## A.2 — Per-Job Folder Layout (On Disk)

```
c:\job\
│
├── status.ini                         ← P1 global file → global.db
│
├── Diced_10.0.4511\                   ← Job folder (watched by DirectoryWatcher)
│   │
│   ├── .audit\                        ← Audit folder (hidden by convention)
│   │   ├── audit.db                   ← All audit events for this job (portable)
│   │   └── manifest.json             ← Chain-of-custody (human-readable)
│   │
│   ├── Metadata.ini                   ← P1 job file
│   ├── S1\
│   │   ├── Metadata.ini              ← P1
│   │   ├── MultiRecipe.ini           ← P1
│   │   ├── DefectsClustering.ini     ← P1
│   │   ├── ProductionInfo.ini        ← P1
│   │   ├── ScanCondition.ini         ← P1
│   │   ├── Wafer2Table.ini           ← P1
│   │   ├── DefaultWafer2Table.ini    ← P2
│   │   ├── DieAlignment.dat_block.ini← P2
│   │   └── Recipes\
│   │       ├── R1\
│   │       │   ├── Recipe.ini        ← P1 (full diff stored)
│   │       │   ├── ProductInfo.ini   ← P1
│   │       │   ├── Waferinfo.ini     ← P1
│   │       │   ├── Wafer2Table.ini   ← P1
│   │       │   ├── Alignment.ini     ← P1
│   │       │   ├── AlignRtp.ini      ← P1
│   │       │   ├── GlobalRTP.ini     ← P1
│   │       │   ├── RTP.txt           ← P1
│   │       │   ├── OpticPreset.ini   ← P1
│   │       │   ├── JobIllumLimits.ini← P1
│   │       │   ├── ZoomLevels.ini    ← P1
│   │       │   ├── zones.ini         ← P1
│   │       │   ├── Zones\
│   │       │   │   ├── PostProcess.ini ← P1
│   │       │   │   └── Scan Area.ini   ← P1
│   │       │   ├── WaferAlignData\
│   │       │   │   └── AlignmentData.ini ← P1
│   │       │   ├── AlignmentData.ini ← P2 (hash only)
│   │       │   ├── DieMapping.dat    ← P2 (hash only)
│   │       │   ├── FocusMapping\     ← P2
│   │       │   ├── TrainData\        ← P2
│   │       │   └── .dc_cache\        ← P2
│   │       └── R2\  (same structure + CcsSetup.xml)
│
└── AnotherJob\                        ← Second job (separate shard)
    ├── .audit\
    │   ├── audit.db
    │   └── manifest.json
    └── ...

c:\bis\auditlog\
├── global.db                          ← status.ini and other global-scope events
└── FileClassificationRules.json       ← Configurable file list (hot-reload)
```

---

## A.3 — Service Startup Sequence

```
Windows SCM
  │
  │  StartService
  ▼
Worker.ExecuteAsync()
  │
  ├─ 1. Validate WatchPath (c:\job\ exists)
  │
  ├─ 2. Enumerate c:\job\* directories (DirectoryWatcher.EnumerateExisting)
  │     │
  │     └── for each job folder found:
  │           ├── ShardRegistry.GetOrCreate(jobName, jobPath)
  │           │     └── ensures .audit\audit.db exists (WAL mode, schema created)
  │           │
  │           └── ManifestManager.RecordArrival(jobPath, machineName)
  │                 ├── if manifest exists from different machine: close that entry
  │                 └── append new history entry { machine=this, from=now }
  │
  ├─ 3. Load FileClassificationRules.json → FileClassifier rules compiled
  │
  ├─ 4. CatchUpScanner.RunAsync() [per job, serialised]
  │     │
  │     ├── Phase 1: hash every file under jobPath
  │     │     ├── hash ≠ baseline  → INSERT audit_log (Modified, note="catch-up")
  │     │     ├── no baseline      → INSERT audit_log (Created, note="catch-up")
  │     │     └── hash == baseline → populate ContentCache (P1 only)
  │     │
  │     └── Phase 2: baseline entries not on disk
  │           └── INSERT audit_log (Deleted, note="catch-up") + DeleteBaseline
  │
  ├─ 5. FileMonitorService.Start()   ← FSW enabled AFTER catch-up
  │     └── DirectoryWatcher.Start() ← job folder arrive/remove events
  │
  └─ Service running  ──────────────────────────────────────────────────────►
```

---

## A.4 — Live File Change Event Flow

```
  RMS / AOI_Main            OS / NTFS              FalconAuditService
       │                        │                          │
       │  1. Open Recipe.ini    │                          │
       │──────────────────────►│                          │
       │  2. Write new content  │                          │
       │──────────────────────►│                          │
       │                        │  3. FSW Changed event   │
       │                        │ ──────────────────────►│  OnFileEvent()
       │  4. Flush + close      │                          │  → start/reset 500ms
       │──────────────────────►│                          │    debounce timer
       │                        │  5. FSW Changed event   │
       │                        │ ──────────────────────►│  timer resets
       │                        │                          │
       │                        │   [500 ms quiet window] │
       │                        │                          │
       │                        │                          │  6. Debounce fires
       │                        │                          │  ChangeEvent → BlockingCollection
       │                        │                          │
       │                        │                          │  7. Writer thread dequeues
       │                        │                          │     ExtractJobName("Diced_10.0.4511")
       │                        │                          │     repo = ShardRegistry.GetOrCreate(jobName)
       │                        │                          │
       │                        │                          │  8. FileClassifier.Classify()
       │                        │                          │     = { Recipe, RMS, P1 }
       │                        │                          │
       │                        │                          │  9. HashHelper.ComputeSha256()
       │                        │                          │     newHash = "a3f2e91b..."
       │                        │                          │
       │                        │                          │  10. repo.GetBaselineAsync()
       │                        │                          │      oldHash = "7e91bc4a..."
       │                        │                          │
       │                        │                          │  11. ReadContent() → newContent
       │                        │                          │      oldContent from ContentCache
       │                        │                          │
       │                        │                          │  12. DiffHelper.UnifiedDiff()
       │                        │                          │      → diff_text
       │                        │                          │
       │                        │                          │  13. repo.InsertAuditLogAsync()
       │                        │                          │      → row in job shard audit.db
       │                        │                          │
       │                        │                          │  14. repo.UpsertBaselineAsync()
       │                        │                          │      oldHash ← newHash
       │                        │                          │
       ▼                        ▼                          ▼
  [recipe saved]           [file stable]          [audit.db row in
                                                   Diced_10.0.4511\.audit\]
```

**Total latency from file-close to DB write:** ~520–810 ms (500 ms debounce + <10 ms processing).

---

## A.5 — Job Portability Flow (Option C — Cut & Paste)

```
Machine A  (FALCON-01)                    Machine B  (FALCON-02)
─────────────────────────────────────     ─────────────────────────────────────
Service running.
c:\job\Diced_10.0.4511\
  .audit\
    audit.db  ← 1,420 rows
    manifest.json:
      history[0]: FALCON-01
        from: 2026-03-10T08:00Z
        to:   null  (active)
        events: 1,420

Worker.StopAsync():
  ManifestManager.RecordDeparture()
    → history[0].to   = 2026-04-15T14:00Z
    → history[0].events = 1,420
  ShardRegistry.Remove("Diced_10.0.4511")
  FileMonitorService.Stop()

operator cuts folder
    ─────────────────────────────────────────────────────────────────►
                                          c:\job\Diced_10.0.4511\
                                            .audit\
                                              audit.db  ← 1,420 rows
                                              manifest.json:
                                                history[0]: FALCON-01 (closed)

                                          Service starts (or folder paste detected)

                                          DirectoryWatcher fires Created
                                            for Diced_10.0.4511\

                                          ShardRegistry.GetOrCreate(
                                            "Diced_10.0.4511",
                                            "c:\job\Diced_10.0.4511")
                                            → opens existing .audit\audit.db
                                            → 1,420 rows preserved

                                          ManifestManager.RecordArrival(
                                            "c:\job\Diced_10.0.4511",
                                            "FALCON-02")
                                            → manifest.json updated:
                                              history[1]: FALCON-02
                                                from: 2026-04-15T14:05Z
                                                to:   null  (active)
                                                events: 0

                                          CatchUpScanner(jobPath=Diced_10.0.4511)
                                            → detects any offline changes
                                            → inserts catch-up rows if needed

                                          FileMonitorService resumes watching
                                          New events: machine_name = FALCON-02
                                            → history[1].events incremented

Result: audit.db has complete history from BOTH machines.
manifest.json shows full custody chain in plain text.
```

---

## A.6 — New Job Arrival Flow (Live, While Service Running)

```
Operator (or RMS)             DirectoryWatcher           FalconAuditService
       │                            │                           │
       │  Create / paste            │                           │
       │  c:\job\NewJob\            │                           │
       │──────────────────────────►│                           │
       │                            │  FSW Created event       │
       │                            │  (directory depth=1)     │
       │                            │─────────────────────────►│  OnDirCreated()
       │                            │                           │
       │                            │                           │  ShardRegistry.GetOrCreate(
       │                            │                           │    "NewJob",
       │                            │                           │    "c:\job\NewJob")
       │                            │                           │  → create .audit\audit.db
       │                            │                           │  → WAL mode + schema
       │                            │                           │
       │                            │                           │  ManifestManager.RecordArrival(
       │                            │                           │    jobPath, machineName)
       │                            │                           │  → create manifest.json
       │                            │                           │
       │                            │                           │  CatchUpScanner.RunAsync(
       │                            │                           │    jobPath = "c:\job\NewJob")
       │                            │                           │  → scan only NewJob\
       │                            │                           │  → insert Created rows for
       │                            │                           │     all existing files
       │                            │                           │
       │                            │                           │  FileMonitorService FSW
       │                            │                           │  already covers c:\job\**
       │                            │                           │  (no restart needed)
       │                            │                           │
       ▼                            ▼                           ▼
  [job folder ready]          [event processed]         [new shard active;
                                                         first recipe save
                                                         appears in NewJob
                                                         audit.db within 1s]
```

---

## A.7 — CatchUpScanner Flow (Per-Job Scope)

```
CatchUpScanner.RunAsync(watchPath, ct, jobPath?)
  │
  ├── Guard: SemaphoreSlim(1) — skip if already running
  │
  ├── Determine scan root:
  │     if jobPath != null → scan only that job subtree
  │     else               → scan all of watchPath (full scan at startup)
  │
  ├── repo = (jobPath != null)
  │           ? ShardRegistry.GetOrCreate(jobName, jobPath)
  │           : GlobalRepo  (for status.ini and unknown paths)
  │
  ├── currentFiles = EnumerateFiles(scanRoot, IncludedExtensions)
  │
  ├── allBaselines = repo.GetAllBaselinesAsync()
  │   baselineMap  = Dictionary<path, FileBaseline>
  │
  ├── Phase 1 — scan current files:
  │   for each file:
  │     ├── try: hash = SHA256(file)
  │     │   catch IOException → skip (deleted mid-scan; FSW will catch it)
  │     │
  │     ├── baseline == null?
  │     │     → InsertAuditLog(Created, note="catch-up")
  │     │     → UpsertBaseline(newHash)
  │     │     → ContentCache.Set(path, content) if P1
  │     │
  │     ├── hash != baseline.LastHash?
  │     │     → InsertAuditLog(Modified, oldHash=baseline, newHash=hash, note="catch-up")
  │     │     → UpsertBaseline(newHash)
  │     │     → ContentCache.Set(path, content) if P1
  │     │
  │     └── hash == baseline.LastHash?
  │           → ContentCache.Set(path, content) if P1  ← prime cache for first live diff
  │           → UpsertBaselineTimestamp(path, now)
  │
  └── Phase 2 — detect deletions:
      currentPaths = Set(currentFiles)
      for each baseline not in currentPaths:
        → InsertAuditLog(Deleted, oldHash=baseline, note="catch-up")
        → DeleteBaseline(path)
        → ContentCache.Remove(path)
```

---

## A.8 — FileClassifier Hot-Reload Flow

```
Service startup:
  FileClassifier.LoadRules(configPath)
    ├── File.ReadAllText(FileClassificationRules.json)
    ├── JsonSerializer.Deserialize<RuleSet>()
    ├── Compile each glob pattern → Regex (RegexOptions.Compiled | IgnoreCase)
    └── Interlocked.Exchange(ref _rules, ImmutableList.Create(compiled))

  FSW_2 = new FileSystemWatcher(configPath)
    { Filter = "FileClassificationRules.json", NotifyFilters = LastWrite }
    FSW_2.Changed += OnConfigChanged

                    ──── normal operation ────

  Operator edits FileClassificationRules.json
    (adds a new rule, changes a priority)
    │
    ▼
  FSW_2 fires Changed event
    OnConfigChanged():
      ├── debounce 1 second (file may still be written)
      ├── FileClassifier.LoadRules(configPath)   ← hot-reload
      │     new rules compiled atomically
      └── Logger: "FileClassificationRules.json reloaded. Rules={N}"

  Next Classify() call uses new rules immediately.
  No service restart. No event loss.
```

---

## A.9 — FSW Buffer Overflow Recovery Flow

```
Mass operation (job import, batch recipe save)
  ~500+ file writes in <1 second
    │
    ▼
  OS FSW internal 64 KB buffer fills up
    │
    ▼
  FSW.Error event fires → OnError()
    │
    ├── Log WARNING to Serilog + Windows Event Log (Event ID 1001)
    │
    ├── _watcher.Dispose()
    │
    ├── InitWatcher()   ← new FSW, events enabled
    │
    └── Task.Run(CatchUpScanner.RunAsync(watchPath, ct))
          ← full scan detects anything missed during overflow window
          ← runs concurrently with re-enabled FSW
          ← SemaphoreSlim(1) prevents duplicate concurrent scans

Result: no permanent audit gap. Missed changes recovered via catch-up.
```

---

## A.10 — SQLite Write Safety Model

```
Thread model:
  ┌─────────────────────────────────────────────────────────────────┐
  │  ThreadPool threads (FSW callbacks, DirectoryWatcher callbacks)  │
  │    → debounce timers → BlockingCollection.TryAdd()              │
  └──────────────────────────┬──────────────────────────────────────┘
                             │ single-producer
                             ▼
  ┌──────────────────────────────────────────────────────────────────┐
  │  AuditWriter thread  (Thread("AuditWriter"), IsBackground=true)  │
  │    → GetConsumingEnumerable()                                    │
  │    → FileChangeHandler.HandleAsync()  [sequential]              │
  │    → ShardRegistry.GetOrCreate(jobName)  → per-job repo         │
  │    → repo.InsertAuditLogAsync()                                  │
  │         └── SemaphoreSlim(1) per repo   ← write lock            │
  └──────────────────────────────────────────────────────────────────┘

Per shard SqliteRepository:
  _conn      (write)  ← WAL mode, synchronous=NORMAL, busy_timeout=3000ms
  _readConn  (read)   ← WAL mode allows concurrent reads
  _writeLock SemaphoreSlim(1) ← prevents any future multi-thread collision

WAL mode: readers never block writers; writer never blocks readers.
```

---

## A.11 — Manifest.json State Machine

```
State: NO MANIFEST (new job, first time on any machine)
  │
  │  ManifestManager.RecordArrival("FALCON-01")
  ▼
State: MANIFEST EXISTS, one open entry
  {
    "created": { "machine": "FALCON-01", "at": "T1" },
    "history": [
      { "machine": "FALCON-01", "from": "T1", "to": null, "events": N }
    ]
  }
  │
  │  ManifestManager.RecordDeparture() on service stop / job folder remove
  ▼
State: MANIFEST EXISTS, entry closed
  {
    "history": [
      { "machine": "FALCON-01", "from": "T1", "to": "T2", "events": N }
    ]
  }
  │
  │  Job moved to FALCON-02; ManifestManager.RecordArrival("FALCON-02")
  ▼
State: TWO ENTRIES, second open
  {
    "history": [
      { "machine": "FALCON-01", "from": "T1", "to": "T2",   "events": N  },
      { "machine": "FALCON-02", "from": "T3", "to": null,   "events": M  }
    ]
  }

Write safety: all manifest writes go to manifest.json.tmp, then
File.Move(tmp, manifest.json, overwrite:true) — atomic on NTFS.
```

---

## A.12 — Component Dependency Graph

```
Program.cs (DI registration — single exe)
  │
  ├─── AUDIT SERVICE DEPENDENCIES ───────────────────────────────────────────
  │
  ├── SqliteRepository (globalDbPath)        ← global.db only
  │     └── loads MonitorConfig (WatchPath, ClassificationRulesPath, ...)
  │
  ├── ShardRegistry
  │     └── creates SqliteRepository per job (lazy, on first event)
  │
  ├── ManifestManager
  │
  ├── DirectoryWatcher
  │     ├── callbacks → ShardRegistry.GetOrCreate / Remove
  │     ├── callbacks → ManifestManager.RecordArrival / Departure
  │     └── callbacks → CatchUpScanner.RunJobAsync (scoped)
  │
  ├── ContentCache
  │
  ├── FileClassifier
  │     └── loads FileClassificationRules.json (hot-reload via FSW)
  │
  ├── ChangeDescriptionEnricher
  │     └── loads ParameterDescriptions.json (hot-reload via FSW)
  │
  ├── FileChangeHandler
  │     ├── depends: ShardRegistry, SqliteRepository (global), FileClassifier,
  │     │            ContentCache, ManifestManager, ChangeDescriptionEnricher,
  │     │            MonitorConfig
  │     ├── routes job files to job shard via ShardRegistry
  │     └── routes global files to SqliteRepository (globalRepo)
  │
  ├── CatchUpScanner
  │     ├── depends: ShardRegistry, SqliteRepository (global), FileClassifier,
  │     │            ContentCache, ChangeDescriptionEnricher, MonitorConfig
  │     └── can scope to single jobPath
  │
  ├── FileMonitorService
  │     ├── owns FSW (c:\job\ full tree, IncludeSubdirectories=true)
  │     ├── debounce → Channel<ChangeEvent> → N consumer tasks
  │     └── depends: FileChangeHandler, CatchUpScanner, MonitorConfig
  │
  ├── Worker (BackgroundService)
  │     ├── startup: enumerate jobs → ShardRegistry, ManifestManager, CatchUpScanner
  │     └── wire DirectoryWatcher, start FileMonitorService
  │
  ├─── WEB SERVER DEPENDENCIES ──────────────────────────────────────────────
  │
  ├── JobDiscoveryService
  │     ├── reads AuditService:WatchPath + AuditService:GlobalDbPath from appsettings
  │     ├── 30 s Timer → scan c:\job\*\.audit\audit.db
  │     └── exposes volatile IReadOnlyList<string> KnownJobs + ShardPath(jobName)
  │
  ├── QueryRepository
  │     ├── depends: JobDiscoveryService
  │     ├── ConcurrentDictionary<dbPath, SqliteConnection> (read-only, WAL)
  │     ├── ListJobs()           → aggregated stats per job shard
  │     ├── GetEvents()          → paginated + filtered audit_log rows
  │     ├── GetEvent()           → single row with diff / old content
  │     └── GetFileHistory()     → all rows for one rel_filepath
  │
  └── Kestrel REST API  (http://0.0.0.0:5100, Windows Auth, role: Auditor)
        ├── JobsEndpoints        GET /api/jobs
        │                        GET /api/jobs/{job}/manifest
        ├── EventsEndpoints      GET /api/events/{job}
        │                        GET /api/events/{job}/{id}
        └── FileHistoryEndpoints GET /api/filehistory/{job}
```

---

## A.13 — Project File Structure

Single exe — `FalconAuditWebServer` project hosts both the audit service and the REST API.

```
FalconAuditWebServer/                   ← single project (assembly: FalconAuditService.exe)
├── FalconAuditWebServer.csproj         ← SDK: Microsoft.NET.Sdk.Web
│                                          AssemblyName: FalconAuditService
│                                          Packages: Sqlite · Negotiate · WindowsServices
│                                                    DiffPlex · Serilog (File+EventLog)
├── Program.cs                          ← merged startup: service DI + web DI +
│                                          UseWindowsService() + Kestrel endpoints
├── appsettings.json                    ← unified config: AuditService: · Kestrel: · Serilog:
│
├─── AUDIT SERVICE ──────────────────────────────────────────────────────────
├── Worker.cs                           namespace FalconAuditService
├── ChangeEvent.cs                      namespace FalconAuditService
├── ContentCache.cs                     namespace FalconAuditService
├── FileMonitorService.cs               namespace FalconAuditService
├── FileChangeHandler.cs                namespace FalconAuditService
├── FileClassifier.cs                   namespace FalconAuditService
├── ChangeDescriptionEnricher.cs        namespace FalconAuditService
├── HashHelper.cs                       namespace FalconAuditService
├── DiffHelper.cs                       namespace FalconAuditService
├── SqliteRepository.cs                 namespace FalconAuditService
├── ShardRegistry.cs                    namespace FalconAuditService
├── ManifestManager.cs                  namespace FalconAuditService
├── DirectoryWatcher.cs                 namespace FalconAuditService
├── CatchUpScanner.cs                   namespace FalconAuditService
├── Models/
│   ├── AuditLogEntry.cs               namespace FalconAuditService.Models
│   ├── FileBaseline.cs                namespace FalconAuditService.Models
│   ├── MonitorConfig.cs               namespace FalconAuditService.Models
│   └── JobManifest.cs                 namespace FalconAuditService.Models
│
├─── WEB SERVER ─────────────────────────────────────────────────────────────
├── Endpoints/
│   ├── JobsEndpoints.cs               namespace FalconAuditWebServer.Endpoints
│   ├── EventsEndpoints.cs             namespace FalconAuditWebServer.Endpoints
│   └── FileHistoryEndpoints.cs        namespace FalconAuditWebServer.Endpoints
├── Services/
│   ├── JobDiscoveryService.cs         namespace FalconAuditWebServer.Services
│   └── QueryRepository.cs             namespace FalconAuditWebServer.Services
├── Models/
│   ├── AuditEventSummary.cs           namespace FalconAuditWebServer.Models
│   ├── AuditEventDetail.cs            namespace FalconAuditWebServer.Models
│   ├── EventFilter.cs                 namespace FalconAuditWebServer.Models
│   ├── JobSummary.cs                  namespace FalconAuditWebServer.Models
│   └── FileHistoryItem.cs             namespace FalconAuditWebServer.Models
│
├─── CONFIG (deployed alongside exe) ───────────────────────────────────────
├── FileClassificationRules.json        ← 69 glob rules (hot-reload)
└── ParameterDescriptions.json          ← INI key → human label (hot-reload)
```

---

## A.14 — REST API Detail

### A.14.1 — Authentication & Authorisation

```
Client (browser / tool)
  │
  │  HTTP GET http://falcon-machine:5100/api/jobs
  ▼
Kestrel (port 5100)
  │
  ▼
Windows Authentication middleware  (Negotiate — Kerberos or NTLM)
  ├── unauthenticated first request → 401 + WWW-Authenticate: Negotiate
  ├── client sends SPNEGO token
  └── token validated against Active Directory / local SAM
        │
        ▼
Authorization middleware
  ├── FallbackPolicy: RequireAuthenticatedUser()  ← all routes require login
  └── AuditorOnly policy: RequireRole("Auditor")  ← applied per-endpoint
        │
        ▼
Endpoint handler
```

### A.14.2 — Endpoint Reference

```
GET /api/jobs
  Response: [ { jobName, shardPath, totalEvents,
                firstEvent, lastEvent, machines,
                shardSizeBytes } ]
  Source:   QueryRepository.ListJobs()
            → reads audit_log COUNT, MIN/MAX changed_at,
              GROUP_CONCAT(machine_name) per shard

GET /api/jobs/{jobName}/manifest
  Response: manifest.json contents as JSON
  Source:   reads .audit\manifest.json directly

GET /api/events/{jobName}
  Query params (all optional):
    module · priority · service · eventType · machine
    from · to · path   (filters)
    sort=asc|desc      page · pageSize
  Response: { items: [ AuditEventSummary ], total: N }
  Source:   QueryRepository.GetEvents()
            → parameterised SQL with dynamic WHERE

GET /api/events/{jobName}/{id}
  Response: AuditEventDetail  (includes old_content, diff_text)
  Source:   QueryRepository.GetEvent()

GET /api/filehistory/{jobName}?relFilepath={path}
  Response: [ FileHistoryItem ]  ordered by changed_at ASC
  Source:   QueryRepository.GetFileHistory()
            → WHERE rel_filepath = @p
```

### A.14.3 — Query Request Flow

```
Browser                  Kestrel                JobDiscoveryService     QueryRepository
   │                        │                          │                      │
   │  GET /api/events/      │                          │                      │
   │  Diced_10.0.4511?      │                          │                      │
   │  priority=P1&page=2    │                          │                      │
   │───────────────────────>│                          │                      │
   │                        │  EventsEndpoints.Map()   │                      │
   │                        │  bind EventFilter        │                      │
   │                        │─────────────────────────────────────────────────>
   │                        │                          │  GetEvents(job, f)   │
   │                        │                          │<─────────────────────│
   │                        │                          │  ShardPath(job)      │
   │                        │                          │─────────────────────>│
   │                        │                          │  path returned       │
   │                        │                          │<─────────────────────│
   │                        │                                                 │
   │                        │                   GetConnection(shardPath)      │
   │                        │                   ← read-only WAL connection    │
   │                        │                   SELECT ... WHERE priority=P1  │
   │                        │                   LIMIT pageSize OFFSET (n-1)*ps│
   │                        │                                                 │
   │                        │<────────────────────────────────────────────────│
   │                        │  { items: [...], total: 42 }                    │
   │<───────────────────────│                                                 │
   │  200 OK  JSON          │                                                 │
```

### A.14.4 — JobDiscoveryService Refresh Cycle

```
  Startup:  Refresh() called immediately
  │
  └─ scan c:\job\*
       for each subfolder that contains .audit\audit.db
         → add jobName to KnownJobs list
  │
  └─ 30 s Timer fires
       └─ Refresh() again
            ├── new job folders picked up automatically
            └── removed job folders dropped from list

  Note: the audit service and web server share the same
  process; KnownJobs tracks the same shards the service
  is writing to.  No inter-process coordination needed.
```
