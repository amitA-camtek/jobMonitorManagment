# Appendix E — Job Origin Detection Design

## Purpose

Extend FalconAuditService to detect and record **how** a job folder appeared under `C:\job\`:

| Classification | Meaning |
|---|---|
| `NewLocal` | Job was created from scratch by the BIS application on this machine |
| `CopiedFromRemote` | A complete job folder was copied from another machine (or volume) |
| `Unknown` | Insufficient files present at the time of detection |

The detection is **BIS-independent** — it relies only on filesystem metadata (NTFS timestamps and the existing manifest), not on BIS file formats or internal logic.

---

## Flow Diagrams

### Flow 1 — Overall System: Job Created vs. Job Copied

```
BIS Application                  Filesystem                  FalconAuditService
      │                               │                              │
      │── creates C:\job\{Name}\ ────►│                              │
      │                               │── FSW OnCreated ────────────►│
      │                               │                              │ onArrived()
      │                               │                              ├─ GetOrCreate(shard)
      │                               │                              ├─ RecordArrival(manifest)
      │                               │                              └─ ScheduleCheck() ─┐
      │── writes setup files ─────────►│                              │                  │
      │   AlignRTP.ini                 │── FSW file events ──────────►│ FileChangeHandler │
      │   GlobalRTP.ini                │                              │ (normal audit)    │
      │   MetaData.ini ...             │                              │                   │
      │                               │                              │   [wait 30 s] ◄───┘
      │                               │                              │
      │                               │                              │ DetermineAndRecordAsync()
      │                               │                              ├─ Stage 1: manifest age check
      │                               │                              ├─ Stage 2: NTFS delta on P1 files
      │                               │                              │
      │                               │                              ├─► "NewLocal"       or
      │                               │                              └─► "CopiedFromRemote"
      │                               │                                     │
      │                               │              monitor_config ◄───────┘
      │                               │              manifest.json  ◄───────┘
```

---

### Flow 2 — Detection Algorithm

```
DetermineAndRecordAsync(jobName, jobPath)
         │
         ▼
  monitor_config.job_origin already set?
  ┌──YES──────────────────────────────────────────────────┐
  │  (service restarted, job already classified)          │
  │  STOP — do not overwrite                              │
  └───────────────────────────────────────────────────────┘
         │ NO
         ▼
  ┌─────────────────────────────────────────────────────────┐
  │  STAGE 1 — Manifest check (definitive if available)     │
  │                                                         │
  │  manifest.json exists?  ──NO──► go to Stage 2           │
  │         │ YES                                           │
  │  manifest.Created.At age > (settle + 60 s)?             │
  │    NO (just created this session) ──► go to Stage 2     │
  │         │ YES (from a previous service session)         │
  │                                                         │
  │  Any History entry from a different machine?            │
  │    YES ──► "CopiedFromRemote"  ──────────────────────►  │
  │    NO  ──► "NewLocal"          ──────────────────────►  │
  └─────────────────────────────────────────────────────────┘
         │ (Stage 2)
         ▼
  ┌─────────────────────────────────────────────────────────┐
  │  STAGE 2 — NTFS timestamp sampling                      │
  │                                                         │
  │  Collect P1-priority files in job folder                │
  │  (skip .audit\, take top 10 sorted by LastWriteTime)    │
  │                                                         │
  │  < 3 P1 files found?                                    │
  │    YES ──► "Unknown"                                    │
  │            is this first attempt? ──YES──► reschedule   │
  │                                   ──NO──►  write "Unknown", stop │
  │         │ ≥ 3 files                                     │
  │                                                         │
  │  For each file:                                         │
  │    delta = file.CreationTimeUtc − file.LastWriteTimeUtc │
  │                                                         │
  │  New file (created fresh on this machine):              │
  │    CreationTime ≈ LastWriteTime  →  delta ≈ 0           │
  │                                                         │
  │  Copied file (content from another machine/volume):     │
  │    CreationTime = now  >  LastWriteTime = old           │
  │    delta > 5 min                                        │
  │                                                         │
  │  ≥ 60% of sample has delta > 5 min?                     │
  │    YES ──► "CopiedFromRemote"                           │
  │    NO  ──► "NewLocal"                                   │
  └─────────────────────────────────────────────────────────┘
         │ result
         ▼
  Write to: monitor_config (key = job_origin)
            manifest.json  (origin field)
  Log: "JobOriginChecker: '{jobName}' → {origin}"
```

---

### Flow 3 — First Installation Migration (pre-existing jobs)

```
Service starts for the first time on a BIS machine
that already has N job folders in C:\job\
         │
         ▼
DirectoryWatcher.EnumerateExisting()
  ├─► onArrived("Job001", "C:\job\Job001")
  ├─► onArrived("Job002", "C:\job\Job002")
  └─► onArrived("JobNNN", "C:\job\JobNNN")
         │ (all fire in rapid succession)
         │
         ▼  For each job in parallel:
  RecordArrival() → creates fresh manifest.json (age = 0)
  GetOrCreate()   → creates new audit.db (empty monitor_config)
  ScheduleCheck() → starts 30-second timer

         │  [30 seconds pass — CatchUpScanner also runs]
         │
         ▼  For each job:
  DetermineAndRecordAsync()
         │
         ├─ monitor_config.job_origin? → empty (first run) → continue
         │
         ├─ STAGE 1: manifest age = ~30 s < (30 + 60) s → SKIP
         │
         └─ STAGE 2: NTFS delta check
                 │
                 ├─ Job was created by BIS on THIS machine (any time in the past):
                 │     CreationTime (on this NTFS) ≈ LastWriteTime (BIS wrote it here)
                 │     delta ≈ 0  →  "NewLocal"  ✓
                 │
                 └─ Job was copied from another machine (any time in the past):
                       CreationTime (copy time on this volume) > LastWriteTime (from source)
                       delta > 5 min  →  "CopiedFromRemote"  ✓

         [Result stored in monitor_config — never recomputed on service restarts]
```

---

### Flow 4 — Per-Event File Era Classification

Every audit event is labelled with a `file_era` value that tells auditors whether the file was part of the initial job snapshot or appeared later during active processing.

#### A) New Job Created — Files Are `JobInit`

```
BIS creates C:\job\{JobName}\
        │
        ▼
DirectoryWatcher.onArrived(jobName, jobPath)
        │
        ├─► ShardRegistry.GetOrCreate()
        │         Creates: {jobPath}\.audit\audit.db
        │         (schema includes file_era column)
        │
        ├─► ManifestManager.RecordArrival()
        │         Creates: {jobPath}\.audit\manifest.json
        │
        ├─► JobOriginChecker.ScheduleCheck()
        │         Starts settle timer (30 s) — runs in background
        │
        └─► CatchUpScanner.RunJobAsync()
                  │
                  ├─ IsInitialScanDone()? → FALSE  (brand-new shard)
                  │
                  ├─ For each file found on disk:
                  │     AuditLogEntry { FileEra = "JobInit", IsBackfill = true }
                  │     → saved to audit_log
                  │
                  └─ SetInitialScanDoneAsync()
                        monitor_config: initial_scan_done = "true"

        ... 30 seconds later ...

JobOriginChecker fires:
        ├─ DetermineOrigin() → "NewLocal" or "CopiedFromRemote"
        └─ Stores result in monitor_config + manifest.json
```

#### B) New File Created During Job Lifecycle — File Is `Runtime`

```
BIS or operator creates a new file inside C:\job\{JobName}\
        │
        ▼
FileSystemWatcher fires Created event
        │
        ▼
FileChangeHandler.HandleAsync()
        │
        ├─ FileEra  = "Runtime"    ← always for real-time FSW events
        ├─ IsBackfill = false
        │
        └─ AuditLogEntry { FileEra = "Runtime" } → saved to audit_log

        ▼

GET /api/jobs/{jobName}/report
        └─ EventFilter { FileEra = "Runtime" }
              → JobInit files excluded from the report
              → only Runtime changes are shown to auditors
```

#### Summary: `file_era` Rules

| Source | `file_era` | Appears in Report |
|---|---|---|
| `CatchUpScanner` — **first ever** scan of this shard | `"JobInit"` | No (excluded) |
| `CatchUpScanner` — **subsequent** scans (FSW gap / restart) | `"Runtime"` | Yes |
| `FileChangeHandler` — real-time FSW event | `"Runtime"` | Yes |
| Legacy rows (pre-feature) | `NULL` | Yes (shown by default) |

---

## Component Design

### New Component: `JobOriginChecker`

A singleton service that:
- Holds a per-job cancellable settle timer (`CancellationTokenSource` per job name)
- On `ScheduleCheck`: starts a non-blocking `Task.Delay` timer; replaces any existing timer for that job
- On `CancelCheck`: cancels the pending timer (called when job departs before timer fires)
- After settle: runs two-stage detection, writes result to shard and manifest, logs outcome
- On retry (Unknown result): reschedules once; if still Unknown writes `"Unknown"` and stops

### NTFS Signal

On Windows NTFS:
- `File.CreationTimeUtc` — resets to **now** when a file is copied to this volume
- `File.LastWriteTimeUtc` — **preserved** from the source when copied (Windows Explorer, .NET `File.Copy`, `xcopy`)
- Therefore: for a copied file, `CreationTime > LastWriteTime + threshold` regardless of when the copy happened

This signal is permanent — it does not expire and works equally well for files copied years ago.

---

## Configuration

| Key | Default | Meaning |
|---|---|---|
| `JobSettleTimeSeconds` | `30` | Wait after folder arrival before checking |
| `OriginSampleSize` | `10` | Max P1 files to NTFS-sample |
| `OriginDeltaMinutes` | `5` | Threshold: `CreationTime − LastWriteTime > this` → copied |
| `OriginCopiedRatio` | `0.6` | Fraction of sample that must exceed delta to classify as copied |

---

## Persistence

| Store | Key / Field | Value |
|---|---|---|
| `monitor_config` table (per shard) | `job_origin` | `"NewLocal"` \| `"CopiedFromRemote"` \| `"Unknown"` |
| `manifest.json` | `"origin"` | same |
| `manifest.json` | `"originDeterminedAt"` | ISO-8601 UTC timestamp |
| `GET /api/jobs` response | `Origin` field in `JobSummary` | same, read at query time |

---

## Files Changed

### Job Origin Detection (`JobOriginChecker`)

| File | Type | Purpose |
|---|---|---|
| `JobOriginChecker.cs` | **New** | Settle timer, two-stage detection, persistence |
| `Models/MonitorConfig.cs` | Modified | 4 new config keys with defaults |
| `Models/JobManifest.cs` | Modified | `origin` + `originDeterminedAt` fields |
| `ManifestManager.cs` | Modified | Public `ReadManifest(jobPath)`; `UpdateOriginAsync` |
| `SqliteRepository.cs` | Modified | `SetConfigValueAsync` / `GetConfigValue` on `monitor_config` |
| `Models/JobSummary.cs` | Modified | `string? Origin` property |
| `Services/QueryRepository.cs` | Modified | Reads `job_origin` when building each `JobSummary` |
| `Program.cs` | Modified | Registers singleton; wires into `onArrived` / `onDeparted` |
| `appsettings.json` | Modified | 4 new keys under `"AuditService"` |

### Per-Event File Era Classification (`file_era`)

| File | Type | Purpose |
|---|---|---|
| `Models/AuditLogEntry.cs` | Modified | `string? FileEra` property |
| `SqliteRepository.cs` | Modified | `file_era` column in `audit_log`; `IsInitialScanDone()` / `SetInitialScanDoneAsync()` |
| `CatchUpScanner.cs` | Modified | Detects first-ever scan per shard; sets `FileEra = "JobInit"` or `"Runtime"` |
| `FileChangeHandler.cs` | Modified | Sets `FileEra = "Runtime"` on all real-time FSW events |
| `Models/AuditEventSummary.cs` | Modified | `string? FileEra` in API response |
| `Models/EventFilter.cs` | Modified | `string? FileEra` filter field |
| `Services/QueryRepository.cs` | Modified | SELECT / WHERE / mapping for `file_era` |
| `Endpoints/EventsEndpoints.cs` | Modified | `fileEra` query param; report endpoint defaults to `"Runtime"` |

---

## Edge Cases

| Scenario | Detection path | Result |
|---|---|---|
| BIS creates job fresh on this machine | Stage 2: delta ≈ 0 on all files | `NewLocal` ✓ |
| User copies folder via Explorer / .NET `File.Copy` | Stage 2: `CreationTime` resets, `LastWriteTime` preserved → delta > 5 min | `CopiedFromRemote` ✓ |
| Job arrives with `.audit\manifest.json` from source machine | Stage 1: foreign machine in History → definitive | `CopiedFromRemote` ✓ |
| Service restarts — job already classified | `monitor_config.job_origin` found → early exit | Unchanged ✓ |
| First install on busy BIS machine | Stage 1 skipped (manifest too new); Stage 2 NTFS check | Correct for both local and copied ✓ |
| Folder empty when timer fires | `< 3 P1 files` → retry once | Resolved on second attempt ✓ |
| `robocopy /COPYALL` (preserves timestamps) | Stage 2 may misclassify; Stage 1 catches it if `.audit\` was also copied | Usually correct ✓ |

---

## Verification

1. Start service. Create a brand-new job via the BIS app.
2. After 30 s: `GET /api/jobs` → `"origin": "NewLocal"` on that job.
3. Copy a complete job folder from another machine into `C:\job\`.
4. After 30 s: `GET /api/jobs` → `"origin": "CopiedFromRemote"`.
5. Restart service — verify `Origin` is unchanged (read from `monitor_config`, not recomputed).
6. Check `{jobPath}\.audit\manifest.json` — confirm `"origin"` and `"originDeterminedAt"` fields are present.
