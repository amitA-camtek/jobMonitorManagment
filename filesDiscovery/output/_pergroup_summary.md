
### Config — AOI_Main

- **Pattern count:** 1 distinct patterns (25 files total)
- **Top monitor priority:** P1
- **Sensitivity:** Critical (highest in group)
- **Write patterns observed:** OnEvent / OnLoad
- **Change verdicts:** Mixed

| Pattern | Count | Priority | Verdict | Write pattern | Sample leaf |
|---|---|---|---|---|---|
| `c:\job\**\Recipes\*\OpticToVCamStorage.json` | 25 | P1 | Mixed | OnEvent / OnLoad | OpticToVCamStorage.json |

### Config — DataServer

- **Pattern count:** 3 distinct patterns (108 files total)
- **Top monitor priority:** P1
- **Sensitivity:** Critical (highest in group)
- **Write patterns observed:** OnEvent / OnLoad
- **Change verdicts:** Mixed

| Pattern | Count | Priority | Verdict | Write pattern | Sample leaf |
|---|---|---|---|---|---|
| `c:\job\**\Recipes\*\OpticPreset.ini` | 41 | P1 | Mixed | OnEvent / OnLoad | OpticPreset.ini |
| `c:\job\**\Recipes\*\OpticLightMetadata\config.ini` | 40 | P2 | Mixed | OnEvent / OnLoad | config.ini |
| `c:\job\**\Recipes\*\JobIllumLimits.ini` | 27 | P1 | Mixed | OnEvent / OnLoad | JobIllumLimits.ini |

**Key fields to watch (from ParameterDescriptions.json):**

  - `c:\job\**\Recipes\*\OpticPreset.ini`
    - `General.PresetName` — Optical preset name
    - `General.IllumChannel` — Illumination channel selection
    - `General.ExposureTime` — Camera exposure time (ms)
    - `General.Gain` — Camera gain value
  - `c:\job\**\Recipes\*\JobIllumLimits.ini`
    - `General.MinIllumLevel` — Minimum allowed illumination level
    - `General.MaxIllumLevel` — Maximum allowed illumination level

### Config — Falcon.Net

- **Pattern count:** 1 distinct patterns (1 files total)
- **Top monitor priority:** P1
- **Sensitivity:** Critical (highest in group)
- **Write patterns observed:** OnRun (continuous)
- **Change verdicts:** Continuously updated

| Pattern | Count | Priority | Verdict | Write pattern | Sample leaf |
|---|---|---|---|---|---|
| `c:\job\status.ini` | 1 | P1 | Continuously updated | OnRun (continuous) | status.ini |

**Key fields to watch (from ParameterDescriptions.json):**

  - `c:\job\status.ini`
    - `UC_PROGRAM.ProgramName` — Active machine program
    - `UC_PROGRAM.ProductName` — Active job / product
    - `UC_PROGRAM.RecipeName` — Active recipe

### Job — RMS

- **Pattern count:** 2 distinct patterns (108 files total)
- **Top monitor priority:** P1
- **Sensitivity:** Critical (highest in group)
- **Write patterns observed:** OnEvent / OnLoad; OnRun (per wafer)
- **Change verdicts:** Mixed

| Pattern | Count | Priority | Verdict | Write pattern | Sample leaf |
|---|---|---|---|---|---|
| `c:\job\**\Metadata.ini` | 86 | P1 | Mixed | OnEvent / OnLoad | Metadata.ini |
| `c:\job\**\ProductionInfo.ini` | 22 | P1 | Mixed | OnRun (per wafer) | ProductionInfo.ini |

**Key fields to watch (from ParameterDescriptions.json):**

  - `c:\job\**\Metadata.ini`
    - `General.name` — Setup name
    - `General.Id` — Setup unique ID
    - `General.Version` — Schema version
    - `General.JobTag` — Job tag label
    - `General.LastActiveRecipe` — Last active recipe
  - `c:\job\**\ProductionInfo.ini`
    - `General.WaferDefectsCount` — Total defect count on last wafer
    - `General.WaferDefectDiceRatio` — Fraction of dice with at least one defect
    - `General.WaferDefectClassId` — Dominant defect class ID
    - `General.WaferDefectClassIdCount` — Defect count in dominant class
    - `General.BatchPassWafersCount` — Wafers passing yield criteria in this batch

### AlignmentData — AOI_Main

- **Pattern count:** 15 distinct patterns (427 files total)
- **Top monitor priority:** P1
- **Sensitivity:** Critical (highest in group)
- **Write patterns observed:** OnCreate; OnEvent (alignment cycle)
- **Change verdicts:** Mixed; Write-once

| Pattern | Count | Priority | Verdict | Write pattern | Sample leaf |
|---|---|---|---|---|---|
| `c:\job\**\Recipes\*\WaferAlignData\Alignment_*.txt` | 120 | P2 | Write-once | OnEvent (alignment cycle) | Alignment_PatFindRtp.txt |
| `c:\job\**\Wafer2Table.ini` | 49 | P1 | Mixed | OnEvent (alignment cycle) | Wafer2Table.ini |
| `c:\job\**\Recipes\*\Alignment.ini` | 42 | P1 | Mixed | OnEvent (alignment cycle) | Alignment.ini |
| `c:\job\**\Recipes\*\WaferAlignData\AlignmentStatisticsTime.txt` | 40 | P2 | Write-once | OnEvent (alignment cycle) | AlignmentStatisticsTime.txt |
| `c:\job\**\Recipes\*\TrainData\FrameToChuck.ini` | 37 | P2 | Write-once | OnCreate | FrameToChuck.ini |
| `c:\job\**\Recipes\*\WaferAlignData\AlignmentData.ini` | 34 | P1 | Write-once | OnEvent (alignment cycle) | AlignmentData.ini |
| `c:\job\**\CurrWaferSurfaceInterpolation.*` | 31 | P2 | Write-once | OnCreate | CurrWaferSurfaceInterpolation.ini |
| `c:\job\**\Wafer2Table_LastKnown.ini` | 18 | P2 | Write-once | OnCreate | Wafer2Table_LastKnown.ini |
| `c:\job\**\WaferAlignData\WaferManualAlign_*.txt` | 18 | P2 | Write-once | OnCreate | WaferManualAlign_PatFindRtp.txt |
| `c:\job\**\DefaultWaferSurfaceInterpolation.*` | 13 | P3 | Write-once | OnCreate | DefaultWaferSurfaceInterpolation.ini |
| `c:\job\**\WaferAlignData\AlignmentNotFound_rtp.txt` | 8 | P2 | Write-once | OnEvent (alignment cycle) | AlignmentNotFound_rtp.txt |
| `c:\job\**\Recipes\*\FocusMapping\FocusPointsForScan.xml` | 6 | P2 | Write-once | OnCreate | FocusPointsForScan.xml |
| `c:\job\**\Recipes\*\FocusMapping\DieReferenceLocation.json` | 5 | P2 | Write-once | OnCreate | DieReferenceLocation.json |
| `c:\job\**\Recipes\*\WLUP.txt` | 3 | P2 | Write-once | OnCreate | WLUP.txt |
| `c:\job\**\WaferAlignData\WLUP_Align_*.txt` | 3 | P2 | Write-once | OnCreate | WLUP_Align_PatFindRtp.txt |

**Key fields to watch (from ParameterDescriptions.json):**

  - `c:\job\**\Wafer2Table.ini`
    - `WAFER ALIGNMENT.Wafer2Table_X` — Affine matrix X-row (R11, R12, Tx)
    - `WAFER ALIGNMENT.Wafer2Table_Y` — Affine matrix Y-row (R21, R22, Ty)
    - `WAFER ALIGNMENT.Rotate  w2t` — Net wafer rotation angle (degrees)
    - `WAFER ALIGNMENT.Shear   w2t` — Transform shear component
    - `WAFER ALIGNMENT.Stretch w2t` — Scale factors along X and Y
  - `c:\job\**\Recipes\*\Alignment.ini`
    - `General.IsAligned` — Alignment completed successfully
    - `General.RotationResidual` — Rotation residual from alignment (degrees)

### AlignmentData — RMS

- **Pattern count:** 5 distinct patterns (153 files total)
- **Top monitor priority:** P1
- **Sensitivity:** Critical (highest in group)
- **Write patterns observed:** OnCreate; OnEvent (alignment cycle)
- **Change verdicts:** Mixed; Write-once

| Pattern | Count | Priority | Verdict | Write pattern | Sample leaf |
|---|---|---|---|---|---|
| `c:\job\**\DefaultWafer2Table.ini` | 49 | P2 | Write-once | OnCreate | DefaultWafer2Table.ini |
| `c:\job\**\Recipes\*\AlignRtp.ini` | 42 | P1 | Mixed | OnEvent (alignment cycle) | AlignRtp.ini |
| `c:\job\**\Recipes\*\AlignmentData.ini` | 42 | P2 | Write-once | OnEvent (alignment cycle) | AlignmentData.ini |
| `c:\job\**\Recipes\*\Params_AlignRTP.ini` | 19 | P1 | Write-once | OnEvent (alignment cycle) | Params_AlignRTP.ini |
| `c:\job\**\Recipes\*\*\DefaultAlign.ini` | 1 | P3 | Write-once | OnEvent (alignment cycle) | DefaultAlign.ini |

**Key fields to watch (from ParameterDescriptions.json):**

  - `c:\job\**\DefaultWafer2Table.ini`
    - `WAFER ALIGNMENT.Wafer2Table_X` — Affine matrix X-row (R11, R12, Tx)
    - `WAFER ALIGNMENT.Wafer2Table_Y` — Affine matrix Y-row (R21, R22, Ty)
    - `WAFER ALIGNMENT.Rotate  w2t` — Net wafer rotation angle (degrees)
    - `WAFER ALIGNMENT.StdAffine` — Affine fit RMS residual (µm)
    - `General.SaveTime` — Transform saved timestamp
  - `c:\job\**\Recipes\*\AlignRtp.ini`
    - `General.SearchWindowX` — Alignment search window width (µm)
    - `General.SearchWindowY` — Alignment search window height (µm)
    - `General.MinScore` — Minimum match score threshold
  - `c:\job\**\Recipes\*\AlignmentData.ini`
    - `General.MinScore` — Minimum pattern match score (0–100)
    - `General.MinTargetScore` — Minimum target pattern score
    - `General.IsAffineEnabled` — Affine transform enabled (vs. rigid)
    - `General.AlignPointsNum` — Number of alignment points
    - `General.ForceToGMF` — Force GMF matcher for all alignment points

### Audit — FalconAuditService

- **Pattern count:** 1 distinct patterns (24 files total)
- **Top monitor priority:** P2
- **Sensitivity:** High (highest in group)
- **Write patterns observed:** OnEvent (arrival/departure)
- **Change verdicts:** Write-once

| Pattern | Count | Priority | Verdict | Write pattern | Sample leaf |
|---|---|---|---|---|---|
| `c:\job\**\.audit\manifest.json` | 24 | P2 | Write-once | OnEvent (arrival/departure) | manifest.json |

### Config — RMS

- **Pattern count:** 8 distinct patterns (161 files total)
- **Top monitor priority:** P1
- **Sensitivity:** Critical (highest in group)
- **Write patterns observed:** OnCreate; OnEvent / OnLoad
- **Change verdicts:** Mixed; Write-once

| Pattern | Count | Priority | Verdict | Write pattern | Sample leaf |
|---|---|---|---|---|---|
| `c:\job\**\Recipes\*\ExternalCoordSystems.ini` | 42 | P2 | Write-once | OnCreate | ExternalCoordSystems.ini |
| `c:\job\**\Recipes\*\WaferDataReadSettings.xml` | 42 | P2 | Write-once | OnCreate | WaferDataReadSettings.xml |
| `c:\job\**\Recipes\*\*\OpticsPreset.ini` | 26 | P3 | Mixed | OnEvent / OnLoad | OpticsPreset.ini |
| `c:\job\**\Recipes\*\SW_QA-*\OpticsPreset.ini` | 24 | P3 | Write-once | OnCreate | OpticsPreset.ini |
| `c:\job\**\Recipes\*\Params_SystemInfo.ini` | 19 | P1 | Write-once | OnCreate | Params_SystemInfo.ini |
| `c:\job\**\Scripts.ini` | 6 | P2 | Write-once | OnCreate | Scripts.ini |
| `c:\job\**\Recipes\*\TNESetup.ini` | 1 | P2 | Write-once | OnCreate | TNESetup.ini |
| `c:\job\**\MultiLightChannels.ini` | 1 | P2 | Write-once | OnCreate | MultiLightChannels.ini |

**Key fields to watch (from ParameterDescriptions.json):**

  - `c:\job\**\Recipes\*\Params_SystemInfo.ini`
    - `General.MachineType` — Machine type identifier
    - `General.MachineSerial` — Machine serial number
    - `General.SoftwareVersion` — Software version applied to this recipe

### DieMap — RMS

- **Pattern count:** 8 distinct patterns (51 files total)
- **Top monitor priority:** P2
- **Sensitivity:** High (highest in group)
- **Write patterns observed:** OnCreate
- **Change verdicts:** Write-once

| Pattern | Count | Priority | Verdict | Write pattern | Sample leaf |
|---|---|---|---|---|---|
| `c:\job\**\TrainData\ZonesVectorInfo.csv` | 21 | P2 | Write-once | OnCreate | ZonesVectorInfo.csv |
| `c:\job\**\Recipes\*\DieMapRegPos.txt` | 13 | P2 | Write-once | OnCreate | DieMapRegPos.txt |
| `c:\job\**\Recipes\*\DieRegPos.txt` | 8 | P2 | Write-once | OnCreate | DieRegPos.txt |
| `c:\job\**\Recipes\*\DiceInstances.xml` | 4 | P2 | Write-once | OnCreate | DiceInstances.xml |
| `c:\job\**\TrainData\DiceInCad.xml` | 2 | P2 | Write-once | OnCreate | DiceInCad.xml |
| `c:\job\**\TrainData\ScanAreaVectorInfo.csv` | 1 | P2 | Write-once | OnCreate | ScanAreaVectorInfo.csv |
| `c:\job\**\Recipes\*\DieMapping.txt` | 1 | P2 | Write-once | OnCreate | DieMapping.txt |
| `c:\job\**\Recipes\*\DieOffset.txt` | 1 | P2 | Write-once | OnCreate | DieOffset.txt |

### Log — RMS

- **Pattern count:** 1 distinct patterns (41 files total)
- **Top monitor priority:** P2
- **Sensitivity:** Medium (highest in group)
- **Write patterns observed:** OnRun (append/overwrite)
- **Change verdicts:** Write-once

| Pattern | Count | Priority | Verdict | Write pattern | Sample leaf |
|---|---|---|---|---|---|
| `c:\job\**\Recipes\*\.dc_cache\TransactionsHistory.ini` | 41 | P2 | Write-once | OnRun (append/overwrite) | TransactionsHistory.ini |

### Recipe — AOI_Main

- **Pattern count:** 8 distinct patterns (270 files total)
- **Top monitor priority:** P1
- **Sensitivity:** Critical (highest in group)
- **Write patterns observed:** OnCreate; OnEvent / OnLoad
- **Change verdicts:** Mixed; Write-once

| Pattern | Count | Priority | Verdict | Write pattern | Sample leaf |
|---|---|---|---|---|---|
| `c:\job\**\Recipes\*\TrainData\Die.ini` | 42 | P2 | Write-once | OnCreate | Die.ini |
| `c:\job\**\Recipes\*\ProductInfo.ini` | 42 | P1 | Mixed | OnEvent / OnLoad | ProductInfo.ini |
| `c:\job\**\Recipes\*\Waferinfo.ini` | 42 | P1 | Mixed | OnEvent / OnLoad | Waferinfo.ini |
| `c:\job\**\Recipes\*\TrainData\DieImage\ZoomLevels.ini` | 37 | P3 | Write-once | OnCreate | ZoomLevels.ini |
| `c:\job\**\Recipes\*\TrainData\DieImage\DieImageToTable.ini` | 37 | P3 | Write-once | OnCreate | DieImageToTable.ini |
| `c:\job\**\Recipes\*\TrainData\DieRefToTrain.txt` | 37 | P2 | Write-once | OnCreate | DieRefToTrain.txt |
| `c:\job\**\Recipes\*\FocusMapping\FocusMapping.ini` | 27 | P2 | Mixed | OnEvent / OnLoad | FocusMapping.ini |
| `c:\job\**\Recipes\*\FocusMapping\Model_*\FocusModel.ini` | 6 | P2 | Write-once | OnCreate | FocusModel.ini |

**Key fields to watch (from ParameterDescriptions.json):**

  - `c:\job\**\Recipes\*\ProductInfo.ini`
    - `General.DieSizeX` — Die width (µm)
    - `General.DieSizeY` — Die height (µm)
    - `General.DiePitchX` — Die pitch X (µm)
    - `General.DiePitchY` — Die pitch Y (µm)
    - `General.WaferDiameter` — Wafer diameter (mm)
  - `c:\job\**\Recipes\*\Waferinfo.ini`
    - `General.RobotPreAlignerAngle` — Robot pre-aligner rotation angle
    - `General.IsAutoCycleEnabled` — Auto-cycle mode enabled
    - `General.UseInkDot` — Ink-dot detection enabled
    - `General.UseOcr` — OCR identification enabled
    - `General.HwDependencies` — Hardware dependency flags
  - `c:\job\**\Recipes\*\FocusMapping\FocusMapping.ini`
    - `General.SampleCount` — Number of focus sample points
    - `General.FocusMethod` — Focus measurement method
    - `General.ValidMap` — Focus map validity flag

### Config — External tool

- **Pattern count:** 1 distinct patterns (18 files total)
- **Top monitor priority:** P3
- **Sensitivity:** Medium (highest in group)
- **Write patterns observed:** OnCreate
- **Change verdicts:** Write-once

| Pattern | Count | Priority | Verdict | Write pattern | Sample leaf |
|---|---|---|---|---|---|
| `c:\job\*\VcamInstallerGuid.txt` | 18 | P3 | Write-once | OnCreate | VcamInstallerGuid.txt |

### Recipe — RMS

- **Pattern count:** 52 distinct patterns (1784 files total)
- **Top monitor priority:** P1
- **Sensitivity:** Critical (highest in group)
- **Write patterns observed:** OnCreate; OnEvent / OnLoad
- **Change verdicts:** Mixed; Write-once

| Pattern | Count | Priority | Verdict | Write pattern | Sample leaf |
|---|---|---|---|---|---|
| `c:\job\**\Recipes\*\Die_*\*.txt` | 406 | P3 | Write-once | OnCreate | DieMapRegPos.txt |
| `c:\job\**\Recipes\*\Zones\*.ini` | 186 | P1 | Mixed | OnEvent / OnLoad | PostProcess.ini |
| `c:\job\**\Recipes\*\Die_*\ReferenceBackup\ZoomLevels.ini` | 116 | P3 | Write-once | OnCreate | ZoomLevels.ini |
| `c:\job\**\Recipes\*\Die_*\Zones.ini` | 116 | P1 | Write-once | OnCreate | Zones.ini |
| `c:\job\**\Recipes\*\Die_*\ZoomLevels.ini` | 116 | P3 | Write-once | OnCreate | ZoomLevels.ini |
| `c:\job\**\Recipes\*\DieAlignment.dat_block.ini` | 57 | P2 | Mixed | OnEvent / OnLoad | DieAlignment.dat_block.ini |
| `c:\job\**\Recipes\*\WaferMapRecipe.ini` | 44 | P2 | Mixed | OnEvent / OnLoad | WaferMapRecipe.ini |
| `c:\job\**\Recipes\*\CreateReference3dOptions.ini` | 42 | P2 | Write-once | OnCreate | CreateReference3dOptions.ini |
| `c:\job\**\Recipes\*\UniqueArea.ini` | 42 | P2 | Write-once | OnCreate | UniqueArea.ini |
| `c:\job\**\Recipes\*\Recipe.ini` | 42 | P1 | Mixed | OnEvent / OnLoad | Recipe.ini |
| `c:\job\**\Recipes\*\ZoomLevels.ini` | 42 | P1 | Write-once | OnCreate | ZoomLevels.ini |
| `c:\job\**\Recipes\*\zones.ini` | 42 | P1 | Write-once | OnCreate | zones.ini |
| `c:\job\**\Recipes\*\CleanReferenceFinalParams.ini` | 41 | P2 | Write-once | OnCreate | CleanReferenceFinalParams.ini |
| `c:\job\**\Recipes\*\ScenariosMetadatas.ini` | 41 | P2 | Mixed | OnEvent / OnLoad | ScenariosMetadatas.ini |
| `c:\job\**\Recipes\*\ReferenceBackup\ZoomLevels.ini` | 41 | P3 | Write-once | OnCreate | ZoomLevels.ini |
| `c:\job\**\Recipes\*\RTP.txt` | 39 | P1 | Write-once | OnCreate | RTP.txt |
| `c:\job\**\Recipes\*\GlobalRTP.ini` | 39 | P1 | Write-once | OnCreate | GlobalRTP.ini |
| `c:\job\**\Recipes\*\zones.txt` | 37 | P2 | Write-once | OnCreate | zones.txt |
| `c:\job\**\DefectsClustering.ini` | 25 | P1 | Mixed | OnEvent / OnLoad | DefectsClustering.ini |
| `c:\job\**\MultiRecipe.ini` | 25 | P1 | Mixed | OnEvent / OnLoad | MultiRecipe.ini |
| `c:\job\**\Recipes\*\ScenarioMetadataGrab.xml` | 20 | P2 | Write-once | OnCreate | ScenarioMetadataGrab.xml |
| `c:\job\**\Recipes\*\CcsLocalMeas.ini` | 19 | P2 | Write-once | OnCreate | CcsLocalMeas.ini |
| `c:\job\**\Recipes\*\Params_WaferInfo.ini` | 19 | P1 | Write-once | OnCreate | Params_WaferInfo.ini |
| `c:\job\**\Recipes\*\Job.dat` | 19 | P2 | Write-once | OnCreate | Job.dat |
| `c:\job\**\Recipes\*\ZoneVerifyOptics.ini` | 18 | P3 | Write-once | OnCreate | ZoneVerifyOptics.ini |
| `c:\job\**\Recipes\*\DieMapAlignRes.dat_block.ini` | 17 | P2 | Write-once | OnCreate | DieMapAlignRes.dat_block.ini |
| `c:\job\**\Recipes\*\WaferToRefWafer.ini` | 13 | P2 | Write-once | OnCreate | WaferToRefWafer.ini |
| `c:\job\**\Recipes\*\AQL.ini` | 13 | P1 | Write-once | OnCreate | AQL.ini |
| `c:\job\**\Recipes\*\SamplingMetrology.ini` | 13 | P2 | Write-once | OnCreate | SamplingMetrology.ini |
| `c:\job\**\Recipes\*\OverlayScan.ini` | 11 | P2 | Write-once | OnCreate | OverlayScan.ini |
| `c:\job\**\Recipes\*\ReferencesInfo.json` | 11 | P2 | Write-once | OnCreate | ReferencesInfo.json |
| `c:\job\**\Recipes\*\s_*.dat.md` | 9 | P3 | Write-once | OnCreate | s_Bumps.dat.md |
| `c:\job\**\ZoomLevels.ini` | 8 | P2 | Write-once | OnCreate | ZoomLevels.ini |
| `c:\job\**\ScanCondition.ini` | 8 | P1 | Write-once | OnCreate | ScanCondition.ini |
| `c:\job\**\Recipes\*\s_FrameData.dat.md` | 6 | P3 | Write-once | OnCreate | s_FrameData.dat.md |
| `c:\job\**\Recipes\*\CTSOpticSetup\SetupSummary.xml` | 5 | P3 | Write-once | OnCreate | SetupSummary.xml |
| `c:\job\**\TrainData\CadtoJobRecipe.xml` | 4 | P2 | Write-once | OnCreate | CadtoJobRecipe.xml |
| `c:\job\**\Recipes\*\CleanReferenceConfiguration.ini` | 4 | P2 | Write-once | OnCreate | CleanReferenceConfiguration.ini |
| `c:\job\**\RecipesInfo.ini` | 4 | P2 | Write-once | OnCreate | RecipesInfo.ini |
| `c:\job\**\Recipes\*\CTSOpticSetup\Manual\ManualScannedSites\*.xml` | 3 | P2 | Write-once | OnCreate | ManualScannedSites.xml |
| `c:\job\**\Recipes\*\Bumps.dat` | 3 | P2 | Write-once | OnCreate | Bumps.dat |
| `c:\job\**\Recipes\*\CTSOpticSetup\Manual\Sites\Sites.xml` | 3 | P2 | Write-once | OnCreate | Sites.xml |
| `c:\job\**\Recipes\*\Sites*.xml` | 3 | P3 | Write-once | OnCreate | SitesInWafer.xml |
| `c:\job\**\ScanCTS_*\ScanCTS_*.xml` | 3 | P2 | Write-once | OnCreate | ScanCTS_1.xml |
| `c:\job\**\TrainData\CadReferenceMetaData.xml` | 2 | P2 | Write-once | OnCreate | CadReferenceMetaData.xml |
| `c:\job\**\Recipes\*\EBRInspectionParameters.xml` | 1 | P1 | Write-once | OnCreate | EBRInspectionParameters.xml |
| `c:\job\**\Transitions.xml` | 1 | P3 | Write-once | OnCreate | Transitions.xml |
| `c:\job\**\Zones.ini` | 1 | P1 | Write-once | OnCreate | Zones.ini |
| `c:\job\**\Recipes\*\ManualMasking.xml` | 1 | P2 | Write-once | OnCreate | ManualMasking.xml |
| `c:\job\**\Recipes\*\CcsSetup.xml` | 1 | P2 | Write-once | OnCreate | CcsSetup.xml |
| `c:\job\**\Recipes\*\CadSegmentCleanReference.json` | 1 | P2 | Write-once | OnCreate | CadSegmentCleanReference.json |
| `c:\job\**\Recipes\*\RecipeScanRestriction.ini` | 1 | P2 | Write-once | OnCreate | RecipeScanRestriction.ini |

**Key fields to watch (from ParameterDescriptions.json):**

  - `c:\job\**\Recipes\*\Recipe.ini`
    - `AutoCycle.AutoFocusBeforeAlignment` — Autofocus before alignment
    - `AutoCycle.AutoFocusEvery` — Autofocus frequency
    - `AutoCycle.CleanReferenceEvery` — Clean reference frequency
    - `AutoCycle.UnloadToAnotherCassette` — Unload to cassette B after scan
    - `AutoCycle.NewCleanReferenceOption` — Clean reference creation method
  - `c:\job\**\Recipes\*\ZoomLevels.ini`
    - `General.ZoomLevelCount` — Number of zoom levels defined
    - `General.ActiveZoomLevel` — Currently active zoom level
  - `c:\job\**\Recipes\*\zones.ini`
    - `General.ZoneCount` — Number of scan zones defined
    - `General.ActiveZones` — Active zone list
  - `c:\job\**\Recipes\*\GlobalRTP.ini`
    - `General.SensitivityLevel` — Global detection sensitivity level
    - `General.ThresholdMode` — Threshold calculation mode
  - `c:\job\**\DefectsClustering.ini`
    - `General.Enabled` — Defect clustering enabled
    - `General.ClusteringAfterMerge` — Apply clustering after multi-recipe merge
    - `General.Distance` — Max center-to-center clustering distance (µm)
    - `General.SelectedFirstSortingList` — Primary defect sort field
    - `General.SelectedSecondSortingList` — Secondary defect sort field
  - `c:\job\**\MultiRecipe.ini`
    - `Scan.MergingThreadCount` — Parallel merge threads
    - `Scan.doSeparateRecipe` — Run recipes as separate scan passes
    - `Scan.DoGrabAfterMerge` — Perform image grab after merge
    - `Scan.Recipes` — Recipe slot enable flags
    - `Scan.FieldCount` — Fields per recipe definition line
  - `c:\job\**\ScanCondition.ini`
    - `General.IsActive` — Scan condition check enforced
  - `c:\job\**\Recipes\*\CleanReferenceConfiguration.ini`
    - `General.AveragingCount` — Number of frames averaged for clean reference
    - `General.OutlierRejection` — Outlier rejection enabled
    - `General.AcceptanceThreshold` — Acceptance quality threshold

### ScanResult — AOI_Main

- **Pattern count:** 6 distinct patterns (63 files total)
- **Top monitor priority:** P2
- **Sensitivity:** Medium (highest in group)
- **Write patterns observed:** OnCreate; OnRun (per scan)
- **Change verdicts:** Mixed; Write-once

| Pattern | Count | Priority | Verdict | Write pattern | Sample leaf |
|---|---|---|---|---|---|
| `c:\job\**\Recipes\*\ScanOverlapLog.txt` | 41 | P3 | Mixed | OnRun (per scan) | ScanOverlapLog.txt |
| `c:\job\**\Recipes\*\ScanOverviewImage_*.txt` | 13 | P3 | Write-once | OnRun (per scan) | ScanOverviewImage_Recipes-R1.txt |
| `c:\job\**\CTWRepeatabilityReport.xml` | 6 | P2 | Write-once | OnCreate | CTWRepeatabilityReport.xml |
| `c:\job\**\Recipes\*\UniqueResultTypeIds.ini` | 1 | P3 | Write-once | OnCreate | UniqueResultTypeIds.ini |
| `c:\job\**\Recipes\*\ScanOverlapLog3d.txt` | 1 | P3 | Write-once | OnRun (per scan) | ScanOverlapLog3d.txt |
| `c:\job\**\Recipes\*\ProcessingRef\*.xml` | 1 | P3 | Write-once | OnCreate | TilePoolData.xml |

### Log — AOI_Main

- **Pattern count:** 3 distinct patterns (550 files total)
- **Top monitor priority:** P4
- **Sensitivity:** Low (highest in group)
- **Write patterns observed:** OnRun (append/overwrite)
- **Change verdicts:** Write-once

| Pattern | Count | Priority | Verdict | Write pattern | Sample leaf |
|---|---|---|---|---|---|
| `c:\job\**\DebugAFMapping*\*_focusCurve.txt` | 521 | P4 | Write-once | OnRun (append/overwrite) | 108317,  19209_focusCurve.txt |
| `c:\job\**\Recipes\*\ImageProcessing.log` | 19 | P4 | Write-once | OnRun (append/overwrite) | ImageProcessing.log |
| `c:\job\**\DebugAFMapping*\FocusMappingDebug*.txt` | 10 | P4 | Write-once | OnRun (append/overwrite) | FocusMappingDebug_AllMags.txt |
