# Diff -- FileClassificationRules.json vs 02_file_summary.md

> Inputs:
> - `filesDiscovery/FileClassificationRules.json` -- authoritative rules (69 patterns)
> - `filesDiscovery/output/02_file_summary.md` -- classification table (115 rows)
>
> Pattern comparison is case-insensitive; classification fields (module / ownerService / monitorPriority) are compared verbatim.

## Summary

| Bucket | Count |
|---|---|
| Patterns in JSON only (rule defined, no row in MD table) | 8 |
| Patterns in MD only (analyst-added, no rule in JSON)     | 54 |
| Patterns in both, classification mismatch                | 0 |
| Patterns in both, classification agrees                  | 61 |

## Section 1 -- In JSON only (rule exists, not surfaced in MD table)

| Pattern | Module | Owner | Priority |
|---|---|---|---|
| `c:\job\*\metadata.ini` | Job | RMS | P1 |
| `c:\job\**\recipes\*\defaultwafer2table.ini` | AlignmentData | RMS | P2 |
| `c:\job\**\recipes\*\waferinfo.dat` | Recipe | RMS | P2 |
| `c:\job\**\recipes\*\diemapping.dat` | DieMap | RMS | P2 |
| `c:\job\**\recipes\*\dieregpos.dat` | DieMap | RMS | P2 |
| `c:\job\**\recipes\*\diemapregpos.dat` | DieMap | RMS | P2 |
| `c:\job\**\recipes\*\zones.dat` | DieMap | RMS | P2 |
| `c:\job\**\recipes\*\scanoverviewimage.txt` | ScanResult | AOI_Main | P3 |

## Section 2 -- In MD only (analyst-added, not in JSON rules)

| Row | Pattern | Module | Owner | Priority |
|---|---|---|---|---|
| 14 | `c:\job\**\recipes\*\die_*\zones.ini` | Recipe | RMS | P1 |
| 25 | `c:\job\**\recipes\*\aql.ini` | Recipe | RMS | P1 |
| 27 | `c:\job\**\recipes\*\ebrinspectionparameters.xml` | Recipe | RMS | P1 |
| 28 | `c:\job\**\zones.ini` | Recipe | RMS | P1 |
| 34 | `c:\job\**\currwafersurfaceinterpolation.*` | AlignmentData | AOI_Main | P2 |
| 35 | `c:\job\**\waferaligndata\wafermanualalign_*.txt` | AlignmentData | AOI_Main | P2 |
| 36 | `c:\job\**\wafer2table_lastknown.ini` | AlignmentData | AOI_Main | P2 |
| 37 | `c:\job\**\waferaligndata\alignmentnotfound_rtp.txt` | AlignmentData | AOI_Main | P2 |
| 38 | `c:\job\**\recipes\*\focusmapping\focuspointsforscan.xml` | AlignmentData | AOI_Main | P2 |
| 40 | `c:\job\**\waferaligndata\wlup_align_*.txt` | AlignmentData | AOI_Main | P2 |
| 41 | `c:\job\**\recipes\*\wlup.txt` | AlignmentData | AOI_Main | P2 |
| 42 | `c:\job\**\.audit\manifest.json` | Audit | FalconAuditService | P2 |
| 46 | `c:\job\**\scripts.ini` | Config | RMS | P2 |
| 47 | `c:\job\**\multilightchannels.ini` | Config | RMS | P2 |
| 48 | `c:\job\**\recipes\*\tnesetup.ini` | Config | RMS | P2 |
| 49 | `c:\job\**\traindata\zonesvectorinfo.csv` | DieMap | RMS | P2 |
| 52 | `c:\job\**\recipes\*\diceinstances.xml` | DieMap | RMS | P2 |
| 53 | `c:\job\**\traindata\diceincad.xml` | DieMap | RMS | P2 |
| 54 | `c:\job\**\traindata\scanareavectorinfo.csv` | DieMap | RMS | P2 |
| 55 | `c:\job\**\recipes\*\diemapping.txt` | DieMap | RMS | P2 |
| 56 | `c:\job\**\recipes\*\dieoffset.txt` | DieMap | RMS | P2 |
| 68 | `c:\job\**\recipes\*\scenariometadatagrab.xml` | Recipe | RMS | P2 |
| 69 | `c:\job\**\recipes\*\job.dat` | Recipe | RMS | P2 |
| 76 | `c:\job\**\zoomlevels.ini` | Recipe | RMS | P2 |
| 77 | `c:\job\**\recipes\*\focusmapping\model_*\focusmodel.ini` | Recipe | AOI_Main | P2 |
| 79 | `c:\job\**\traindata\cadtojobrecipe.xml` | Recipe | RMS | P2 |
| 80 | `c:\job\**\recipesinfo.ini` | Recipe | RMS | P2 |
| 81 | `c:\job\**\scancts_*\scancts_*.xml` | Recipe | RMS | P2 |
| 82 | `c:\job\**\recipes\*\ctsopticsetup\manual\sites\sites.xml` | Recipe | RMS | P2 |
| 83 | `c:\job\**\recipes\*\bumps.dat` | Recipe | RMS | P2 |
| 84 | `c:\job\**\recipes\*\ctsopticsetup\manual\manualscannedsites\*.xml` | Recipe | RMS | P2 |
| 85 | `c:\job\**\traindata\cadreferencemetadata.xml` | Recipe | RMS | P2 |
| 86 | `c:\job\**\recipes\*\manualmasking.xml` | Recipe | RMS | P2 |
| 88 | `c:\job\**\recipes\*\cadsegmentcleanreference.json` | Recipe | RMS | P2 |
| 89 | `c:\job\**\recipes\*\recipescanrestriction.ini` | Recipe | RMS | P2 |
| 90 | `c:\job\**\ctwrepeatabilityreport.xml` | ScanResult | AOI_Main | P2 |
| 91 | `c:\job\**\defaultwafersurfaceinterpolation.*` | AlignmentData | AOI_Main | P3 |
| 92 | `c:\job\**\recipes\*\*\defaultalign.ini` | AlignmentData | RMS | P3 |
| 93 | `c:\job\**\recipes\*\*\opticspreset.ini` | Config | RMS | P3 |
| 95 | `c:\job\*\vcaminstallerguid.txt` | Config | External tool | P3 |
| 96 | `c:\job\**\recipes\*\die_*\*.txt` | Recipe | RMS | P3 |
| 97 | `c:\job\**\recipes\*\die_*\zoomlevels.ini` | Recipe | RMS | P3 |
| 98 | `c:\job\**\recipes\*\die_*\referencebackup\zoomlevels.ini` | Recipe | RMS | P3 |
| 102 | `c:\job\**\recipes\*\zoneverifyoptics.ini` | Recipe | RMS | P3 |
| 103 | `c:\job\**\recipes\*\s_*.dat.md` | Recipe | RMS | P3 |
| 105 | `c:\job\**\recipes\*\ctsopticsetup\setupsummary.xml` | Recipe | RMS | P3 |
| 106 | `c:\job\**\recipes\*\sites*.xml` | Recipe | RMS | P3 |
| 107 | `c:\job\**\transitions.xml` | Recipe | RMS | P3 |
| 109 | `c:\job\**\recipes\*\scanoverviewimage_*.txt` | ScanResult | AOI_Main | P3 |
| 110 | `c:\job\**\recipes\*\processingref\*.xml` | ScanResult | AOI_Main | P3 |
| 111 | `c:\job\**\recipes\*\uniqueresulttypeids.ini` | ScanResult | AOI_Main | P3 |
| 112 | `c:\job\**\recipes\*\scanoverlaplog3d.txt` | ScanResult | AOI_Main | P3 |
| 113 | `c:\job\**\debugafmapping*\*_focuscurve.txt` | Log | AOI_Main | P4 |
| 115 | `c:\job\**\debugafmapping*\focusmappingdebug*.txt` | Log | AOI_Main | P4 |

## Section 3 -- Classification mismatches (pattern in both)

_None -- all overlapping patterns agree on module / owner / priority._

