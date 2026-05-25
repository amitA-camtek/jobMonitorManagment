# 02 — File Classification & Summary

> **Input:** `output/01_discovered_files.md` (Prompt 1) + `FileClassificationRules.json` + `ParameterDescriptions.json` + `system.md`
> **Scope:** Classify every text file in `c:\job\` by Module, Hardware scope, Owner service, Write pattern, Sensitivity, Monitor priority. No solution proposals.
> **Result:** 3 784 inventoried files → 115 distinct pattern groups → 100% classified.

Companion artifacts (in this folder):
- [`file_classification.csv`](file_classification.csv) — per-file classification (3 784 rows)
- [`group_classification.csv`](group_classification.csv) — 115 pattern groups with module/owner/priority/sensitivity/write-pattern
- [`module_owner_summary.csv`](module_owner_summary.csv) — Module × Owner roll-up

---

## Methodology

1. **Authoritative source:** `filesDiscovery/FileClassificationRules.json` (69 glob rules, ordered most-specific → least-specific). It already provides `module`, `ownerService`, `monitorPriority`, `shortName`, `description`.
2. **First-match wins** with glob-to-regex conversion (`**` → multi-segment, `*` → single segment) on lowercased absolute paths.
3. **Manual rule extension:** 70 additional patterns added to cover paths not in the original ruleset (per-die `Die_*` subtrees in `MPW_From_cad`, `.audit/`, QA `RND-*`/`AMITA1`/`FALCON_*` subfolders, CTS optic setup tree, focus-mapping debug dumps, WLUP files, per-site `*_focusCurve.txt`, etc.).
4. **Leaf-name fallback:** when no path rule matches, fall back to the rule that owns the same leaf name elsewhere.
5. **Per-file enrichment** with the change-indicator verdict from Prompt 1 (`change_indicators.csv`): Write-once / Mixed / Continuously updated.
6. **Derived fields:**
   - **Write pattern** — mapped from the change verdict + content/path hints (see § "Write-pattern decision rules" below).
   - **Sensitivity** — derived from monitor priority with a small adjustment so log/scan-result `P2` files are rated `Medium`, not `High`.

### Write-pattern decision rules

| Trigger | Write pattern |
|---|---|
| `c:\job\status.ini` (machine state singleton) | **OnRun (continuous)** |
| Module=`AlignmentData` & name contains "alignment" | **OnEvent (alignment cycle)** |
| `.audit/manifest.json` | **OnEvent (arrival/departure)** |
| Module=`Log` | **OnRun (append/overwrite)** |
| Name contains "ProductionInfo" / "production" / "wafer production" | **OnRun (per wafer)** |
| Name contains "Scan overlap" / "overview image" | **OnRun (per scan)** |
| Change verdict = `Continuously updated` | **OnRun (continuous)** |
| Change verdict = `Mixed` | **OnEvent / OnLoad** |
| Change verdict = `Write-once` | **OnCreate** |

> **Note:** Distinguishing `OnLoad` from `OnEvent` and `OnClose` cleanly requires code-level inspection (RMS/AOI_Main writers). The "OnEvent / OnLoad" composite is used wherever the timestamp evidence is consistent with either — it means *"rewritten outside the original create batch, but not on every wafer"*. `OnClose` is not separately distinguished in this pass (would require codebase trace through `SetupCreator`, `ProductionRecorder`, etc.).

### Sensitivity mapping

| Priority | Default Sensitivity | Override |
|---|---|---|
| P1 | Critical | — |
| P2 | High | `Module ∈ {Log, ScanResult}` ⇒ Medium |
| P3 | Medium | `Module = Log` ⇒ Low |
| P4 | Low | — |

---

## Section 1 — Aggregate breakdown

### 1.1 By priority

| Priority | Pattern groups | Files | % of files |
|---|---|---|---|
| **P1** | 28 | 1 089 | 28.8 % |
| **P2** | 62 | 1 211 | 32.0 % |
| **P3** | 22 | 934 | 24.7 % |
| **P4** | 3 | 550 | 14.5 % |
| **Total** | **115** | **3 784** | 100 % |

### 1.2 By module

| Module | Files | Notes |
|---|---|---|
| `Recipe` | 2 054 | The bulk — recipe definition + per-die variants |
| `Log` | 591 | Mostly `*_focusCurve.txt` debug dumps (521) — all P4 |
| `AlignmentData` | 580 | Alignment runtime + setup-creation baselines |
| `Config` | 313 | Optics, illumination, system-info, scripts |
| `Job` | 108 | Top-level + setup-level `Metadata.ini` (86) + `ProductionInfo.ini` (22) |
| `ScanResult` | 63 | Overlap/overview/CTW report artifacts |
| `DieMap` | 51 | DieMapRegPos, DieRegPos, ZonesVectorInfo, DiceInstances/InCad |
| `Audit` | 24 | `.audit\manifest.json` — written by FalconAuditService itself |

### 1.3 By owner service

| Owner | Files | Notes |
|---|---|---|
| `RMS` | 2 298 | Camtek.RMS.Service writes the bulk of recipe/setup files |
| `AOI_Main` | 1 335 | Falcon.Net runtime writers (alignment, focus, scan results) |
| `DataServer` | 108 | Optic preset, illum limits, optical-light metadata |
| `FalconAuditService` | 24 | Audit manifests under `.audit\` |
| `External tool` | 18 | `VcamInstallerGuid.txt` (VCamInstaller) |
| `Falcon.Net` | 1 | `c:\job\status.ini` only — the global UC_PROGRAM state |

### 1.4 By Module × Owner

| Module | Owner | Pattern groups | Files | Top priority |
|---|---|---|---|---|
| Audit | FalconAuditService | 1 | 24 | P2 |
| Config | AOI_Main | 1 | 25 | P1 |
| Config | DataServer | 3 | 108 | P1 |
| Config | External tool | 1 | 18 | P3 |
| Config | Falcon.Net | 1 | 1 | P1 |
| Config | RMS | 8 | 161 | P1 |
| Job | RMS | 2 | 108 | P1 |
| AlignmentData | AOI_Main | 15 | 427 | P1 |
| AlignmentData | RMS | 5 | 153 | P1 |
| DieMap | RMS | 8 | 51 | P2 |
| Log | AOI_Main | 3 | 550 | P4 |
| Log | RMS | 1 | 41 | P2 |
| Recipe | AOI_Main | 8 | 270 | P1 |
| Recipe | RMS | 52 | 1 784 | P1 |
| ScanResult | AOI_Main | 6 | 63 | P2 |

---

## Section 2 — Classification table (all 115 pattern groups)

Sorted by **Priority → Module → Count**. Hardware-scope column is a heuristic from the pattern name / `shortName` (most files are `Global`; `Camera`, `Illumination`, `Stage` are flagged where the file name signals it; `Robot/EFEM` did not surface as a leaf-name pattern in this inventory but exists in code).

| # | File pattern | Module | HW Scope | Owner | Write pattern | Sensitivity | Priority | Count | Verdict |
|---|---|---|---|---|---|---|---|---|---|
| 1 | `c:\job\**\Wafer2Table.ini` | AlignmentData | Global | AOI_Main | OnEvent (alignment cycle) | Critical | P1 | 49 | Mixed |
| 2 | `c:\job\**\Recipes\*\AlignRtp.ini` | AlignmentData | Global | RMS | OnEvent (alignment cycle) | Critical | P1 | 42 | Mixed |
| 3 | `c:\job\**\Recipes\*\Alignment.ini` | AlignmentData | Global | AOI_Main | OnEvent (alignment cycle) | Critical | P1 | 42 | Mixed |
| 4 | `c:\job\**\Recipes\*\WaferAlignData\AlignmentData.ini` | AlignmentData | Global | AOI_Main | OnEvent (alignment cycle) | Critical | P1 | 34 | Write-once |
| 5 | `c:\job\**\Recipes\*\Params_AlignRTP.ini` | AlignmentData | Global | RMS | OnEvent (alignment cycle) | Critical | P1 | 19 | Write-once |
| 6 | `c:\job\**\Recipes\*\OpticPreset.ini` | Config | Camera | DataServer | OnEvent / OnLoad | Critical | P1 | 41 | Mixed |
| 7 | `c:\job\**\Recipes\*\JobIllumLimits.ini` | Config | Illumination | DataServer | OnEvent / OnLoad | Critical | P1 | 27 | Mixed |
| 8 | `c:\job\**\Recipes\*\OpticToVCamStorage.json` | Config | Camera | AOI_Main | OnEvent / OnLoad | Critical | P1 | 25 | Mixed |
| 9 | `c:\job\**\Recipes\*\Params_SystemInfo.ini` | Config | Global | RMS | OnCreate | Critical | P1 | 19 | Write-once |
| 10 | `c:\job\status.ini` | Config | Global | Falcon.Net | OnRun (continuous) | Critical | P1 | 1 | Continuously updated |
| 11 | `c:\job\**\Metadata.ini` | Job | Global | RMS | OnEvent / OnLoad | Critical | P1 | 86 | Mixed |
| 12 | `c:\job\**\ProductionInfo.ini` | Job | Global | RMS | OnRun (per wafer) | Critical | P1 | 22 | Mixed |
| 13 | `c:\job\**\Recipes\*\Zones\*.ini` | Recipe | Global | RMS | OnEvent / OnLoad | Critical | P1 | 186 | Mixed |
| 14 | `c:\job\**\Recipes\*\Die_*\Zones.ini` | Recipe | Global | RMS | OnCreate | Critical | P1 | 116 | Write-once |
| 15 | `c:\job\**\Recipes\*\zones.ini` | Recipe | Global | RMS | OnCreate | Critical | P1 | 42 | Write-once |
| 16 | `c:\job\**\Recipes\*\ProductInfo.ini` | Recipe | Global | AOI_Main | OnEvent / OnLoad | Critical | P1 | 42 | Mixed |
| 17 | `c:\job\**\Recipes\*\Waferinfo.ini` | Recipe | Global | AOI_Main | OnEvent / OnLoad | Critical | P1 | 42 | Mixed |
| 18 | `c:\job\**\Recipes\*\Recipe.ini` | Recipe | Global | RMS | OnEvent / OnLoad | Critical | P1 | 42 | Mixed |
| 19 | `c:\job\**\Recipes\*\ZoomLevels.ini` | Recipe | Global | RMS | OnCreate | Critical | P1 | 42 | Write-once |
| 20 | `c:\job\**\Recipes\*\GlobalRTP.ini` | Recipe | Global | RMS | OnCreate | Critical | P1 | 39 | Write-once |
| 21 | `c:\job\**\Recipes\*\RTP.txt` | Recipe | Global | RMS | OnCreate | Critical | P1 | 39 | Write-once |
| 22 | `c:\job\**\DefectsClustering.ini` | Recipe | Global | RMS | OnEvent / OnLoad | Critical | P1 | 25 | Mixed |
| 23 | `c:\job\**\MultiRecipe.ini` | Recipe | Global | RMS | OnEvent / OnLoad | Critical | P1 | 25 | Mixed |
| 24 | `c:\job\**\Recipes\*\Params_WaferInfo.ini` | Recipe | Global | RMS | OnCreate | Critical | P1 | 19 | Write-once |
| 25 | `c:\job\**\Recipes\*\AQL.ini` | Recipe | Global | RMS | OnCreate | Critical | P1 | 13 | Write-once |
| 26 | `c:\job\**\ScanCondition.ini` | Recipe | Global | RMS | OnCreate | Critical | P1 | 8 | Write-once |
| 27 | `c:\job\**\Recipes\*\EBRInspectionParameters.xml` | Recipe | Global | RMS | OnCreate | Critical | P1 | 1 | Write-once |
| 28 | `c:\job\**\Zones.ini` | Recipe | Global | RMS | OnCreate | Critical | P1 | 1 | Write-once |
| 29 | `c:\job\**\Recipes\*\WaferAlignData\Alignment_*.txt` | AlignmentData | Global | AOI_Main | OnEvent (alignment cycle) | High | P2 | 120 | Write-once |
| 30 | `c:\job\**\DefaultWafer2Table.ini` | AlignmentData | Global | RMS | OnCreate | High | P2 | 49 | Write-once |
| 31 | `c:\job\**\Recipes\*\AlignmentData.ini` | AlignmentData | Global | RMS | OnEvent (alignment cycle) | High | P2 | 42 | Write-once |
| 32 | `c:\job\**\Recipes\*\WaferAlignData\AlignmentStatisticsTime.txt` | AlignmentData | Global | AOI_Main | OnEvent (alignment cycle) | High | P2 | 40 | Write-once |
| 33 | `c:\job\**\Recipes\*\TrainData\FrameToChuck.ini` | AlignmentData | Stage | AOI_Main | OnCreate | High | P2 | 37 | Write-once |
| 34 | `c:\job\**\CurrWaferSurfaceInterpolation.*` | AlignmentData | Global | AOI_Main | OnCreate | High | P2 | 31 | Write-once |
| 35 | `c:\job\**\WaferAlignData\WaferManualAlign_*.txt` | AlignmentData | Global | AOI_Main | OnCreate | High | P2 | 18 | Write-once |
| 36 | `c:\job\**\Wafer2Table_LastKnown.ini` | AlignmentData | Global | AOI_Main | OnCreate | High | P2 | 18 | Write-once |
| 37 | `c:\job\**\WaferAlignData\AlignmentNotFound_rtp.txt` | AlignmentData | Global | AOI_Main | OnEvent (alignment cycle) | High | P2 | 8 | Write-once |
| 38 | `c:\job\**\Recipes\*\FocusMapping\FocusPointsForScan.xml` | AlignmentData | Stage | AOI_Main | OnCreate | High | P2 | 6 | Write-once |
| 39 | `c:\job\**\Recipes\*\FocusMapping\DieReferenceLocation.json` | AlignmentData | Global | AOI_Main | OnCreate | High | P2 | 5 | Write-once |
| 40 | `c:\job\**\WaferAlignData\WLUP_Align_*.txt` | AlignmentData | Global | AOI_Main | OnCreate | High | P2 | 3 | Write-once |
| 41 | `c:\job\**\Recipes\*\WLUP.txt` | AlignmentData | Global | AOI_Main | OnCreate | High | P2 | 3 | Write-once |
| 42 | `c:\job\**\.audit\manifest.json` | Audit | Global | FalconAuditService | OnEvent (arrival/departure) | High | P2 | 24 | Write-once |
| 43 | `c:\job\**\Recipes\*\ExternalCoordSystems.ini` | Config | Global | RMS | OnCreate | High | P2 | 42 | Write-once |
| 44 | `c:\job\**\Recipes\*\WaferDataReadSettings.xml` | Config | Global | RMS | OnCreate | High | P2 | 42 | Write-once |
| 45 | `c:\job\**\Recipes\*\OpticLightMetadata\config.ini` | Config | Illumination | DataServer | OnEvent / OnLoad | High | P2 | 40 | Mixed |
| 46 | `c:\job\**\Scripts.ini` | Config | Global | RMS | OnCreate | High | P2 | 6 | Write-once |
| 47 | `c:\job\**\MultiLightChannels.ini` | Config | Illumination | RMS | OnCreate | High | P2 | 1 | Write-once |
| 48 | `c:\job\**\Recipes\*\TNESetup.ini` | Config | Global | RMS | OnCreate | High | P2 | 1 | Write-once |
| 49 | `c:\job\**\TrainData\ZonesVectorInfo.csv` | DieMap | Global | RMS | OnCreate | High | P2 | 21 | Write-once |
| 50 | `c:\job\**\Recipes\*\DieMapRegPos.txt` | DieMap | Global | RMS | OnCreate | High | P2 | 13 | Write-once |
| 51 | `c:\job\**\Recipes\*\DieRegPos.txt` | DieMap | Global | RMS | OnCreate | High | P2 | 8 | Write-once |
| 52 | `c:\job\**\Recipes\*\DiceInstances.xml` | DieMap | Global | RMS | OnCreate | High | P2 | 4 | Write-once |
| 53 | `c:\job\**\TrainData\DiceInCad.xml` | DieMap | Global | RMS | OnCreate | High | P2 | 2 | Write-once |
| 54 | `c:\job\**\TrainData\ScanAreaVectorInfo.csv` | DieMap | Global | RMS | OnCreate | High | P2 | 1 | Write-once |
| 55 | `c:\job\**\Recipes\*\DieMapping.txt` | DieMap | Global | RMS | OnCreate | High | P2 | 1 | Write-once |
| 56 | `c:\job\**\Recipes\*\DieOffset.txt` | DieMap | Global | RMS | OnCreate | High | P2 | 1 | Write-once |
| 57 | `c:\job\**\Recipes\*\.dc_cache\TransactionsHistory.ini` | Log | Global | RMS | OnRun (append/overwrite) | Medium | P2 | 41 | Write-once |
| 58 | `c:\job\**\Recipes\*\DieAlignment.dat_block.ini` | Recipe | Global | RMS | OnEvent / OnLoad | High | P2 | 57 | Mixed |
| 59 | `c:\job\**\Recipes\*\WaferMapRecipe.ini` | Recipe | Global | RMS | OnEvent / OnLoad | High | P2 | 44 | Mixed |
| 60 | `c:\job\**\Recipes\*\UniqueArea.ini` | Recipe | Global | RMS | OnCreate | High | P2 | 42 | Write-once |
| 61 | `c:\job\**\Recipes\*\CreateReference3dOptions.ini` | Recipe | Global | RMS | OnCreate | High | P2 | 42 | Write-once |
| 62 | `c:\job\**\Recipes\*\TrainData\Die.ini` | Recipe | Global | AOI_Main | OnCreate | High | P2 | 42 | Write-once |
| 63 | `c:\job\**\Recipes\*\ScenariosMetadatas.ini` | Recipe | Global | RMS | OnEvent / OnLoad | High | P2 | 41 | Mixed |
| 64 | `c:\job\**\Recipes\*\CleanReferenceFinalParams.ini` | Recipe | Global | RMS | OnCreate | High | P2 | 41 | Write-once |
| 65 | `c:\job\**\Recipes\*\TrainData\DieRefToTrain.txt` | Recipe | Global | AOI_Main | OnCreate | High | P2 | 37 | Write-once |
| 66 | `c:\job\**\Recipes\*\zones.txt` | Recipe | Global | RMS | OnCreate | High | P2 | 37 | Write-once |
| 67 | `c:\job\**\Recipes\*\FocusMapping\FocusMapping.ini` | Recipe | Stage | AOI_Main | OnEvent / OnLoad | High | P2 | 27 | Mixed |
| 68 | `c:\job\**\Recipes\*\ScenarioMetadataGrab.xml` | Recipe | Global | RMS | OnCreate | High | P2 | 20 | Write-once |
| 69 | `c:\job\**\Recipes\*\Job.dat` | Recipe | Global | RMS | OnCreate | High | P2 | 19 | Write-once |
| 70 | `c:\job\**\Recipes\*\CcsLocalMeas.ini` | Recipe | Global | RMS | OnCreate | High | P2 | 19 | Write-once |
| 71 | `c:\job\**\Recipes\*\DieMapAlignRes.dat_block.ini` | Recipe | Global | RMS | OnCreate | High | P2 | 17 | Write-once |
| 72 | `c:\job\**\Recipes\*\WaferToRefWafer.ini` | Recipe | Global | RMS | OnCreate | High | P2 | 13 | Write-once |
| 73 | `c:\job\**\Recipes\*\SamplingMetrology.ini` | Recipe | Global | RMS | OnCreate | High | P2 | 13 | Write-once |
| 74 | `c:\job\**\Recipes\*\OverlayScan.ini` | Recipe | Global | RMS | OnCreate | High | P2 | 11 | Write-once |
| 75 | `c:\job\**\Recipes\*\ReferencesInfo.json` | Recipe | Global | RMS | OnCreate | High | P2 | 11 | Write-once |
| 76 | `c:\job\**\ZoomLevels.ini` | Recipe | Global | RMS | OnCreate | High | P2 | 8 | Write-once |
| 77 | `c:\job\**\Recipes\*\FocusMapping\Model_*\FocusModel.ini` | Recipe | Stage | AOI_Main | OnCreate | High | P2 | 6 | Write-once |
| 78 | `c:\job\**\Recipes\*\CleanReferenceConfiguration.ini` | Recipe | Global | RMS | OnCreate | High | P2 | 4 | Write-once |
| 79 | `c:\job\**\TrainData\CadtoJobRecipe.xml` | Recipe | Global | RMS | OnCreate | High | P2 | 4 | Write-once |
| 80 | `c:\job\**\RecipesInfo.ini` | Recipe | Global | RMS | OnCreate | High | P2 | 4 | Write-once |
| 81 | `c:\job\**\ScanCTS_*\ScanCTS_*.xml` | Recipe | Global | RMS | OnCreate | High | P2 | 3 | Write-once |
| 82 | `c:\job\**\Recipes\*\CTSOpticSetup\Manual\Sites\Sites.xml` | Recipe | Global | RMS | OnCreate | High | P2 | 3 | Write-once |
| 83 | `c:\job\**\Recipes\*\Bumps.dat` | Recipe | Global | RMS | OnCreate | High | P2 | 3 | Write-once |
| 84 | `c:\job\**\Recipes\*\CTSOpticSetup\Manual\ManualScannedSites\*.xml` | Recipe | Global | RMS | OnCreate | High | P2 | 3 | Write-once |
| 85 | `c:\job\**\TrainData\CadReferenceMetaData.xml` | Recipe | Global | RMS | OnCreate | High | P2 | 2 | Write-once |
| 86 | `c:\job\**\Recipes\*\ManualMasking.xml` | Recipe | Global | RMS | OnCreate | High | P2 | 1 | Write-once |
| 87 | `c:\job\**\Recipes\*\CcsSetup.xml` | Recipe | Global | RMS | OnCreate | High | P2 | 1 | Write-once |
| 88 | `c:\job\**\Recipes\*\CadSegmentCleanReference.json` | Recipe | Global | RMS | OnCreate | High | P2 | 1 | Write-once |
| 89 | `c:\job\**\Recipes\*\RecipeScanRestriction.ini` | Recipe | Global | RMS | OnCreate | High | P2 | 1 | Write-once |
| 90 | `c:\job\**\CTWRepeatabilityReport.xml` | ScanResult | Global | AOI_Main | OnCreate | Medium | P2 | 6 | Write-once |
| 91 | `c:\job\**\DefaultWaferSurfaceInterpolation.*` | AlignmentData | Global | AOI_Main | OnCreate | Medium | P3 | 13 | Write-once |
| 92 | `c:\job\**\Recipes\*\*\DefaultAlign.ini` | AlignmentData | Global | RMS | OnEvent (alignment cycle) | Medium | P3 | 1 | Write-once |
| 93 | `c:\job\**\Recipes\*\*\OpticsPreset.ini` | Config | Camera | RMS | OnEvent / OnLoad | Medium | P3 | 26 | Mixed |
| 94 | `c:\job\**\Recipes\*\SW_QA-*\OpticsPreset.ini` | Config | Camera | RMS | OnCreate | Medium | P3 | 24 | Write-once |
| 95 | `c:\job\*\VcamInstallerGuid.txt` | Config | Global | External tool | OnCreate | Medium | P3 | 18 | Write-once |
| 96 | `c:\job\**\Recipes\*\Die_*\*.txt` | Recipe | Global | RMS | OnCreate | Medium | P3 | 406 | Write-once |
| 97 | `c:\job\**\Recipes\*\Die_*\ZoomLevels.ini` | Recipe | Global | RMS | OnCreate | Medium | P3 | 116 | Write-once |
| 98 | `c:\job\**\Recipes\*\Die_*\ReferenceBackup\ZoomLevels.ini` | Recipe | Global | RMS | OnCreate | Medium | P3 | 116 | Write-once |
| 99 | `c:\job\**\Recipes\*\ReferenceBackup\ZoomLevels.ini` | Recipe | Global | RMS | OnCreate | Medium | P3 | 41 | Write-once |
| 100 | `c:\job\**\Recipes\*\TrainData\DieImage\DieImageToTable.ini` | Recipe | Global | AOI_Main | OnCreate | Medium | P3 | 37 | Write-once |
| 101 | `c:\job\**\Recipes\*\TrainData\DieImage\ZoomLevels.ini` | Recipe | Global | AOI_Main | OnCreate | Medium | P3 | 37 | Write-once |
| 102 | `c:\job\**\Recipes\*\ZoneVerifyOptics.ini` | Recipe | Camera | RMS | OnCreate | Medium | P3 | 18 | Write-once |
| 103 | `c:\job\**\Recipes\*\s_*.dat.md` | Recipe | Global | RMS | OnCreate | Medium | P3 | 9 | Write-once |
| 104 | `c:\job\**\Recipes\*\s_FrameData.dat.md` | Recipe | Global | RMS | OnCreate | Medium | P3 | 6 | Write-once |
| 105 | `c:\job\**\Recipes\*\CTSOpticSetup\SetupSummary.xml` | Recipe | Global | RMS | OnCreate | Medium | P3 | 5 | Write-once |
| 106 | `c:\job\**\Recipes\*\Sites*.xml` | Recipe | Global | RMS | OnCreate | Medium | P3 | 3 | Write-once |
| 107 | `c:\job\**\Transitions.xml` | Recipe | Global | RMS | OnCreate | Medium | P3 | 1 | Write-once |
| 108 | `c:\job\**\Recipes\*\ScanOverlapLog.txt` | ScanResult | Global | AOI_Main | OnRun (per scan) | Medium | P3 | 41 | Mixed |
| 109 | `c:\job\**\Recipes\*\ScanOverviewImage_*.txt` | ScanResult | Global | AOI_Main | OnRun (per scan) | Medium | P3 | 13 | Write-once |
| 110 | `c:\job\**\Recipes\*\ProcessingRef\*.xml` | ScanResult | Global | AOI_Main | OnCreate | Medium | P3 | 1 | Write-once |
| 111 | `c:\job\**\Recipes\*\UniqueResultTypeIds.ini` | ScanResult | Global | AOI_Main | OnCreate | Medium | P3 | 1 | Write-once |
| 112 | `c:\job\**\Recipes\*\ScanOverlapLog3d.txt` | ScanResult | Global | AOI_Main | OnRun (per scan) | Medium | P3 | 1 | Write-once |
| 113 | `c:\job\**\DebugAFMapping*\*_focusCurve.txt` | Log | Stage | AOI_Main | OnRun (append/overwrite) | Low | P4 | 521 | Write-once |
| 114 | `c:\job\**\Recipes\*\ImageProcessing.log` | Log | Global | AOI_Main | OnRun (append/overwrite) | Low | P4 | 19 | Write-once |
| 115 | `c:\job\**\DebugAFMapping*\FocusMappingDebug*.txt` | Log | Global | AOI_Main | OnRun (append/overwrite) | Low | P4 | 10 | Write-once |

> The "Verdict = Write-once" for many P1 mixed-content files (e.g. `Wafer2Table.ini`, `Alignment.ini`) is biased by the 2026-05-17 bulk-job deploy that reset `CreationTime` on 18 of 24 jobs to today (per Prompt 1 §4.2). The four older jobs (`Diced_10.0.4511`, `ValidationJob`, `OVL 1 frame`, `ScanAreaOnly`) drove the "Mixed" verdicts on those rows.

---

## Section 3 — Per-group summaries

Grouped by (Module, Owner). Key fields are sourced from `ParameterDescriptions.json` where available.

### Job — RMS

- **Pattern count:** 2 distinct patterns (108 files total)
- **Top monitor priority:** P1
- **Sensitivity:** Critical
- **Write patterns observed:** OnEvent / OnLoad (Metadata.ini); OnRun (per wafer) (ProductionInfo.ini)
- **Change verdicts:** Mixed
- **Patterns:**
  - `c:\job\**\Metadata.ini` — 86 files (job- and setup-level identity)
  - `c:\job\**\ProductionInfo.ini` — 22 files
- **Key fields to watch:**
  - `Metadata.ini` → `General.name`, `General.Id`, `General.Version`, `General.JobTag`, `General.LastActiveRecipe`
  - `ProductionInfo.ini` → `General.WaferDefectsCount`, `General.WaferDefectDiceRatio`, `General.WaferDefectClassId`, `General.WaferDefectClassIdCount`, `General.BatchPassWafersCount`

### Recipe — RMS

- **Pattern count:** 52 distinct patterns (1 784 files total)
- **Top monitor priority:** P1
- **Sensitivity:** Critical
- **Write patterns observed:** OnCreate (most), OnEvent / OnLoad (Recipe.ini, MultiRecipe.ini, DefectsClustering.ini, Zones/*.ini, WaferMapRecipe.ini, ScenariosMetadatas.ini, DieAlignment.dat_block.ini, FocusMapping.ini)
- **Change verdicts:** Mostly Write-once, several Mixed at recipe level
- **High-volume patterns:** `Recipes\*\Zones\*.ini` (186), `Die_*\Zones.ini` (116), `Die_*\ZoomLevels.ini` (116), `Die_*\ReferenceBackup\ZoomLevels.ini` (116), `Recipes\*\ZoomLevels.ini` (42), `Recipes\*\zones.ini` (42), `Recipes\*\Recipe.ini` (42), `Recipes\*\Params_WaferInfo.ini` (19)
- **Key fields to watch:**
  - `Recipe.ini` → `AutoCycle.AutoFocusEvery`, `AutoCycle.CleanReferenceEvery`, `AutoCycle.UnloadToAnotherCassette`, `AutoCycle.EnableDieLevelPostProcessing`, `General.Recipe Name`
  - `zones.ini` → `General.ZoneCount`, `General.ActiveZones`
  - `ZoomLevels.ini` → `General.ZoomLevelCount`, `General.ActiveZoomLevel`
  - `GlobalRTP.ini` → `General.SensitivityLevel`, `General.ThresholdMode`
  - `MultiRecipe.ini` → `Scan.Recipes`, `Scan.RunWaferAlignment`, `Scan.RecipesSampling`, `WaferAQL2.Scan1`
  - `DefectsClustering.ini` → `General.Enabled`, `General.Distance`, `General.DefectsClusteringAlgMode`, `General.SelectedFirstSortingList`

### Recipe — AOI_Main

- **Pattern count:** 8 distinct patterns (270 files total)
- **Top monitor priority:** P1
- **Sensitivity:** Critical
- **Write patterns observed:** OnEvent / OnLoad (ProductInfo.ini, Waferinfo.ini, FocusMapping.ini), OnCreate (TrainData/*, DieImage/*)
- **Patterns:** `ProductInfo.ini` (42), `Waferinfo.ini` (42), `TrainData\Die.ini` (42), `TrainData\DieRefToTrain.txt` (37), `TrainData\DieImage\DieImageToTable.ini` (37), `TrainData\DieImage\ZoomLevels.ini` (37), `FocusMapping\FocusMapping.ini` (27), `FocusMapping\Model_*\FocusModel.ini` (6)
- **Key fields to watch:**
  - `ProductInfo.ini` → `General.DieSizeX`, `General.DieSizeY`, `General.DiePitchX`, `General.DiePitchY`, `General.WaferDiameter`, `General.EdgeExclusion`, `General.ScanType`
  - `Waferinfo.ini` → `General.RobotPreAlignerAngle`, `General.IsAutoCycleEnabled`, `General.UseInkDot`, `General.UseOcr`, `General.HwDependencies`
  - `FocusMapping.ini` → `General.SampleCount`, `General.FocusMethod`, `General.ValidMap`

### AlignmentData — AOI_Main

- **Pattern count:** 15 distinct patterns (427 files total)
- **Top monitor priority:** P1
- **Sensitivity:** Critical (Wafer2Table.ini, Alignment.ini, WaferAlignData\AlignmentData.ini) / High otherwise
- **Write patterns observed:** OnEvent (alignment cycle) for Wafer2Table.ini, Alignment.ini, AlignmentData.ini, AlignmentStatisticsTime.txt, Alignment_*.txt, AlignmentNotFound_rtp.txt; OnCreate for everything else
- **Patterns:** `Wafer2Table.ini` (49), `Alignment.ini` (42), `WaferAlignData\AlignmentData.ini` (34), `WaferAlignData\Alignment_*.txt` (120), `WaferAlignData\AlignmentStatisticsTime.txt` (40), `TrainData\FrameToChuck.ini` (37), `CurrWaferSurfaceInterpolation.*` (31), `WaferAlignData\WaferManualAlign_*.txt` (18), `Wafer2Table_LastKnown.ini` (18), `WaferAlignData\AlignmentNotFound_rtp.txt` (8), `FocusPointsForScan.xml` (6), `FocusMapping\DieReferenceLocation.json` (5), `WLUP_Align_*.txt` (3), `WLUP.txt` (3), `DefaultWaferSurfaceInterpolation.*` (13, P3)
- **Key fields to watch:**
  - `Wafer2Table.ini` → `WAFER ALIGNMENT.Wafer2Table_X`, `WAFER ALIGNMENT.Wafer2Table_Y`, `WAFER ALIGNMENT.Rotate w2t`, `WAFER ALIGNMENT.Offset w2t`, `WAFER ALIGNMENT.StdAffine`, `LOCAL_CORRECTION.Apply`, `General.SaveTime`, `ANCHOR POINTS.Count`
  - `Alignment.ini` → `General.IsAligned`, `General.RotationResidual`

### AlignmentData — RMS

- **Pattern count:** 5 distinct patterns (153 files total)
- **Top monitor priority:** P1 (Params_AlignRTP.ini, AlignRtp.ini)
- **Sensitivity:** Critical (P1) / High (P2)
- **Patterns:** `Recipes\*\AlignRtp.ini` (42), `Recipes\*\Params_AlignRTP.ini` (19), `Recipes\*\AlignmentData.ini` (42), `DefaultWafer2Table.ini` (49), `Recipes\*\*\DefaultAlign.ini` (1, P3)
- **Write patterns observed:** OnEvent (alignment cycle) where Mixed; OnCreate otherwise
- **Key fields to watch:**
  - `AlignmentData.ini` → `General.MinScore`, `General.MinTargetScore`, `General.IsAffineEnabled`, `General.AlignPointsNum`, `SECOND_ALIGN.MinGoodModels`
  - `AlignRtp.ini` → `General.SearchWindowX`, `General.SearchWindowY`, `General.MinScore`
  - `DefaultWafer2Table.ini` → identical to `Wafer2Table.ini`, plus `General.SaveTime`

### Config — Falcon.Net (1 file)

- **Pattern:** `c:\job\status.ini` — only shared/global file at `c:\job\` root
- **Files:** 1
- **Priority:** P1, **Sensitivity:** Critical, **Write pattern:** OnRun (continuous)
- **Change verdict:** Continuously updated (Δt ≈ 181 675 min between create and last-write — the file persists across machine reboots)
- **Key fields to watch:** `UC_PROGRAM.ProgramName`, `UC_PROGRAM.ProductName`, `UC_PROGRAM.RecipeName`
- **Notes:** Single global singleton, rewritten by Falcon.Net on every program/state transition. This is the canonical "current state" telemetry source.

### Config — DataServer

- **Pattern count:** 3 distinct patterns (108 files total)
- **Top monitor priority:** P1 (OpticPreset.ini, JobIllumLimits.ini)
- **Sensitivity:** Critical / High
- **Patterns:** `Recipes\*\OpticPreset.ini` (41), `Recipes\*\OpticLightMetadata\config.ini` (40), `Recipes\*\JobIllumLimits.ini` (27)
- **Write patterns:** OnEvent / OnLoad
- **Key fields to watch:**
  - `OpticPreset.ini` → `General.PresetName`, `General.IllumChannel`, `General.ExposureTime`, `General.Gain`
  - `JobIllumLimits.ini` → `General.MinIllumLevel`, `General.MaxIllumLevel`

### Config — AOI_Main (25 files)

- **Pattern:** `c:\job\**\Recipes\*\OpticToVCamStorage.json`
- **Priority:** P1, **Sensitivity:** Critical, **Write pattern:** OnEvent / OnLoad
- **Notes:** Optic-to-virtual-camera mapping; mixes JSON structure (`OpticId`, `Scenario`, `IsAutoFocus`).

### Config — RMS

- **Pattern count:** 8 distinct patterns (161 files total)
- **Top monitor priority:** P1 (Params_SystemInfo.ini)
- **Patterns:** `Params_SystemInfo.ini` (19, P1), `ExternalCoordSystems.ini` (42, P2), `WaferDataReadSettings.xml` (42, P2), `Recipes\*\*\OpticsPreset.ini` (26, P3 — QA/RND variants), `Recipes\*\SW_QA-*\OpticsPreset.ini` (24, P3), `Scripts.ini` (6, P2), `MultiLightChannels.ini` (1, P2), `TNESetup.ini` (1, P2)
- **Key fields:** `Params_SystemInfo.ini` → `General.MachineType`, `General.MachineSerial`, `General.SoftwareVersion`

### Config — External tool (18 files)

- **Pattern:** `c:\job\*\VcamInstallerGuid.txt` (P3, OnCreate)
- **Notes:** Stamped by VCamInstaller during deploy; one per VCAM-prefixed job.

### DieMap — RMS

- **Pattern count:** 8 distinct patterns (51 files total)
- **Top monitor priority:** P2
- **Sensitivity:** High
- **Patterns:** `TrainData\ZonesVectorInfo.csv` (21), `Recipes\*\DieMapRegPos.txt` (13), `Recipes\*\DieRegPos.txt` (8), `Recipes\*\DiceInstances.xml` (4), `TrainData\DiceInCad.xml` (2), `TrainData\ScanAreaVectorInfo.csv` (1), `Recipes\*\DieMapping.txt` (1), `Recipes\*\DieOffset.txt` (1)
- **Write patterns:** OnCreate (all write-once)
- **Notes:** The original ruleset also lists binary `DieRegPos.dat`, `DieMapRegPos.dat`, `DieMapping.dat`, `zones.dat` as DieMap/P2 — those are excluded from the text inventory because they failed the 30 %-non-printable heuristic.

### Audit — FalconAuditService (24 files)

- **Pattern:** `c:\job\**\.audit\manifest.json`
- **Priority:** P2, **Sensitivity:** High, **Write pattern:** OnEvent (arrival/departure)
- **Key fields:** `jobName`, `auditDbVersion`, `created.machine`, `created.timestamp`, `lastSeen.machine`, `sealed`
- **Notes:** This is **the monitoring service's own state file**. It is the only file under `c:\job\` written by the service that will eventually consume this classification.

### ScanResult — AOI_Main

- **Pattern count:** 6 distinct patterns (63 files total)
- **Top monitor priority:** P2 (CTWRepeatabilityReport.xml)
- **Sensitivity:** Medium
- **Patterns:** `Recipes\*\ScanOverlapLog.txt` (41, P3, OnRun per scan), `Recipes\*\ScanOverviewImage_*.txt` (13, P3), `CTWRepeatabilityReport.xml` (6, P2), `Recipes\*\ScanOverlapLog3d.txt` (1, P3), `Recipes\*\ProcessingRef\*.xml` (1, P3), `Recipes\*\UniqueResultTypeIds.ini` (1, P3)

### Log — RMS (41 files)

- **Pattern:** `c:\job\**\Recipes\*\.dc_cache\TransactionsHistory.ini` (P2, OnRun append/overwrite, Medium)
- **Notes:** RMS's internal log of which recipe-part transactions were applied; appended on every edit.

### Log — AOI_Main (550 files)

- **Pattern count:** 3 distinct patterns
- **All P4, Sensitivity Low**
- **Patterns:**
  - `c:\job\**\DebugAFMapping*\*_focusCurve.txt` (521 files — one per AF site)
  - `c:\job\**\Recipes\*\ImageProcessing.log` (19)
  - `c:\job\**\DebugAFMapping*\FocusMappingDebug*.txt` (10)
- **Notes:** High-volume diagnostic dumps. Per-site focus-curve files churn the most.

---

## Section 4 — Monitoring scope summary

| Decision | File groups (count of patterns / files) | Reason |
|---|---|---|
| **Monitor (P1)** — 28 patterns, **1 089 files** | All `Recipe`/`Job`/`Config` files marked Critical: `Metadata.ini`, `ProductionInfo.ini`, `Recipe.ini`, `Recipes\*\zones.ini`, `Zones\*.ini`, `ZoomLevels.ini`, `GlobalRTP.ini`, `RTP.txt`, `MultiRecipe.ini`, `DefectsClustering.ini`, `ProductInfo.ini`, `Waferinfo.ini`, `Wafer2Table.ini`, `Alignment.ini`, `AlignRtp.ini`, `AlignmentData.ini` (in `WaferAlignData`), `Params_AlignRTP.ini`, `Params_WaferInfo.ini`, `Params_SystemInfo.ini`, `OpticPreset.ini`, `OpticToVCamStorage.json`, `JobIllumLimits.ini`, `AQL.ini`, `ScanCondition.ini`, `EBRInspectionParameters.xml`, `status.ini`, `Die_*\Zones.ini` | Change could affect inspection result or machine safety. **Hash + diff every change.** |
| **Monitor (P2)** — 62 patterns, **1 211 files** | Default templates (`DefaultWafer2Table.ini`), alignment runtime logs (`Alignment_*.txt`, `AlignmentStatisticsTime.txt`), `manifest.json`, `ExternalCoordSystems.ini`, `WaferDataReadSettings.xml`, `OpticLightMetadata\config.ini`, `DieMapRegPos.txt`, `DieRegPos.txt`, `DiceInstances.xml`, `ZonesVectorInfo.csv`, `Scripts.ini`, `TransactionsHistory.ini`, `UniqueArea.ini`, `CreateReference3dOptions.ini`, `TrainData\Die.ini`, `ScenariosMetadatas.ini`, `WaferMapRecipe.ini`, `DieAlignment.dat_block.ini`, `Job.dat`, `FocusMapping.ini`, `FocusPointsForScan.xml`, `CTWRepeatabilityReport.xml`, etc. | Affects traceability / repeatability. **Record change, full diff optional.** |
| **Nice-to-have (P3)** — 22 patterns, **934 files** | Per-die variants in `Die_*\` (638 files — heavy volume from `MPW_From_cad`), `ReferenceBackup\ZoomLevels.ini`, `DieImage\DieImageToTable.ini`, `DieImage\ZoomLevels.ini`, `SW_QA-*\OpticsPreset.ini`, machine-name `*\OpticsPreset.ini`, `s_*.dat.md` sidecars, `ScanOverlapLog.txt`, `ScanOverviewImage_*.txt`, `ScanOverlapLog3d.txt`, `Transitions.xml`, `Sites*.xml`, `VcamInstallerGuid.txt`, `CTSOpticSetup\SetupSummary.xml`, `ZoneVerifyOptics.ini`, `DefaultWaferSurfaceInterpolation.*` | Useful for debugging; **record existence of change, no diff**. The 638 per-die variants are highly repetitive copies of a small set of recipe templates — diffing each one is wasteful. |
| **Skip (P4)** — 3 patterns, **550 files** | `DebugAFMapping*\*_focusCurve.txt` (521), `Recipes\*\ImageProcessing.log` (19), `DebugAFMapping*\FocusMappingDebug*.txt` (10) | Too noisy, churned per AF site / per frame. **Filter out at FSW level**; don't even emit change events. |

### Counts at a glance

| Decision | Patterns | Files |
|---|---|---|
| Monitor (P1) | 28 | 1 089 |
| Monitor (P2) | 62 | 1 211 |
| Record existence only (P3) | 22 | 934 |
| Skip (P4) | 3 | 550 |
| **Total** | **115** | **3 784** |

### Hardware-scope distribution

Most files are `Global` (not tied to a single hardware unit). The non-Global tagging is concentrated where the file name carries hardware semantics:

| HW Scope | Patterns | Notes |
|---|---|---|
| Camera | 5 | `OpticPreset.ini`, `OpticToVCamStorage.json`, `Recipes\*\*\OpticsPreset.ini`, `SW_QA-*\OpticsPreset.ini`, `ZoneVerifyOptics.ini` |
| Illumination | 3 | `JobIllumLimits.ini`, `OpticLightMetadata\config.ini`, `MultiLightChannels.ini` |
| Stage | 4 | `TrainData\FrameToChuck.ini`, `FocusPointsForScan.xml`, `FocusMapping.ini`, `FocusMapping\Model_*\FocusModel.ini`, `DebugAFMapping*\*_focusCurve.txt` |
| Robot/EFEM | 0 | Not surfaced as a leaf-name pattern in this inventory (`Waferinfo.ini` mentions `Robot.Rotation`/`SideUp` but is filed Global); code references in `apps\Falcon.Net\Modules\EFEMModule.cs`, `Automation.Mng\EFEM\AutoLoaderWrapper.cs` show robot/EFEM state lives in machine config, not under `c:\job\` |
| Global | 103 | Default |

---

## Section 5 — Caveats / unknowns

- **`OnLoad` vs `OnEvent` vs `OnClose`** distinction is heuristic. Wherever the change verdict is "Mixed" with Δt < 24 h, the pattern is reported as `OnEvent / OnLoad`. Codebase tracing through `SetupCreator`, `RecipeModelManager`, `ProductionRecorder`, `ScanResultsInternalService` would disambiguate; that is Prompt 3+ work.
- **`Continuously updated` only fires for `status.ini`** in this snapshot. Other files that look continuously-updated in production (e.g. `Wafer2Table.ini`, `Alignment.ini`, `AlignmentStatisticsTime.txt`) appear "Write-once" because the 18 freshly-deployed VCAM jobs haven't run yet. Re-classify after the next production wafer pass.
- **18 of 24 jobs were deployed 2026-05-17 ~11 am** (per `git status` + Prompt 1 §3.5). Those jobs have `CreationTime` ≈ today, so the timestamp-delta heuristic understates "continuously updated" — flagged in the table notes.
- **Binary `.dat` files** (`DieRegPos.dat`, `DieMapRegPos.dat`, `DieMapping.dat`, `zones.dat`) are excluded from the text inventory by the 30 %-non-printable heuristic, but they exist in `FileClassificationRules.json` as `DieMap/RMS/P2`. They are referenced here for completeness but not counted in the 3 784 figure.
- **Hardware-scope tagging** is conservative; many files marked `Global` carry hardware-specific subsections (e.g. `Waferinfo.ini` has `[Robot]` group). The current tagging keys on filename/short-name only.
- **Owner uncertainty.** The `FileClassificationRules.json` declares a single `ownerService` per pattern, but for several files (e.g. `Recipe.ini`, `MultiRecipe.ini`) both RMS and AOI_Main can rewrite them depending on the operation. The classification keeps the rules' declared owner; a future "last writer observed" attribution would need FSW telemetry.

---

*End of 02_file_summary.md. Next: Prompt 3 — design the monitoring service.*
