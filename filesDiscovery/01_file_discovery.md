# Audit Log — Prompt 1: File Discovery

> **Goal:** Recursively scan `c:\job\` and produce a complete inventory of every non-binary file
> that exists on the machine. Do NOT classify or propose solutions yet — only gather facts.
> **Run this first.**

---

You are a **senior system analyst**.
You have access to a Camtek Falcon AOI/EBI inspection machine with the standard deployment layout.
use the code base and The `system.md` document in this folder describes the relevant services.

Your task is to scan the codebase c:\CamtekGit and find all the places that create / update files and document every file , then also scan the `c:\job\` directory tree and document every file that is readable as text.


---

## Section 1 — Scan Instructions

Execute or simulate the following scan on the target machine:

1. Recursively list all files under `c:\job\` (all subdirectories, all depths).
2. **Include** files with these extensions (case-insensitive):
   `.txt`, `.ini`, `.json`, `.xml`, `.csv`, `.log`, `.yaml`, `.yml`, `.cfg`, `.dat`, `.seq`, `.md`, `.properties`, `.conf`, `.config`, `.bat`, `.cmd`, `.ps1`, `.sql`
3. **Exclude** files with these extensions (binary / image / compiled):
   `.exe`, `.dll`, `.pdb`, `.bin`, `.img`, `.bmp`, `.tiff`, `.tif`, `.jpg`, `.jpeg`, `.png`, `.gif`,
   `.db`, `.sqlite`, `.mdb`, `.ldf`, `.mdf`, `.zip`, `.gz`, `.7z`, `.rar`,
   `.obj`, `.lib`, `.pyd`, `.pyc`, `.class`
4. Also exclude any file whose first 512 bytes contain more than 30% non-printable characters (binary heuristic).
5. For each included file, capture:
   - **Full path** (absolute)
   - **Extension**
   - **Size** (bytes)
   - **Last modified** timestamp (`YYYY-MM-DD HH:MM`)
   - **Created** timestamp (`YYYY-MM-DD HH:MM`)
   - **First line** or first 120 characters of content (to identify file purpose)
   - **Writable by SYSTEM?** (yes / no / unknown)

---

## Section 2 — Scan codebase Instructions

Execute or simulate the following scan on the target machine:

1. list all files that the code is creating/updating/deleteing that locatated under  `c:\job\` (all subdirectories, all depths).
2. **Include** files with these extensions (case-insensitive):
   `.txt`, `.ini`, `.json`, `.xml`, `.csv`, `.log`, `.yaml`, `.yml`, `.cfg`, `.dat`, `.seq`, `.md`, `.properties`, `.conf`, `.config`, `.bat`, `.cmd`, `.ps1`, `.sql`
3. **Exclude** files with these extensions (binary / image / compiled):
   `.exe`, `.dll`, `.pdb`, `.bin`, `.img`, `.bmp`, `.tiff`, `.tif`, `.jpg`, `.jpeg`, `.png`, `.gif`,
   `.db`, `.sqlite`, `.mdb`, `.ldf`, `.mdf`, `.zip`, `.gz`, `.7z`, `.rar`,
   `.obj`, `.lib`, `.pyd`, `.pyc`, `.class`
4. Also exclude any file whose first 512 bytes contain more than 30% non-printable characters (binary heuristic).
5. For each included file, capture:
   - **Full path** (absolute)
   - **Extension**
   - **Size** (bytes)
   - **Last modified** timestamp (`YYYY-MM-DD HH:MM`)
   - **Created** timestamp (`YYYY-MM-DD HH:MM`)
   - **First line** or first 120 characters of content (to identify file purpose)
   - **Writable by SYSTEM?** (yes / no / unknown)

---

## Section 3 — Directory Structure

After listing individual files, also document:

1. The top-level subdirectory structure of `c:\job\` (one level deep) — list each folder name and its apparent purpose.
2. Are there **per-job subdirectories** (e.g., `c:\job\<JobName>\...`)? If so, describe the naming pattern.
3. Are there **shared/global** files at `c:\job\` root level (not inside job subdirs)?
4. Approximate **total file count** and **total size** of the non-binary inventory.

---

## Section 4 — Change Indicators

For each file or file group, note any evidence of **who writes it**:

1. Does the file have a **write lock** open (check with `handle.exe` or `Process Monitor` if available)?
2. Does the **file name** or path suggest a specific service (e.g., `rms_`, `falcon_`, `aoi_`, `dds_`)?
3. Does the **content** reference a known service, module, or process name?
4. Is the file written **once at job creation** or **continuously updated during a run**?
   (Use last-modified vs created timestamp gap as a heuristic — if they differ by more than 10 minutes, it is likely continuously updated.)

---

## Output Format

Produce a **file inventory document** with these sections:

### Directory Tree
```
c:\job\
├── <dir>/
│   ├── <file>  (<size>, last modified: <date>)
│   └── ...
└── ...
```

### Full File Inventory Table

| # | Full Path | Ext | Size (B) | Last Modified | Created | First 120 chars / purpose hint | Written continuously? |
|---|---|---|---|---|---|---|---|
| 1 | `c:\job\...` | `.xml` | 4821 | 2024-03-15 09:12 | 2024-03-10 14:00 | `<JobDef name="WaferA"...` | No |
| ... | | | | | | | |

### Summary Counts

| Extension | File count | Total size |
|---|---|---|
| `.xml` | 12 | 48 KB |
| ... | | |

Do NOT classify by module yet — that is Prompt 2.
Do NOT propose solutions yet.

Save the final document to:

`output/01_discovered_files.md`
