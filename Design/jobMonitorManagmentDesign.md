# Job Monitor Management Design

> **Based on:** `04_recommended_design.md`  
> **Date:** 2026-04-23  
> **Role:** Senior software architect, Camtek Falcon BIS platform  
> **Scope:** Per-job audit isolation + portable job history + configurable file list

---

## Background

The `04_recommended_design.md` defines a Windows Service (`FalconAuditService`) that monitors `c:\job\` and writes all audit events to a single central SQLite database. This design addresses two additional requirements:

1. **Job portability** — operators physically cut/paste job folders between Falcon machines. The full audit history for that job must travel with the folder automatically, with no operator export/import step.
2. **Configurable file list** — the list of monitored files and their P1/P2/P3 classifications must be editable by the user (e.g., to add a new recipe file type introduced in a software update) without recompiling or redeploying the service.

All roles and restrictions from the original design are preserved: P1/P2/P3 priority levels, module classifications (Recipe / Job / Config / AlignmentData / DieMap / Log / ScanResult / Sequence), owner services (RMS / Falcon.Net / AOI_Main / DataServer), 500 ms debounce, CatchUpScanner, WAL-mode SQLite, `SemaphoreSlim(1)` write safety, P1 content snapshotting, and unified diff.

---

## Section 1 — Configurable File List: FileClassificationRules.json

### Concept

`FileClassifier.cs` currently embeds its path-to-classification mapping as a hardcoded table. This section replaces that with an external JSON file, `FileClassificationRules.json`, loaded at service startup and hot-reloaded on any change — no service restart required.

### How the JSON is generated

The rule set is derived from `JobConfigurationFileList.json` by collapsing every concrete job/recipe path into a glob pattern:

| Concrete path | Glob pattern |
|---|---|
| `c:\job\Diced_10.0.4511\S1\Recipes\R1\Recipe.ini` | `c:\job\**\Recipes\*\Recipe.ini` |
| `c:\job\Diced_10.0.4511\S1\Recipes\R2\Recipe.ini` | *(same pattern — one rule covers all recipes)* |
| `c:\job\Diced_10.0.4511\S1\Metadata.ini` | `c:\job\**\Metadata.ini` |

Rules within the same folder depth are deduplicated. `**` matches any number of nested directories; `*` matches one path segment.

### FileClassifier changes

- `LoadRules(string configPath)` — reads the JSON, compiles glob patterns to `Regex` once, stores rules as an `ImmutableList`.
- A second `FileSystemWatcher` watches only `FileClassificationRules.json`. On `Changed` event: parse, compile, swap the list atomically (`Interlocked.Exchange`). No lock needed on the read path.
- Rules are evaluated in order — most-specific patterns are listed first. First match wins.
- Config file location: `C:\bis\auditlog\FileClassificationRules.json` (overridable via `monitor_config` key `classification_rules_path`).

### Classify method — unchanged signature

```csharp
public ClassificationResult Classify(string filePath)
{
    var norm = filePath.ToLowerInvariant().Replace('\\', '/');
    var rules = _rules;  // snapshot of ImmutableList — lock-free

    foreach (var rule in rules)
    {
        if (rule.MatchType == "exact" && norm == rule.NormalisedPattern)
            return rule.Result;
        if (rule.MatchType == "glob" && rule.CompiledRegex.IsMatch(norm))
            return rule.Result;
    }
    return _default;
}
```

### Full FileClassificationRules.json

Derived from `JobConfigurationFileList.json`. To add a new monitored file: add one entry to the `rules` array and save — no recompile, service reloads within 2 seconds.

```json
{
  "version": "1.0",
  "generatedFrom": "JobConfigurationFileList.json",
  "rules": [

    // ── Global files ───────────────────────────────────────────────────────────
    { "pattern": "c:\\job\\status.ini",                                          "matchType": "exact", "module": "Config",        "ownerService": "Falcon.Net", "monitorPriority": "P1" },

    // ── Job-level files ────────────────────────────────────────────────────────
    { "pattern": "c:\\job\\*\\Metadata.ini",                                     "matchType": "glob",  "module": "Job",           "ownerService": "RMS",        "monitorPriority": "P1" },
    { "pattern": "c:\\job\\**\\Metadata.ini",                                    "matchType": "glob",  "module": "Job",           "ownerService": "RMS",        "monitorPriority": "P1" },
    { "pattern": "c:\\job\\**\\ProductionInfo.ini",                              "matchType": "glob",  "module": "Job",           "ownerService": "RMS",        "monitorPriority": "P1" },
    { "pattern": "c:\\job\\**\\MultiRecipe.ini",                                 "matchType": "glob",  "module": "Recipe",        "ownerService": "RMS",        "monitorPriority": "P1" },
    { "pattern": "c:\\job\\**\\DefectsClustering.ini",                           "matchType": "glob",  "module": "Recipe",        "ownerService": "RMS",        "monitorPriority": "P1" },
    { "pattern": "c:\\job\\**\\ScanCondition.ini",                               "matchType": "glob",  "module": "Recipe",        "ownerService": "RMS",        "monitorPriority": "P1" },
    { "pattern": "c:\\job\\**\\Wafer2Table.ini",                                 "matchType": "glob",  "module": "AlignmentData", "ownerService": "AOI_Main",   "monitorPriority": "P1" },

    // ── P1 recipe files ────────────────────────────────────────────────────────
    { "pattern": "c:\\job\\**\\Recipes\\*\\Recipe.ini",                          "matchType": "glob",  "module": "Recipe",        "ownerService": "RMS",        "monitorPriority": "P1" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\ProductInfo.ini",                     "matchType": "glob",  "module": "Recipe",        "ownerService": "AOI_Main",   "monitorPriority": "P1" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\Waferinfo.ini",                       "matchType": "glob",  "module": "Recipe",        "ownerService": "AOI_Main",   "monitorPriority": "P1" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\GlobalRTP.ini",                       "matchType": "glob",  "module": "Recipe",        "ownerService": "RMS",        "monitorPriority": "P1" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\RTP.txt",                             "matchType": "glob",  "module": "Recipe",        "ownerService": "RMS",        "monitorPriority": "P1" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\ZoomLevels.ini",                      "matchType": "glob",  "module": "Recipe",        "ownerService": "RMS",        "monitorPriority": "P1" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\zones.ini",                           "matchType": "glob",  "module": "Recipe",        "ownerService": "RMS",        "monitorPriority": "P1" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\Params_WaferInfo.ini",                "matchType": "glob",  "module": "Recipe",        "ownerService": "RMS",        "monitorPriority": "P1" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\Zones\\*.ini",                        "matchType": "glob",  "module": "Recipe",        "ownerService": "RMS",        "monitorPriority": "P1" },

    // ── P1 alignment files ─────────────────────────────────────────────────────
    { "pattern": "c:\\job\\**\\Recipes\\*\\Alignment.ini",                       "matchType": "glob",  "module": "AlignmentData", "ownerService": "AOI_Main",   "monitorPriority": "P1" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\AlignRtp.ini",                        "matchType": "glob",  "module": "AlignmentData", "ownerService": "RMS",        "monitorPriority": "P1" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\Params_AlignRTP.ini",                 "matchType": "glob",  "module": "AlignmentData", "ownerService": "RMS",        "monitorPriority": "P1" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\WaferAlignData\\AlignmentData.ini",   "matchType": "glob",  "module": "AlignmentData", "ownerService": "AOI_Main",   "monitorPriority": "P1" },

    // ── P1 config files ────────────────────────────────────────────────────────
    { "pattern": "c:\\job\\**\\Recipes\\*\\Params_SystemInfo.ini",               "matchType": "glob",  "module": "Config",        "ownerService": "RMS",        "monitorPriority": "P1" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\OpticPreset.ini",                     "matchType": "glob",  "module": "Config",        "ownerService": "DataServer", "monitorPriority": "P1" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\JobIllumLimits.ini",                  "matchType": "glob",  "module": "Config",        "ownerService": "DataServer", "monitorPriority": "P1" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\OpticToVCamStorage.json",             "matchType": "glob",  "module": "Config",        "ownerService": "AOI_Main",   "monitorPriority": "P1" },

    // ── P2 alignment files ─────────────────────────────────────────────────────
    { "pattern": "c:\\job\\**\\DefaultWafer2Table.ini",                          "matchType": "glob",  "module": "AlignmentData", "ownerService": "RMS",        "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\DefaultWafer2Table.ini",              "matchType": "glob",  "module": "AlignmentData", "ownerService": "RMS",        "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\AlignmentData.ini",                   "matchType": "glob",  "module": "AlignmentData", "ownerService": "RMS",        "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\WaferAlignData\\AlignmentStatisticsTime.txt", "matchType": "glob", "module": "AlignmentData", "ownerService": "AOI_Main", "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\WaferAlignData\\Alignment_*.txt",     "matchType": "glob",  "module": "AlignmentData", "ownerService": "AOI_Main",   "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\FocusMapping\\DieReferenceLocation.json", "matchType": "glob", "module": "AlignmentData", "ownerService": "AOI_Main", "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\TrainData\\FrameToChuck.ini",         "matchType": "glob",  "module": "AlignmentData", "ownerService": "AOI_Main",   "monitorPriority": "P2" },

    // ── P2 recipe files ────────────────────────────────────────────────────────
    { "pattern": "c:\\job\\**\\Recipes\\*\\ReferencesInfo.json",                 "matchType": "glob",  "module": "Recipe",        "ownerService": "RMS",        "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\zones.txt",                           "matchType": "glob",  "module": "Recipe",        "ownerService": "RMS",        "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\ScenariosMetadatas.ini",              "matchType": "glob",  "module": "Recipe",        "ownerService": "RMS",        "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\CcsLocalMeas.ini",                    "matchType": "glob",  "module": "Recipe",        "ownerService": "RMS",        "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\CleanReferenceConfiguration.ini",     "matchType": "glob",  "module": "Recipe",        "ownerService": "RMS",        "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\CleanReferenceFinalParams.ini",       "matchType": "glob",  "module": "Recipe",        "ownerService": "RMS",        "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\CreateReference3dOptions.ini",        "matchType": "glob",  "module": "Recipe",        "ownerService": "RMS",        "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\OverlayScan.ini",                     "matchType": "glob",  "module": "Recipe",        "ownerService": "RMS",        "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\SamplingMetrology.ini",               "matchType": "glob",  "module": "Recipe",        "ownerService": "RMS",        "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\UniqueArea.ini",                      "matchType": "glob",  "module": "Recipe",        "ownerService": "RMS",        "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\WaferMapRecipe.ini",                  "matchType": "glob",  "module": "Recipe",        "ownerService": "RMS",        "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\WaferToRefWafer.ini",                 "matchType": "glob",  "module": "Recipe",        "ownerService": "RMS",        "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\DieAlignment.dat_block.ini",          "matchType": "glob",  "module": "Recipe",        "ownerService": "RMS",        "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\DieMapAlignRes.dat_block.ini",        "matchType": "glob",  "module": "Recipe",        "ownerService": "RMS",        "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\CcsSetup.xml",                        "matchType": "glob",  "module": "Recipe",        "ownerService": "RMS",        "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\FocusMapping\\FocusMapping.ini",      "matchType": "glob",  "module": "Recipe",        "ownerService": "AOI_Main",   "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\TrainData\\Die.ini",                  "matchType": "glob",  "module": "Recipe",        "ownerService": "AOI_Main",   "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\TrainData\\DieRefToTrain.txt",        "matchType": "glob",  "module": "Recipe",        "ownerService": "AOI_Main",   "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\WaferInfo.dat",                       "matchType": "glob",  "module": "Recipe",        "ownerService": "RMS",        "monitorPriority": "P2" },

    // ── P2 DieMap files ────────────────────────────────────────────────────────
    { "pattern": "c:\\job\\**\\Recipes\\*\\DieMapRegPos.txt",                    "matchType": "glob",  "module": "DieMap",        "ownerService": "RMS",        "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\DieRegPos.txt",                       "matchType": "glob",  "module": "DieMap",        "ownerService": "RMS",        "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\DieMapping.dat",                      "matchType": "glob",  "module": "DieMap",        "ownerService": "RMS",        "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\DieRegPos.dat",                       "matchType": "glob",  "module": "DieMap",        "ownerService": "RMS",        "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\DieMapRegPos.dat",                    "matchType": "glob",  "module": "DieMap",        "ownerService": "RMS",        "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\zones.dat",                           "matchType": "glob",  "module": "DieMap",        "ownerService": "RMS",        "monitorPriority": "P2" },

    // ── P2 config files ────────────────────────────────────────────────────────
    { "pattern": "c:\\job\\**\\Recipes\\*\\ExternalCoordSystems.ini",            "matchType": "glob",  "module": "Config",        "ownerService": "RMS",        "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\WaferDataReadSettings.xml",           "matchType": "glob",  "module": "Config",        "ownerService": "RMS",        "monitorPriority": "P2" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\OpticLightMetadata\\config.ini",      "matchType": "glob",  "module": "Config",        "ownerService": "DataServer", "monitorPriority": "P2" },

    // ── P2 log files ───────────────────────────────────────────────────────────
    { "pattern": "c:\\job\\**\\Recipes\\*\\.dc_cache\\TransactionsHistory.ini",  "matchType": "glob",  "module": "Log",           "ownerService": "RMS",        "monitorPriority": "P2" },

    // ── P3 files ───────────────────────────────────────────────────────────────
    { "pattern": "c:\\job\\**\\Recipes\\*\\TrainData\\DieImage\\DieImageToTable.ini", "matchType": "glob", "module": "Recipe",   "ownerService": "AOI_Main",   "monitorPriority": "P3" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\TrainData\\DieImage\\ZoomLevels.ini", "matchType": "glob",  "module": "Recipe",        "ownerService": "AOI_Main",   "monitorPriority": "P3" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\ReferenceBackup\\ZoomLevels.ini",     "matchType": "glob",  "module": "Recipe",        "ownerService": "RMS",        "monitorPriority": "P3" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\SW_QA-*\\OpticsPreset.ini",           "matchType": "glob",  "module": "Config",        "ownerService": "RMS",        "monitorPriority": "P3" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\ScanOverlapLog.txt",                  "matchType": "glob",  "module": "ScanResult",    "ownerService": "AOI_Main",   "monitorPriority": "P3" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\ScanOverviewImage.txt",               "matchType": "glob",  "module": "ScanResult",    "ownerService": "AOI_Main",   "monitorPriority": "P3" },
    { "pattern": "c:\\job\\**\\Recipes\\*\\s_FrameData.dat.md",                  "matchType": "glob",  "module": "Recipe",        "ownerService": "RMS",        "monitorPriority": "P3" },

    // ── P4 (not stored, logged only) ───────────────────────────────────────────
    { "pattern": "c:\\job\\**\\Recipes\\*\\ImageProcessing.log",                 "matchType": "glob",  "module": "Log",           "ownerService": "AOI_Main",   "monitorPriority": "P4" }
  ],
  "defaultClassification": {
    "module": "Unknown",
    "ownerService": "Unknown",
    "monitorPriority": "P3"
  }
}
```

> **Rule count:** 69 glob/exact rules covering all file types from `JobConfigurationFileList.json`.  
> **To add a new file type:** append one JSON object to `rules` and save — the service hot-reloads within 2 seconds.

---

## Section 2 — Job Management: 3 Options for Per-Job Isolation

### Design constraint shared by all options

A job folder in `c:\job\` can be cut to a USB drive or network share and pasted onto a different Falcon machine. After the move, the destination machine's service must:
- Have access to the full audit history for that job (all events from all prior machines)
- Continue appending new events to the same history
- Require no manual operator action beyond the folder move itself

---

### Option A — Job-Embedded Shard (Co-located, Automatic)

#### Concept

Each job folder contains its own SQLite database at `<jobFolder>\.audit\audit.db`. Moving the job folder automatically moves the full audit history.

#### Folder layout

```
c:\job\
  Diced_10.0.4511\
    .audit\
      audit.db          ← full audit history for this job
    S1\
      Recipes\
        R1\
          Recipe.ini
          ...

c:\bis\auditlog\
  global.db             ← global-scope files only (c:\job\status.ini)
  FileClassificationRules.json
```

#### Portability flow

```
Machine A                                   Machine B
─────────────────────────────────────────   ─────────────────────────────────────────
c:\job\Diced_10.0.4511\                     (operator cuts folder)
  .audit\audit.db  ← 1420 rows
  S1\...

                         ── cut & paste ──>

                                            c:\job\Diced_10.0.4511\
                                              .audit\audit.db  ← 1420 rows (preserved)
                                              S1\...

                                            DirectoryWatcher detects new folder
                                            → open existing shard
                                            → CatchUpScanner (scoped to this job)
                                            → resume appending; machine_name = FALCON-02
```

#### Architecture changes

| Component | Change |
|---|---|
| `SqliteRepository.cs` | Parameterise by `dbPath`; each job holds its own instance |
| `ShardRegistry.cs` | **New** — `GetOrCreate(jobName, jobPath)` caches per-job repositories |
| `DirectoryWatcher.cs` | **New** — watches `c:\job\` at depth=1; fires on job folder create/delete |
| `Worker.cs` | Startup: enumerate `c:\job\*`, open/create shard per job; wire `DirectoryWatcher` |
| `FileChangeHandler.cs` | Extract `jobName` from path; route to `ShardRegistry.GetOrCreate(jobName)` |
| `CatchUpScanner.cs` | Add optional `jobPath` scope; run scoped on job arrival |
| `MonitorConfig.cs` | Add `classification_rules_path`; `db_path` = `global.db` path |
| `FileClassifier.cs` | Load rules from JSON (Section 1) |

**Unchanged:** `HashHelper.cs`, `DiffHelper.cs`, `AuditLogEntry.cs`, `FileBaseline.cs`, `audit_log` schema, debounce, WAL mode, `SemaphoreSlim(1)`.

#### Pros / Cons

| | |
|---|---|
| **Pro** | Zero operator action — folder move = complete history move |
| **Pro** | Full history always co-located with job data |
| **Pro** | Simple mental model: one job = one folder = one DB |
| **Con** | `.audit\` folder visible inside job directory |
| **Con** | Multiple open SQLite connections (one per active job) |

---

### Option B — Central DB + Job-Relative Paths + Explicit Export/Import

#### Concept

Retain the single central `audit.db` from the original design. Store all paths in two forms: absolute (for the local machine) and job-relative (for portability). Provide a CLI export command that the operator runs before moving a job; the destination machine auto-imports when it detects the job.

#### Schema additions

```sql
-- Two new columns on audit_log:
ALTER TABLE audit_log ADD COLUMN job_name    TEXT;   -- "Diced_10.0.4511"
ALTER TABLE audit_log ADD COLUMN rel_filepath TEXT;  -- "S1\Recipes\R1\Recipe.ini"
```

#### Export / Import

```
-- Before moving the job from Machine A:
FalconAuditService.exe export --job "Diced_10.0.4511" --out "D:\Diced_10.0.4511_audit.zip"
```

The ZIP contains:
- `audit_shard.db` — all rows for this job, relative paths only
- `export_manifest.json` — source machine, date, row count

```
-- On Machine B (operator drops ZIP into job folder before or after paste):
c:\job\Diced_10.0.4511\_audit_import\Diced_10.0.4511_audit.zip
```

On detecting the new job folder, the service checks for `_audit_import\*.zip`. If found:
1. Reads shard rows from ZIP.
2. Rewrites `filepath` to local absolute path.
3. Bulk-inserts into `audit.db` (skips rows already present by `id`).
4. Moves ZIP to `_audit_import\done\`.

#### Architecture changes

| Component | Change |
|---|---|
| `audit_log` schema | Add `job_name`, `rel_filepath` columns |
| `FileChangeHandler.cs` | Populate `job_name` and `rel_filepath` on every insert |
| `ExportCommand.cs` | **New** — CLI subcommand; queries by `job_name`, writes ZIP |
| `ImportCommand.cs` | **New** — CLI subcommand; reads ZIP, remaps paths, bulk-inserts |
| `DirectoryWatcher.cs` | **New** — triggers import check on new job folder detection |
| `FileClassifier.cs` | Load rules from JSON (Section 1) |

#### Portability flow

```
Machine A                                   Machine B
─────────────────────────────────────────   ─────────────────────────────────────────
audit.db: 1420 rows for Diced_10.0.4511

operator runs export command
  → Diced_10.0.4511_audit.zip

operator cuts job folder + drops ZIP
into _audit_import\                  ──>    c:\job\Diced_10.0.4511\
                                               _audit_import\
                                                 Diced_10.0.4511_audit.zip
                                               S1\...

                                            DirectoryWatcher detects new folder
                                            → ImportCommand reads ZIP
                                            → 1420 rows merged into local audit.db
                                            → CatchUpScanner runs
                                            → live monitoring resumes
```

#### Pros / Cons

| | |
|---|---|
| **Pro** | Single DB file — unchanged from original design |
| **Pro** | Export is an explicit, deliberate action with a clear paper trail |
| **Con** | Operator must remember to export before moving |
| **Con** | If forgotten, history stays on source machine (recoverable, but requires manual intervention) |
| **Con** | Import step must complete before monitoring is considered current |

---

### Option C — Job-Embedded Shard with Custody Manifest (Recommended)

#### Concept

Identical physical layout to Option A (`.audit\audit.db` per job folder), with one addition: a human-readable `manifest.json` alongside the DB records which machines have held this job and when. This adds ~80 lines of code beyond Option A but gives operators and support staff a clear chain-of-custody record they can read in any text editor.

#### Folder layout

```
c:\job\
  Diced_10.0.4511\
    .audit\
      audit.db          ← full audit history (all machines, all time)
      manifest.json     ← human-readable custody chain
    S1\
      Recipes\...

c:\bis\auditlog\
  global.db
  FileClassificationRules.json
```

#### manifest.json structure

```json
{
  "jobName": "Diced_10.0.4511",
  "auditDbVersion": "1",
  "created": {
    "machine": "FALCON-01",
    "at": "2026-03-10T08:00:00Z"
  },
  "history": [
    {
      "machine": "FALCON-01",
      "from": "2026-03-10T08:00:00Z",
      "to":   "2026-04-15T14:00:00Z",
      "events": 1420
    },
    {
      "machine": "FALCON-02",
      "from": "2026-04-15T14:05:00Z",
      "to":   null,
      "events": 38
    }
  ]
}
```

- `to: null` = currently active on this machine.
- `events` = number of `audit_log` rows written by that machine.
- Written atomically (write to `.audit\manifest.tmp`, then rename to `manifest.json`).

#### Portability flow

```
Machine A (FALCON-01)                       Machine B (FALCON-02)
─────────────────────────────────────────   ─────────────────────────────────────────
.audit\audit.db   ← 1420 rows
.audit\manifest.json:
  history[0]: FALCON-01, from=2026-03-10,
              to=null, events=1420

service StopAsync():
  ManifestManager.RecordDeparture()
    → sets history[0].to = now
    → sets history[0].events = 1420

operator cuts folder              ──────>   c:\job\Diced_10.0.4511\
                                              .audit\audit.db  ← 1420 rows
                                              .audit\manifest.json
                                                history[0]: FALCON-01 (closed)

                                            DirectoryWatcher detects new folder
                                            ManifestManager.RecordArrival("FALCON-02")
                                              → appends history[1]: FALCON-02,
                                                from=now, to=null, events=0
                                            CatchUpScanner (scoped to this job)
                                            Live monitoring resumes
                                            New events: machine_name = FALCON-02
                                              → history[1].events incremented
```

#### ManifestManager

```csharp
public class ManifestManager
{
    // Called when service starts and finds an existing job folder
    public void RecordArrival(string jobPath, string machineName);

    // Called when DirectoryWatcher detects an existing job departing
    // (service stop, job folder rename/delete)
    public void RecordDeparture(string jobPath);

    // Read the manifest for display or diagnostics
    public JobManifest? ReadManifest(string jobPath);
}
```

Write path: write to `manifest.tmp` → `File.Move(tmp, manifest.json, overwrite: true)` — atomic on NTFS.

#### Architecture changes

| Component | Change |
|---|---|
| `SqliteRepository.cs` | Parameterise by `dbPath` (same as Option A) |
| `ShardRegistry.cs` | **New** — same as Option A |
| `ManifestManager.cs` | **New** — reads/writes `.audit\manifest.json` |
| `DirectoryWatcher.cs` | **New** — same as Option A; also calls `ManifestManager.RecordArrival()` |
| `Worker.cs` | Startup: enumerate jobs, open shards, `ManifestManager.RecordArrival()` if machine changed |
| `FileChangeHandler.cs` | Route to `ShardRegistry` (same as Option A) |
| `CatchUpScanner.cs` | Scoped per-job (same as Option A) |
| `FileClassifier.cs` | Load rules from JSON (Section 1) |

**Unchanged from original design:** `HashHelper.cs`, `DiffHelper.cs`, `AuditLogEntry.cs`, `FileBaseline.cs`, `audit_log` schema, debounce, WAL mode, `SemaphoreSlim(1)`, content snapshotting, unified diff.

#### Pros / Cons

| | |
|---|---|
| **Pro** | Zero operator action — folder move = complete history move |
| **Pro** | `manifest.json` readable in any text editor — no DB tool needed to see custody chain |
| **Pro** | `machine_name` column in `audit_log` + manifest together answer "who changed what and where" |
| **Con** | `.audit\` folder visible inside job directory |
| **Con** | Multiple open SQLite connections (one per active job) |

---

## Section 3 — Option Scoring

| Criterion | Weight | Option A (Embedded) | Option B (Central + Export) | Option C (Embedded + Manifest) |
|---|---|---|---|---|
| Portability — zero operator action | 35 % | **5** | 2 | **5** |
| History completeness after move | 25 % | **5** | 3 | **5** |
| Chain-of-custody visibility | 20 % | 3 | 4 | **5** |
| Implementation complexity (lower = better) | 20 % | **4** | 3 | 4 |
| **Weighted total** | 100 % | 4.35 | 2.95 | **4.75** |

> Score detail:  
> A = 5×0.35 + 5×0.25 + 3×0.20 + 4×0.20 = 4.35  
> B = 2×0.35 + 3×0.25 + 4×0.20 + 3×0.20 = 2.95  
> C = 5×0.35 + 5×0.25 + 5×0.20 + 4×0.20 = **4.75**

**Recommendation: Option C**

Option C delivers the same automatic portability as Option A and adds a lightweight `manifest.json` that makes the chain of custody human-readable without any database tool. For a forensic audit system on a production line where machines frequently exchange jobs, knowing *which machine wrote which events* and *exactly when a job moved* is as important as the audit events themselves. The only additional code is `ManifestManager.cs` (~80 lines).

---

## Section 4 — Implementation Delta (vs `04_recommended_design.md`)

### New files

| File | Purpose |
|---|---|
| `FileClassificationRules.json` | Configurable rule set (Section 1); pre-populated from `JobConfigurationFileList.json` |
| `ShardRegistry.cs` | Maintains per-job `SqliteRepository` instances; `GetOrCreate(jobName, jobPath)` |
| `DirectoryWatcher.cs` | Monitors `c:\job\` at depth=1 for job folder arrive/remove events |
| `ManifestManager.cs` | Reads/writes `.audit\manifest.json`; atomic write via temp-file rename |

### Modified files

| File | Change |
|---|---|
| `FileClassifier.cs` | `LoadRules(path)` replaces hardcoded table; FSW hot-reload via `ImmutableList` swap |
| `SqliteRepository.cs` | Parameterised by `dbPath` constructor argument |
| `Worker.cs` | Startup enumerates jobs, opens shards, wires `DirectoryWatcher`; calls `ManifestManager.RecordArrival()` |
| `FileChangeHandler.cs` | Extracts `jobName`; routes writes to `ShardRegistry.GetOrCreate(jobName)` |
| `CatchUpScanner.cs` | Adds optional `string? jobPath` scope; `null` = full scan (original behaviour) |
| `MonitorConfig.cs` | Adds `classification_rules_path` key; `db_path` now refers to `global.db` |

### Unchanged

`HashHelper.cs`, `DiffHelper.cs`, `AuditLogEntry.cs`, `FileBaseline.cs`, `audit_log` DDL, `file_baseline` DDL, `install.ps1`, all debounce logic, WAL mode configuration, `SemaphoreSlim(1)` write guard, P1 content snapshotting, unified diff generation.

---

## Section 5 — Verification

| Test | Steps | Expected result |
|---|---|---|
| **JSON hot-reload** | Edit `FileClassificationRules.json` — add a new glob rule for a new filename | Within 2 s, next file event matching that pattern is classified correctly; no service restart |
| **Job portability (live service)** | While service running on Machine B, paste `c:\job\Diced_10.0.4511\` (including `.audit\`) from Machine A | `DirectoryWatcher` fires; shard opened; `manifest.json` updated with Machine B entry; `CatchUpScanner` runs; first new recipe save in shard within 1 second |
| **Job portability (service stopped)** | Stop service on Machine A; paste job folder to Machine B; start service on B | `CatchUpScanner` runs scoped to this job; offline changes detected; `manifest.json` updated |
| **New job (no prior history)** | Copy a new job folder with no `.audit\` subfolder into `c:\job\` | Service creates `.audit\audit.db` and `manifest.json`; first event appears within 1 second |
| **Roles preserved** | Modify `Recipe.ini` (P1) and `DieMapping.dat` (P2) | P1: `old_content`, `new_content`, `diff_text` stored. P2: hash-only row. `ImageProcessing.log` (P4): ignored |
| **Manifest accuracy** | Perform a move between two machines | `manifest.json` shows two history entries with correct machine names, timestamps, and non-zero event counts |
| **Rule precedence** | Create a file matching both a specific and a general glob | First matching rule wins; classification matches the more-specific pattern listed earlier in the JSON |
