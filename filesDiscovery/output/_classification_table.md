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
