# Audit Log — Prompt 2: File Classification & Summary

> **Goal:** Using the file inventory from Prompt 1, classify every file by module, owner service,
> write pattern, and sensitivity. Produce a per-group summary that will be the foundation for
> the monitoring service design.
> **Input required:** `output/01_discovered_files.md`

---

You are a **senior software architect** familiar with the Camtek Falcon BIS platform.
You have the file inventory produced by Prompt 1 (`output/01_discovered_files.md`).
The `system.md` document in this folder describes the services and their roles.
yue have the codebae.

Your task is to classify every file in the inventory and summarize each group.
Do NOT propose monitoring solutions yet — only classify and summarize facts.

---

## Section 1 — Module Classification

For each file (or file pattern) in the inventory, determine:

1. **Module** — which functional area does this file belong to?
   - `Job` — job definition, job parameters
   - `Recipe` — inspection recipe, scan recipe
   - `Config` — machine or application configuration
   - `AlignmentData` — alignment offsets, reference coordinates
   - `ScanResult` — intermediate or final scan output
   - `Log` — runtime log, error log, event log
   - `DieMap` — die layout, exclusion map
   - `Sequence` — scan sequence, step definition
   - `Unknown` — cannot determine from content

2. **Hardware scope** — does this file relate to a specific hardware component?
   - `Camera` — camera-specific settings
   - `Illumination` — light settings, intensity tables
   - `Robot/EFEM` — loader, handler settings
   - `Stage` — motion stage calibration
   - `Global` — not hardware-specific
   - `Unknown`

3. **Owner service** — which Falcon service last writes this file?
   Use the service table in `system.md` and the content hints from Prompt 1.
   - `RMS`, `Falcon.Net`, `AOI_Main`, `DataServer`, `JobSelect.Net`, `External tool`, `Unknown`

---

## Section 2 — Write Pattern Analysis

For each file or group, classify the write pattern:

| Pattern | Definition |
|---|---|
| `OnCreate` | Written once when the job/recipe is first created; rarely or never modified afterward |
| `OnLoad` | Overwritten each time the job is loaded into the machine |
| `OnRun` | Updated continuously or periodically during an active scan run |
| `OnEvent` | Written only when a specific event occurs (alignment change, error, calibration) |
| `OnClose` | Written when a job finishes or is unloaded |
| `Unknown` | Cannot determine from available data |

Provide evidence for each classification (e.g., "last-modified vs created gap > 10 min on multiple files → `OnRun`").

---

## Section 3 — Sensitivity Classification

Rate each file group by audit sensitivity:

| Level | Meaning |
|---|---|
| `Critical` | Change could affect inspection result or machine safety (recipe, config, die map) |
| `High` | Change affects traceability or job repeatability (job definition, sequence) |
| `Medium` | Useful for debugging but not safety-critical (alignment data, scan result metadata) |
| `Low` | Informational only (logs, notes) |

---

## Section 4 — Monitoring Priority

Based on the above, assign a monitoring priority to each file group:

| Priority | Meaning |
|---|---|
| `P1` | Must be monitored — change must be recorded immediately with content hash |
| `P2` | Should be monitored — changes recorded, full diff optional |
| `P3` | Nice to have — record existence of change, no diff required |
| `P4` | Skip — too noisy or low value (e.g., rapidly-rotating log files) |

---

## Output Format

### Classification Table

| # | File pattern / path | Module | Hardware scope | Owner service | Write pattern | Sensitivity | Monitor priority |
|---|---|---|---|---|---|---|---|
| 1 | `c:\job\*.xml` | Job | Global | RMS | OnCreate / OnLoad | Critical | P1 |
| 2 | `c:\job\<name>\*.ini` | Config | Global | Falcon.Net | OnLoad | Critical | P1 |
| ... | | | | | | | |

### Per-Group Summary

For each distinct group (by Module + Owner), produce a summary block:

```
## [Module] — [Owner Service]
- **File pattern:** `c:\job\...`
- **Typical count per job:** N files
- **Write pattern:** ...
- **Sensitivity:** ...
- **Monitor priority:** P1/P2/P3/P4
- **Key fields to watch:** (list top 3–5 field names inside the file, if structured)
- **Notes / unknowns:** ...
```

### Monitoring Scope Summary

End with a table showing what will and won't be monitored:

| Decision | File groups | Reason |
|---|---|---|
| Monitor (P1/P2) | ... | ... |
| Skip (P3/P4) | ... | ... |

Save the final document to:

`output/02_file_summary.md`

Do NOT propose solutions yet.
