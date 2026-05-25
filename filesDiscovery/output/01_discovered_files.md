# 01 — Discovered Files Inventory

> **Scan target:** `c:\job\` (Camtek Falcon AOI/EBI machine, host `AMITA1`)
> **Codebase scanned:** `c:\CamtekGit`
> **Scan date:** 2026-05-17
> **Scope:** Non-binary (text) files only. No classification or solutions — facts only.

Companion data files (machine-readable, in this folder):
- [`file_inventory.csv`](file_inventory.csv) — per-file inventory (3784 rows)
- [`file_role_summary.csv`](file_role_summary.csv) — aggregate by file leaf name (337 rows)
- [`extension_summary.csv`](extension_summary.csv)
- [`change_indicators.csv`](change_indicators.csv) — write-once vs continuously-updated verdict per file role
- [`writer_candidates.txt`](writer_candidates.txt) — 99 code files in `c:\CamtekGit` that reference `c:\job\` or job-path constants
- [`code_to_file_refs.csv`](code_to_file_refs.csv) — code files cross-referenced against inventory leaf names

---

## Section 1 — `c:\job\` File Discovery

### 1.1 Scan parameters

- **Recursion:** All depths under `c:\job\`.
- **Included extensions:** `.txt .ini .json .xml .csv .log .yaml .yml .cfg .dat .seq .md .properties .conf .config .bat .cmd .ps1 .sql`
- **Excluded extensions:** binary/image/compiled (`.exe .dll .tif .bmp .db ...`)
- **Binary heuristic:** first 256 bytes inspected; files with >30 % non-printable bytes treated as binary and dropped.
- **SYSTEM writability:** verified via `Get-Acl` — `NT AUTHORITY\SYSTEM` has **FullControl** on `c:\job\` and inherits to every file. **All inventoried files are writable by SYSTEM.**

### 1.2 Raw counts

| Metric | Value |
|---|---|
| Total files under `c:\job\` (any extension) | 17 050 |
| Files matching include-extension list | 4 364 |
| After binary heuristic | **3 784** |
| Total text-file size | **4 418 749 B (~4 MB)** |
| Top-level subdirectories (job folders) | 24 |
| Shared/global files at `c:\job\` root | 1 (`status.ini`, 75 B) |
| Distinct file leaf names | **337** |

### 1.3 Extension summary

| Extension | File count | Total size (bytes) |
|---|---|---|
| `.ini` | 2 169 | 2 342 048 |
| `.txt` | 1 338 | 1 190 592 |
| `.xml` | 108 | 797 848 |
| `.json` | 66 | 23 583 |
| `.md` | 40 | 24 335 |
| `.csv` | 22 | 5 583 |
| `.dat` | 22 | 34 057 |
| `.log` | 19 | 703 |

Notes:
- `.dat` files vary: many are binary (excluded), but 22 small ones (e.g. `Job.dat`, `WaferInfo.dat`) appear as low-grade text and slipped past the heuristic.
- `.md` files are NOT markdown narrative — they are tiny metadata sidecars (e.g. `s_FrameData.dat.md` is an XML field map, `CurrWaferSurfaceInterpolation.md` is `[General] IsInChuckSpace=1`).

---

## Section 2 — Codebase scan (writers under `c:\job\`)

### 2.1 Method

Searched `c:\CamtekGit` (Camtek monorepo) for files referencing job-tree paths in two complementary ways:

1. **Literal-path regex** `c:\\?job\\` (case-insensitive) → **83 files**
2. **Path-constant identifiers** `JobsRoot | JobsPath | JobsFolder | JobRoot | JobsDirectory` → **181 files**
3. Restricted to source-language files (`.cs .cpp .h .vb .bas .frm`) → **99 deduplicated candidate writer files** ([`writer_candidates.txt`](writer_candidates.txt)).

These 99 candidates were then matched against the 337 distinct leaf names from Section 1. 24 candidate files contain explicit literal mentions of an inventoried file name — the rest construct paths via `Path.Combine(jobRoot, …)` and constants and so are not visible to a literal-name grep. Full mapping in [`code_to_file_refs.csv`](code_to_file_refs.csv).

> **Caveat:** "Touches `c:\job\`" includes both readers and writers. Distinguishing write/update/delete from read requires per-file inspection (Prompt 2 territory). Below is a writer-context grouping by module/project.

### 2.2 Code locations referencing `c:\job\` — by module

| Module / Project | Candidate files |
|---|---|
| `BIS/Sources/machine` | 29 |
| `BIS/Sources/apps` | 10 |
| `BIS/Sources/UI` | 10 |
| `BIS/Sources/ToolManagement` | 10 |
| `BIS/Sources/Utilities` | 9 |
| `BIS/Sources/tests` | 5 |
| `BIS/Sources/TestAutomationAPI` | 5 |
| `BIS/Sources/system` | 4 |
| `BIS/Sources/objects` | 3 |
| `BIS/Sources/dds` | 3 |
| `BIS/lib/x64` | 2 |
| `BIS/Sources/processing` | 1 |
| `BSIHR/Sources/Client` | 1 |
| `BIS/Sources/JobParts` | 1 |
| `BIS/Sources/Grabbing` | 1 |
| `BIS/Sources/Automation.Mng` | 1 |
| `BIS/Sources/Scripts` | 1 |
| `CamtekSoftwareSolutions/rms/Camtek.RMS.Service` | 1 |
| Other (single-file groups, lib headers) | 2 |

### 2.3 Highlighted writer code paths

Cross-referencing candidates against `system.md` ownership:

| Service / App | Key files that touch `c:\job\` |
|---|---|
| **Falcon.Net (AOI_Main)** | `apps/Falcon.Net/Forms/frmJobTab.cs`, `frmScanTab.cs`, `frmProduction.cs`, `frmNewJob.cs`, `frmSetupNew.cs`, `MainContext/MainContextModule.cs`, `Modules/RecipeModelManager.cs`, `Modules/EFEMModule.cs`, `Modules/modJobHistory.cs`, `Modules/FalconAudit/FalconAuditClient.cs`, `Classes/clsInitAOI.cs` |
| **FalconAuditService** (.NET 8 Windows Service) | `Utilities/FalconAuditService/Program.cs`, `AuditEventQueue.cs`, `FileClassificationRules.json`, `appsettings.json` — writes `{jobPath}\.audit\audit.db` + `manifest.json` |
| **Job / Setup (C++/CLI)** | `objects/Job/Program.cpp`, `objects/Job/SetupData.cpp`, `objects/Job.NET/SetupCreator.cs`, `objects/Job.NET/MultiProductCreator.cs` |
| **JobTemplates (new-job wizard)** | `system/Camtek.JobTemplates/JobTemplates/ViewModels/NewJob/NewJobViewModel.cs`, `Common/NewJobDataLoader.cs`, `RecipeDataVm.cs`, `JobExistsChecker.cs` |
| **DataAccess / DataLayer** | `objects/DataAccess/DataLayer/Implementations/PathsHandler.cs` — central path-resolution |
| **DDS (Distributed Defect System)** | `dds/ProcessingServer/Reference/ReferenceImporter.cs`, `dds/ProcessingServer/NetworkPacketsHandlers/StartPacketHandler.cs`, `dds/DdsIPC/DdsIPC.cs`, `dds/ProcessingObjects/Signatures/ReferenceSig.cs` |
| **Tool Management — SECS/GEM** | `ToolManagement/SecsGemClient/*.cpp/h`, `ToolManagement/SecsGemObjects/Rms/SecsGemRms.cs`, `Clients/ProcessProgramManager/ProcessProgramManager.cs`, `Stream7Function6Reply.cs` |
| **Tool Management — JobProvider / TAC** | `ToolManagement/JobProvider/JobProvider.cs`, `S21Server/S21Server.cs`, `Utils/JobLocker.cs`, `ToolManagement/NetTAC/SonyTac/SonyTacRmsHandler.cs` |
| **VCamInstaller / VCamDataDeployer** | `Utilities/VCamInstaller/.../VCamDataDeployer.cs`, `VCamDataCollector.cs`, `FileSystemSupport.cs`, `MainViewModel.cs`, plus standalone `Utilities/VCamDataDeployer/*.cs` |
| **Automation.Mng** | `Automation.Mng/AutomationManager/Batches/Recorder/ProductionRecorder.cs`, `EFEM/AutoLoaderWrapper.cs`, `BatchControl/BatchController.cs`, `WaferLoader/Production/ProductionScenario/ProductionStandaloneScenario.cs` |
| **CamtekSoftwareSolutions/rms** (RMS Service) | `rms/Camtek.RMS.Service/Services/Helpers/FileOperationsHelper.cs`, `Services/Handlers/UploadToServerHandler.cs` |
| **DataServer** | `dataserver/Modules/Camtek.ScanResults/Service/ScanResultsInternalService.cs`, `Common/DataServer/DAL/WaferScanResultFSContext.cs`, `Infrastructure/Base/StringParser.cs` |
| **BSIHR** | `BSIHR/Sources/Client/BSIHR.Client/ServerRecipeManager.cs`, `BSIHR.Common/System/ConfigurationService.cs`, `DataContext/BSIHR.DataServices/Services/Implementation/RecipeDbService.cs` |
| **UI (Falcon)** | `UI/Falcon/Camtek.UI.Falcon.BL/Services/JobHistory/*.cs`, `UI/Falcon/Services/Scan/Services/Services/RobotService.cs`, `UI/Falcon/Controls/.../WaferMapRecipeViewModel.cs` |
| **Tests / TestAutomationAPI** | `TestAutomationAPI/TestAutomationSDK/RecipePathInfo.cs`, `TestAutomationSDK/Deterministic/.../JobReference.cs`, `BaselineMigrationApp/MigratorV0V1.cs`, plus various unit-test fixtures |

The full candidate list is in [`writer_candidates.txt`](writer_candidates.txt). Per-file mapping of which inventory leaf names each candidate mentions is in [`code_to_file_refs.csv`](code_to_file_refs.csv).

---

## Section 3 — Directory structure

### 3.1 Top-level layout

```
c:\job\
├── status.ini                                  (75 bytes, shared/global machine state)
├── Diced_10.0.4511/   (text files: 125)
├── MPW_From_cad/   (text files: 559)
├── OVL 1 frame/   (text files: 65)
├── ScanAreaOnly/   (text files: 34)
├── Secgemgrey_78231/   (text files: 61)
├── ValidationJob/   (text files: 80)
├── VCAM 2D+3D CCS/   (text files: 176)
├── VCAM 2D+3D CTS/   (text files: 177)
├── VCAM AF before alignment/   (text files: 130)
├── VCAM CTS FM Auto/   (text files: 73)
├── VCAM EBRScan10.0/   (text files: 61)
├── VCAM GrabColor/   (text files: 169)
├── VCAM GrabDefectiveDie/   (text files: 169)
├── VCAM MPW_x5/   (text files: 377)
├── VCAM Scan All/   (text files: 169)
├── VCAM Scan And Grab/   (text files: 169)
├── VCAM Scan defects only/   (text files: 169)
├── VCAM ScanAll And Grab/   (text files: 169)
├── VCAM TNE 10.0/   (text files: 57)
├── VCAM with CCS FM/   (text files: 70)
├── VCAM with FM/   (text files: 70)
├── VCAM WLUP + FM + CR + Grab/   (text files: 299)
├── VCAM x20 Scan Area/   (text files: 169)
└── Vcam_3MR_B4587/   (text files: 186)
```

### 3.2 Per-job subdirectory naming

- **Yes**, every top-level subdirectory under `c:\job\` is a **per-job folder**. The folder name *is* the job name (matches the `name=` field inside that job's top-level `Metadata.ini`).
- Naming pattern is free-form (mix of letters, digits, spaces, `+`, `_`, `.`); examples: `Diced_10.0.4511`, `VCAM 2D+3D CTS`, `Vcam_3MR_B4587`, `MPW_From_cad`. No machine-generated GUID prefix on the folder; the GUID lives inside `Metadata.ini`.
- Standard internal layout per job:

```
<jobName>/
├── .audit/                       ← FalconAuditService scratch
│   └── manifest.json             (24/24 jobs have this — 100 %)
├── Metadata.ini                  ← top-level job identity (name, GUID, version, tag)
└── <SetupFolder>/                ← typical names: S1, S2, Setup1, "300mm", etc.
    ├── Metadata.ini              ← setup-level identity
    ├── MultiRecipe.ini, DefectsClustering.ini, ScanCondition.ini, ...
    ├── DefaultWafer2Table.ini, Wafer2Table.ini, ProductionInfo.ini, ...
    └── Recipes/
        └── <RecipeName>/         ← e.g. R1, R2, x5, AllMags, Repeatability, TPT
            ├── Recipe.ini, ProductInfo.ini, Waferinfo.ini, ...
            ├── Alignment.ini, AlignRtp.ini, GlobalRTP.ini, RTP.txt, ...
            ├── zones.ini, zones.txt, ZoomLevels.ini, OpticPreset.ini, ...
            ├── Zones/             ← per-zone .ini files (Scan Area.ini, PostProcess.ini, ...)
            ├── WaferAlignData/    ← alignment runtime outputs (Alignment_Stat.txt, ...)
            ├── TrainData/         ← training fixtures (Die.ini, DieImage/, FrameToChuck.ini, ...)
            ├── FocusMapping/      ← FocusMapping.ini, DieReferenceLocation.json
            ├── OpticLightMetadata/, ReferenceBackup/, .dc_cache/, SW_QA-*/, ...
            └── (~50–60 text files per recipe)
```

Per-job structural presence:

| Feature                              | Jobs (of 24) |
|---|---|
| Has `.audit/` directory              | 24 (100 %) |
| Has top-level `Metadata.ini`         | 24 (100 %) |
| Has `S1`/`S2`/`Setup1` setup folder  | 16 (the rest use `300mm` or no setup folder) |

### 3.3 Shared/global files at `c:\job\` root

Only one: **`c:\job\status.ini`** (75 B). Current content:

```ini
[UC_PROGRAM]
ProgramName=300mm
ProductName=ValidationJob
RecipeName=x5
```

This is the machine's current-program-state singleton (per the `FileClassificationRules.json` rule: *"Global machine-state singleton updated continuously by Falcon.Net on every program/state transition; records the active job, product, and recipe."*).

### 3.4 Representative full job tree

Single job `Diced_10.0.4511` (125 text files across `.audit/` + `S1/` + `S1/Recipes/R1/` + `S1/Recipes/R2/`):

See [`_tree.md`](_tree.md) for the full rendered tree; selected excerpt:

```
c:\job\Diced_10.0.4511\
├── .audit/
│   └── manifest.json  (398 B, mod 2026-05-12 12:57)
├── Metadata.ini  (94 B, mod 2026-03-03 15:44)
└── S1/
    ├── DefaultWafer2Table.ini  (1134 B, mod 2026-03-03 14:10)
    ├── DefectsClustering.ini  (236 B, mod 2026-04-26 21:37)
    ├── DieAlignment.dat_block.ini  (119 B, mod 2026-03-03 19:03)
    ├── Metadata.ini  (93 B, mod 2026-03-03 19:03)
    ├── MultiRecipe.ini  (335 B, mod 2026-04-27 16:29)
    ├── ProductionInfo.ini  (155 B, mod 2026-04-27 16:51)
    ├── ScanCondition.ini  (23 B, mod ...)
    ├── Wafer2Table.ini  (1134 B, mod 2026-03-03 14:10)
    └── Recipes/
        ├── R1/   (≈55 files: Recipe.ini, ProductInfo.ini, GlobalRTP.ini, RTP.txt, AlignRtp.ini, …)
        │   ├── FocusMapping/, OpticLightMetadata/, ReferenceBackup/, SW_QA-5/, TrainData/,
        │   ├── WaferAlignData/, Zones/, .dc_cache/
        │   └── (full list in _tree.md)
        └── R2/   (≈55 files, mirror of R1)
```

### 3.5 Per-job text-file counts

| Job Folder | Created | Last Modified | Text Files |
|---|---|---|---|
| `MPW_From_cad` | 2026-05-17 11:11 | 2026-05-17 11:11 | 559 |
| `VCAM MPW_x5` | 2026-05-17 11:15 | 2026-05-17 11:15 | 377 |
| `VCAM WLUP + FM + CR + Grab` | 2026-05-17 11:17 | 2026-05-17 11:17 | 299 |
| `Vcam_3MR_B4587` | 2026-05-17 11:10 | 2026-05-17 11:10 | 186 |
| `VCAM 2D+3D CTS` | 2026-05-17 11:19 | 2026-05-17 11:19 | 177 |
| `VCAM 2D+3D CCS` | 2026-05-17 11:18 | 2026-05-17 11:18 | 176 |
| `VCAM ScanAll And Grab` | 2026-05-17 11:17 | 2026-05-17 11:17 | 169 |
| `VCAM Scan defects only` | 2026-05-17 11:17 | 2026-05-17 11:17 | 169 |
| `VCAM Scan And Grab` | 2026-05-17 11:17 | 2026-05-17 11:17 | 169 |
| `VCAM Scan All` | 2026-05-17 11:17 | 2026-05-17 11:17 | 169 |
| `VCAM GrabDefectiveDie` | 2026-05-17 11:15 | 2026-05-17 11:15 | 169 |
| `VCAM GrabColor` | 2026-05-17 11:15 | 2026-05-17 11:15 | 169 |
| `VCAM x20 Scan Area` | 2026-05-17 11:18 | 2026-05-17 11:18 | 169 |
| `VCAM AF before alignment` | 2026-05-17 11:19 | 2026-05-17 11:19 | 130 |
| `Diced_10.0.4511` | 2026-03-15 14:31 | 2026-05-12 09:20 | 125 |
| `ValidationJob` | 2026-04-30 22:08 | 2026-05-12 09:20 | 80 |
| `VCAM CTS FM Auto` | 2026-05-17 11:19 | 2026-05-17 11:19 | 73 |
| `VCAM with CCS FM` | 2026-05-17 11:17 | 2026-05-17 11:17 | 70 |
| `VCAM with FM` | 2026-05-17 11:17 | 2026-05-17 11:17 | 70 |
| `OVL 1 frame` | 2026-04-30 10:16 | 2026-05-12 09:20 | 65 |
| `VCAM EBRScan10.0` | 2026-05-17 11:15 | 2026-05-17 11:15 | 61 |
| `Secgemgrey_78231` | 2026-05-17 11:09 | 2026-05-17 11:09 | 61 |
| `VCAM TNE 10.0` | 2026-05-17 11:17 | 2026-05-17 11:17 | 57 |
| `ScanAreaOnly` | 2026-03-11 16:21 | 2026-05-12 09:20 | 34 |

> Note: Most `VCAM *` jobs share the same CreationTime down to the second (≈ 2026-05-17 11:09–11:19). This is consistent with a recent bulk job-installer run (VCamInstaller / VCamDataDeployer) on the day of the scan.

---

## Section 4 — Change indicators

### 4.1 Write-lock probe

`handle.exe` / Process Monitor were **not invoked** in this scan (would require an admin elevation and is intrusive). Open file handles are **unknown** for this report — flagged "unknown" in `file_inventory.csv`. The FalconAuditService FSW watch should provide the equivalent telemetry without a probe.

### 4.2 Heuristic: `LastModified − Created > 10 min` ⇒ continuously updated

The current cohort of jobs was *just* deployed today (2026-05-17 ~11 am) by the bulk installer, so for the VCAM-prefixed jobs `LastModified − Created` is typically zero or negative (negative happens when `CreationTime` is the local copy timestamp while `LastWriteTime` is the older source mtime preserved during deploy). The heuristic is therefore most informative on **older jobs that have actually run** — `Diced_10.0.4511`, `ValidationJob`, `OVL 1 frame`, `ScanAreaOnly`.

### 4.3 Continuously updated file roles (any file with Δt > 10 min, sorted by max Δt)

| File leaf | Count in inventory | Cont.-updated copies | Max Δt (min) | Verdict |
|---|---|---|---|---|
| `status.ini`             | 1  | 1 | 181 675 | **Continuously updated** (the global machine state file) |
| `ProductInfo.ini`        | 42 | 4 |  89 082 | Mixed |
| `Wafer2Table.ini`        | 49 | 2 |  83 419 | Mixed |
| `JobIllumLimits.ini`     | 27 | 1 |  82 124 | Mixed |
| `ProductionInfo.ini`     | 22 | 1 |  62 060 | Mixed |
| `MultiRecipe.ini`        | 25 | 1 |  62 038 | Mixed |
| `DefectsClustering.ini`  | 25 | 1 |  60 906 | Mixed |
| `OpticsPreset.ini`       | 50 | 2 |  37 558 | Mixed |
| `Waferinfo.ini`          | 42 | 4 |  33 254 | Mixed |
| `Alignment.ini`          | 42 | 1 |  31 950 | Mixed |
| `Recipe.ini`             | 42 | 3 |  31 947 | Mixed |
| `Metadata.ini`           | 86 | 2 |  17 214 | Mixed (top-level + setup-level) |
| `DieAlignment.dat_block.ini` | 57 | 1 | 16 636 | Mixed |
| `config.ini`             | 40 | 1 |  16 635 | Mixed |
| `ScanOverlapLog.txt`     | 41 | 1 |  16 635 | Mixed |
| `AlignRtp.ini`           | 42 | 1 |  16 635 | Mixed |
| `OpticPreset.ini`        | 41 | 2 |  16 145 | Mixed |
| `WaferMapRecipe.ini`     | 44 | 2 |  16 144 | Mixed |
| `OpticToVCamStorage.json`| 25 | 1 |   6 869 | Mixed |
| `ScenariosMetadatas.ini` | 41 | 1 |   6 869 | Mixed |
| `Pad.ini`                | 1  | 1 |   3 833 | **Continuously updated** |
| `FocusMapping.ini`       | 27 | 1 |      29 | Mixed (single barely-updated copy) |

Full per-role verdict in [`change_indicators.csv`](change_indicators.csv).

### 4.4 Writer hints from file path / content

- **`.audit/manifest.json`** — owned by **FalconAuditService** (the only service that creates the `.audit/` folder). Content example: `{ "jobName": "...", "auditDbVersion": "1", "created": { "machine": "AMITA1", ... } }`.
- **`status.ini`** at `c:\job\` root — owned by **Falcon.Net** (per project's `FileClassificationRules.json` rule and `system.md`).
- **`Metadata.ini`** at job-root and setup-root levels — owned by **RMS** (creates the GUID/version envelope on new-job/new-setup creation).
- **`Recipe.ini`, `ProductionInfo.ini`, `MultiRecipe.ini`, `DefectsClustering.ini`, `GlobalRTP.ini`, `RTP.txt`, `ZoomLevels.ini`, `zones.ini`, …** — owned by **RMS** per `FileClassificationRules.json`.
- **`Waferinfo.ini`, `ProductInfo.ini`, `Wafer2Table.ini`, `AlignmentData.ini`** — owned by **AOI_Main (Falcon.Net)** per `FileClassificationRules.json`.
- **`OpticToVCamStorage.json`, `OpticsPreset.ini`, `OpticLightMetadata/config.ini`** — written by VCamInstaller / VCam pipeline (matches `Utilities/VCamInstaller/InstallData/VCamDataDeployer.cs` candidate hit).
- **`Alignment_Stat.txt`, `Alignment_PatRes.txt`, `Alignment_PatFindRtp.txt`, `AlignmentStatisticsTime.txt`** in `WaferAlignData/` — alignment runtime logs, appended by AOI_Main during alignment cycles.
- **`Job.dat`, `WaferInfo.dat`** — partly-binary recipe blobs; written by the C++ Job/SetupData layer (`objects/Job/SetupData.cpp`).
- **Filename prefix hints observed:** `Params_*` (RMS-derived parameter sets), `Default*` (initial templates copied at recipe creation), `Wafer2Table_LastKnown.ini` (snapshot of last alignment), `s_FrameData.dat.md` (field-map sidecar for binary `.dat`).

### 4.5 Write-once vs runtime, by inference

Combining the timestamp-delta and the inventory layout:

| Class | Examples | Heuristic justification |
|---|---|---|
| **Write-once at job/setup/recipe creation** | `WaferDataReadSettings.xml`, `ExternalCoordSystems.ini`, `CreateReference3dOptions.ini`, `UniqueArea.ini`, `Die.ini`, `ZoomLevels.ini`, `Params_AlignRTP.ini`, `Params_SystemInfo.ini`, `Params_WaferInfo.ini`, `DefaultWafer2Table.ini`, `CleanReferenceFinalParams.ini`, `Scan Area.ini`, `PostProcess.ini` | Δt ≤ 0 across every job |
| **Updated each run / continuously** | `status.ini` (global), `Wafer2Table.ini`, `ProductionInfo.ini`, `ProductInfo.ini`, `Recipe.ini`, `JobIllumLimits.ini`, `OpticPreset.ini`, `OpticsPreset.ini`, `WaferMapRecipe.ini`, `ScanOverlapLog.txt`, `Alignment*.txt` (in `WaferAlignData/`), `OpticToVCamStorage.json` | At least one copy with Δt > 10 min (often > 1 day) |
| **Newly-written on each scan (audit/manifest)** | `.audit/manifest.json` | Δt ≈ 1 min (created and finalized together at job-arrival), but rewritten by ManifestManager on every arrive/depart event per project context |

> The 18 jobs created today (2026-05-17 ~11 am) have not yet run, so their "Mixed" / "Continuously updated" classification will sharpen once they accumulate runtime updates. The four older jobs (`Diced_10.0.4511`, `ValidationJob`, `OVL 1 frame`, `ScanAreaOnly`) carry most of the runtime-update signal in this snapshot.

---

## Section 5 — Aggregate file role table (top 60 by recurrence)

Sorted by count (number of jobs in which the file appears). Full 337-row table in [`file_role_summary.csv`](file_role_summary.csv).

| # | File Name | Ext | Count | Min Size | Max Size | Cont? | Sample Path | First 120 chars |
|---|---|---|---|---|---|---|---|---|
| 1 | `ZoomLevels.ini` | .ini | 360 | 77 | 361 | 0 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\ZoomLevels.ini` | `[ZOOM_LEVELS] LEVEL_1=1 SaveCompressed=0 LEVEL_2=1 LEVEL_4=1 LEVEL_8=1 LEVEL_16=1 LEVEL_32=1 [LEVEL_...` |
| 2 | `zones.ini` | .ini | 159 | 23 | 632 | 0 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\zones.ini` | `[General] Version=11 [ALGName] 255=PostProcess 0=Scan Area [ALG] 255=PostProcess 0=Surface [BinCodes...` |
| 3 | `zones.txt` | .txt | 153 | 170 | 530 | 0 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\zones.txt` | `ScaleX =    1.0000000000; ScaleY =    1.0000000000   RefID       X        Y  Zone Count      Shape D...` |
| 4 | `DieRegPos.txt` | .txt | 124 | 110 | 470 | 0 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\DieRegPos.txt` | `ScaleX =    1.0000000000; ScaleY =    1.0000000000   RefID       X        Y  Zone Count      Shape D...` |
| 5 | `Metadata.ini` | .ini | 86 | 32 | 158 | 2/86 | `C:\job\Diced_10.0.4511\Metadata.ini` | `[General] name=Diced_10.0.4511 Id=46a4c3bf-4464-4070-b41b-4939a9842d63 Version=1 JobTag=` |
| 6 | `AlignmentData.ini` | .ini | 76 | 31 | 1018 | 0 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\AlignmentData.ini` | `[General] MinScore=85 MinTargetScore=0 IsAffineEnabled=1 AlignPointsNum=8 ForceToGMF=0 [Point_0] X=1...` |
| 7 | `DieMapRegPos.txt` | .txt | 71 | 110 | 350 | 0 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\DieMapRegPos.txt` | `ScaleX =    1.0000000000; ScaleY =    1.0000000000   RefID       X        Y  Zone Count      Shape D...` |
| 8 | `zonesInCad.txt` | .txt | 58 | 0 | 0 | 0 | `C:\job\MPW_From_cad\300mm\Recipes\x5\Die_1\zonesInCad.txt` | *(empty)* |
| 9 | `DieRegPosInCad.txt` | .txt | 58 | 0 | 0 | 0 | `C:\job\MPW_From_cad\300mm\Recipes\x5\Die_1\DieRegPosInCad.txt` | *(empty)* |
| 10 | `DieAlignment.dat_block.ini` | .ini | 57 | 116 | 6942 | 1/57 | `C:\job\Diced_10.0.4511\S1\DieAlignment.dat_block.ini` | `[General] IsMultiIndex=0 DieCount=1 [Die_0] Row=0 Col=0 PosX=0.000 PosY=0.000 SizeX=4117.000 SizeY=1...` |
| 11 | `OpticsPreset.ini` | .ini | 50 | 36 | 926 | 2/50 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\SW_QA-5\OpticsPreset.ini` | `[RobotSetup] Name=PD_8_106448_ARC.cfg [Chuck] CenterX=158684.0625 CenterY=138935.140625 CenterZ=-377...` |
| 12 | `Wafer2Table.ini` | .ini | 49 | 503 | 1290 | 2/49 | `C:\job\Diced_10.0.4511\S1\Wafer2Table.ini` | `[WAFER ALIGNMENT] Wafer2Table_X=          0.9999944815        -0.0062677412    106354.8850214472 Waf...` |
| 13 | `DefaultWafer2Table.ini` | .ini | 49 | 870 | 1290 | 0 | `C:\job\Diced_10.0.4511\S1\DefaultWafer2Table.ini` | `[WAFER ALIGNMENT] Wafer2Table_X=          0.9999922204        -0.0062656224    106355.7066746282 Waf...` |
| 14 | `WaferMapRecipe.ini` | .ini | 44 | 28 | 1608 | 2/44 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\WaferMapRecipe.ini` | `[GENERAL] SettingsPolicy=0 FiducialBin=-1 MappingCorner=0 [Input_Update] Enable=0 FileMask= ImportDi...` |
| 15 | `WaferDataReadSettings.xml` | .xml | 42 | 195 | 195 | 0 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\WaferDataReadSettings.xml` | `<?xml version="1.0"?> <WaferDataReadSettings xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" x...` |
| 16 | `Recipe.ini` | .ini | 42 | 303 | 4975 | 3/42 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\Recipe.ini` | `[AutoCycle] AutoFocusBeforeAlignment=0 AutoFocusEvery=1 ; None CleanReferenceEvery=3 ; Wafer UnloadT...` |
| 17 | `Alignment.ini` | .ini | 42 | 38 | 404 | 1/42 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\Alignment.ini` | `[WAFER ALIGNMENT] CoarseAlignDone=0 Rotate  w2t=0` |
| 18 | `Waferinfo.ini` | .ini | 42 | 219 | 725 | 4/42 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\Waferinfo.ini` | `[Robot] Rotation=90 SideUp=1 [General] key_minarea=0 hd_cmm=False hd_alignment=True hd_scan=True hd_...` |
| 19 | `AlignRtp.ini` | .ini | 42 | 1092 | 2985 | 1/42 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\AlignRtp.ini` | `[DIE Alignment] Die__MinModelSize=64 Die__MinScore=65 Die__ExtraScanLength_um=0.000000 Die__ExtraSca...` |
| 20 | `UniqueArea.ini` | .ini | 42 | 20 | 1121 | 0 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\UniqueArea.ini` | `[General] Count=0 UseGMF=0 TryAllModels=0 MinTargetScore=0 ImagePreprocessorId=306a7e84-3bae-4443-be...` |
| 21 | `CreateReference3dOptions.ini` | .ini | 42 | 307 | 415 | 0 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\CreateReference3dOptions.ini` | `[General] Version=0 Debug=0 [Registration] ReferenceRegBy=0 BigBlockRowsNumber=1000 YPartitionsNumbe...` |
| 22 | `ProductInfo.ini` | .ini | 42 | 2363 | 5618 | 4/42 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\ProductInfo.ini` | `[AutoFocus] SearchUseDefault=1 ScanDelta=0 OnLimitViolationAction=0 FocusVAriationLimit=0 LimitFocus...` |
| 23 | `ExternalCoordSystems.ini` | .ini | 42 | 33 | 33 | 0 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\ExternalCoordSystems.ini` | `[ExternalCoordSystems] Count=0` |
| 24 | `Die.ini` | .ini | 42 | 764 | 1809 | 0 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\TrainData\Die.ini` | `[DIE] PixelSize_X=0.857650914714166 PixelSize_Y=0.857650914714166 FrameSizeX=1280 FrameSizeY=1280 St...` |
| 25 | `PostProcess.ini` | .ini | 42 | 1347 | 4355 | 0 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\Zones\PostProcess.ini` | `[General] ZoneName=PostProcess ZoneID=255 TypeName=PostProcess AutoTH=0 AutoProbe=0 [Warp Direction ...` |
| 26 | `CleanReferenceFinalParams.ini` | .ini | 41 | 950 | 1106 | 0 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\CleanReferenceFinalParams.ini` | `[General] Version=2 [CleanRefParams] SizeX=1280 SizeY=1280 minCriteria=-2 maxCriteria=-1 minCreateMu...` |
| 27 | `ScenariosMetadatas.ini` | .ini | 41 | 261 | 1579 | 1/41 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\ScenariosMetadatas.ini` | `[General] Version=1 [Scan2d] Id=d65783d2-b589-4005-a387-47cd3b398afa OpticsId=fa520c77-45ae-48dd-aff...` |
| 28 | `ScanOverlapLog.txt` | .txt | 41 | 450 | 450 | 1/41 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\ScanOverlapLog.txt` | `Scan 2d Overlap [px], [um]; Pixel size = 0.858, 0.86 Minimum             :    32,   32,   27,   27 D...` |
| 29 | `OpticPreset.ini` | .ini | 41 | 578 | 5004 | 2/41 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\OpticPreset.ini` | `[IllumConversion] Executed=0 [General] Signature=1 [Scan2d] Id=d5ecdfdf-532b-4995-96f7-3d14b6fa5c0c ...` |
| 30 | `TransactionsHistory.ini` | .ini | 41 | 120 | 462 | 0 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\.dc_cache\TransactionsHistory.ini` | `[DeprecatedInV0] Namespace.RuntimeEntities.RecipePartsCollection_Runtime\|RecipePartsCollection\|Cle...` |
| 31 | `AlignmentStatisticsTime.txt` | .txt | 40 | 164 | 171 | 0 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\WaferAlignData\AlignmentStatisticsTime.txt` | `Alignment Statistics Grabbing Counter=10 Total=374 Average=37.400002 Moving Counter=10 Total=1483 Av...` |
| 32 | `config.ini` | .ini | 40 | 255 | 1941 | 1/40 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\OpticLightMetadata\config.ini` | `[General] NominalDelta=0.000000000 Exist=0 targetPos_X=0.000000000 targetPos_Y=0.000000000 minY=0.00...` |
| 33 | `Alignment_Stat.txt` | .txt | 40 | 426 | 1574 | 0 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\WaferAlignData\Alignment_Stat.txt` | `[16:46:12 04/07/26] ==============================================================================...` |
| 34 | `Alignment_PatRes.txt` | .txt | 40 | 1175 | 2243 | 0 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\WaferAlignData\Alignment_PatRes.txt` | `[16:46:12 04/07/26] ===============================================================================...` |
| 35 | `Alignment_PatFindRtp.txt` | .txt | 40 | 485 | 742 | 0 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\WaferAlignData\Alignment_PatFindRtp.txt` | `[16:46:12 04/07/26] Minimum Score: 85, Minimum Start: 90, Reduce Delta ...` |
| 36 | `GlobalRTP.ini` | .ini | 39 | 1678 | 3205 | 0 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\GlobalRTP.ini` | `[GLOBAL_RTP] LSL_Height=100.000000 USL_Height=140.000000 NominalHeight=120.000000 BumpType=1 CCS_Ste...` |
| 37 | `RTP.txt` | .txt | 39 | 4836 | 18355 | 0 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\RTP.txt` | `[PostProcess] ; Zone name Alg = Warp_Direction_Calculation Inner_Radius_[Microns] = 1 ; (Radius ...` |
| 38 | `Scan Area.ini` | .ini | 38 | 3811 | 5848 | 0 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\Zones\Scan Area.ini` | `[General] ZoneName=Scan Area ZoneID=63 TypeName=Surface AutoTH=0 AutoProbe=0 [Surface] Enable=1 Algo...` |
| 39 | `DieImageToTable.ini` | .ini | 37 | 144 | 302 | 0 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\TrainData\DieImage\DieImageToTable.ini` | `[DieImageToTable] I2T_X=0.857636017024815 0.00505507782893001 -2571.21898480672 I2T_Y=-0.00505507782...` |
| 40 | `DieRefToTrain.txt` | .txt | 37 | 155 | 298 | 0 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\TrainData\DieRefToTrain.txt` | `[DieRefToTrainImage] DR_2_TI_X=1 0 1114.36704139293 DR_2_TI_Y=0 1 1120.96597481518 DR_2_TI_Theta=0.0...` |
| 41 | `FrameToChuck.ini` | .ini | 37 | 160 | 160 | 0 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\TrainData\FrameToChuck.ini` | `[FrameToChuck] F2C_X=          0.8576508809         0.0002406744      -549.0505954039 F2C_Y= ...` |
| 42 | `FocusMapping.ini` | .ini | 27 | 1864 | 2562 | 1/27 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\FocusMapping\FocusMapping.ini` | `[Mode] AutoFocusRange=100 CreateSurfaceScore=Good MinMatchModelScore=Good DurationOfOneSite=3 NumOfD...` |
| 43 | `JobIllumLimits.ini` | .ini | 27 | 84 | 182 | 1/27 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\JobIllumLimits.ini` | `[AMITA1] IllumCalibDate=2024-12-15 07:59:23 ForcedSkipToDate=2026-06-10 15:20:51` |
| 44 | `DefectsClustering.ini` | .ini | 25 | 236 | 239 | 1/25 | `C:\job\Diced_10.0.4511\S1\DefectsClustering.ini` | `[General] Enabled=0 ClusteringAfterMerge=0 Distance=0 SelectedFirstSortingList=Priority SelectedSeco...` |
| 45 | `MultiRecipe.ini` | .ini | 25 | 121 | 453 | 1/25 | `C:\job\Diced_10.0.4511\S1\MultiRecipe.ini` | `[Scan] MergingThreadCount=1 doSeparateRecipe=0 DoGrabAfterMerge=0 Recipes=0,1, FieldCount=8 RecipesP...` |
| 46 | `OpticToVCamStorage.json` | .json | 25 | 184 | 901 | 1/25 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\OpticToVCamStorage.json` | `[ { "OpticId": "fa520c77-45ae-48dd-afff-47b8caf409a6", "Scenario": "Scan", "IsAutoFocu...` |
| 47 | `manifest.json` | .json | 24 | 391 | 434 | 0 | `C:\job\Diced_10.0.4511\.audit\manifest.json` | `{ "jobName": "Diced_10.0.4511", "auditDbVersion": "1", "created": { "machine": "AMITA1", ...` |
| 48 | `ProductionInfo.ini` | .ini | 22 | 61 | 197 | 1/22 | `C:\job\Diced_10.0.4511\S1\ProductionInfo.ini` | `[General] WaferDefectsCount=0 WaferDefectDiceRatio=0.0000 WaferDefectClassId=0 WaferDefectClassIdCou...` |
| 49 | `ZonesVectorInfo.csv` | .csv | 21 | 46 | 464 | 0 | `C:\job\ValidationJob\300mm\Recipes\AllMags\TrainData\ZonesVectorInfo.csv` | `ID,zone,X,Y,SizeX,SizeY,ShapeType,Angle,Is3d 1,Solder,1931.55318906358,3462.82860085208,3842.6666618...` |
| 50 | `ScenarioMetadataGrab.xml` | .xml | 20 | 1117 | 5679 | 0 | `C:\job\ValidationJob\300mm\Recipes\AllMags\ScenarioMetadataGrab.xml` | `<MultiOpticsScenarioGrabConfiguration VersionNumber="2.0">   <ScenarioGrab Id="8762b93a-7d27-4b01-ae...` |
| 51 | `Params_AlignRTP.ini` | .ini | 19 | 2558 | 2982 | 0 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\Params_AlignRTP.ini` | `[DIE Alignment] Die__MinModelSize=0 Die__MinScore=50 Die__ExtraScanLength_um=0.000000 Die__ExtraScan...` |
| 52 | `CcsLocalMeas.ini` | .ini | 19 | 153 | 210 | 0 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\CcsLocalMeas.ini` | `[ScanParams] MarginX=0 MarginY=0 ScanStepX=0 ScanStepY=0 ResultsToRevisit= ScanDefects_Enabled=0 Sca...` |
| 53 | `Params_SystemInfo.ini` | .ini | 19 | 2191 | 2395 | 0 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\Params_SystemInfo.ini` | `[SystemParams] Gain=0 GainFx=0 Offset=0 Distortion=0 SizeX=1280 SizeY=1280 OffsetX=384 OffsetY=384 C...` |
| 54 | `Params_WaferInfo.ini` | .ini | 19 | 1339 | 1968 | 0 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\Params_WaferInfo.ini` | `[Path] SetupPath=c:\job\Diced_10.0.4511\S1\Recipes\R1\ RecipeName=R1 RecipePath=c:\job\Diced_10.0.45...` |
| 55 | `Job.dat` | .dat | 19 | 577 | 1864 | 0 | `C:\job\ScanAreaOnly\Setup1\Recipes\Default\Job.dat` | *(low-grade text — Camtek binary recipe blob)* |
| 56 | `ImageProcessing.log` | .log | 19 | 37 | 37 | 0 | `C:\job\Diced_10.0.4511\S1\Recipes\R1\ImageProcessing.log` | `_previouseAlignmentDataExists = 1` |
| 57 | `VcamInstallerGuid.txt` | .txt | 18 | 36 | 36 | 0 | `C:\job\ValidationJob\VcamInstallerGuid.txt` | `0f79e46a-a13a-4779-8b21-541a64d4e0b7` |
| 58 | `Wafer2Table_LastKnown.ini` | .ini | 18 | 1120 | 1122 | 0 | `C:\job\VCAM GrabColor\Setup1\Recipes\Repeatability\Wafer2Table_LastKnown.ini` | `[WAFER ALIGNMENT] Wafer2Table_X=          1.0000000000         0.0000000000   -156935.0399285386 Waf...` |
| 59 | `Bright-3_5um.ini` | .ini | 18 | 5689 | 5797 | 0 | `C:\job\VCAM GrabColor\Setup1\Recipes\Repeatability\Zones\Bright-3_5um.ini` | `[General] ZoneName=Bright-3_5um ZoneID=2 TypeName=Surface AutoTH=0 AutoProbe=0 [Surface] Enable=1 Al...` |
| 60 | `ZoneVerifyOptics.ini` | .ini | 18 | 51 | 51 | 0 | `C:\job\VCAM GrabColor\Setup1\Recipes\Repeatability\ZoneVerifyOptics.ini` | `[General] Version=1 UseSetGrabForOnlineVerify=0` |

…277 more rows in [`file_role_summary.csv`](file_role_summary.csv). The full per-file (non-aggregated) inventory with absolute paths, sizes, timestamps and first-120-character samples is in [`file_inventory.csv`](file_inventory.csv).

---

## Section 6 — Limitations / caveats

- **Open-handle probe skipped** (no `handle.exe` available, no admin elevation; would also impact a running machine). All inventory rows report "writable by SYSTEM = yes" (FullControl confirmed via ACL) but **active lock state is unknown**.
- The **bulk-installer event** that ran on 2026-05-17 ~11 am has reset `CreationTime` on 18 of 24 jobs to today, which biases the `LastModified − Created` heuristic. Only `Diced_10.0.4511`, `ValidationJob`, `OVL 1 frame`, `ScanAreaOnly` carry a meaningful runtime-update signature in this snapshot.
- The codebase scan distinguishes "references `c:\job\`" but **not write-vs-read** at the call site. Mapping each leaf name to a specific writer requires per-file inspection (Prompt 2 scope).
- Several `.dat` files passed the 30 % non-printable threshold despite being primarily binary (e.g. `Job.dat`, `WaferInfo.dat`, `DieMapping.dat`). They are kept in the inventory for completeness, flagged in the role table.
- A handful of files with `.md` extension are not markdown but tiny INI sidecars (`s_FrameData.dat.md`, `CurrWaferSurfaceInterpolation.md`); they passed the include filter on extension and the text-content filter on content.

---

*End of 01_discovered_files.md. Next: Prompt 2 — module classification.*
