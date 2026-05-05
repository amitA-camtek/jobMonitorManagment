# Plan: CI/CD Integration — FalconAuditService into CamtekGit

## Context

FalconAuditService is a .NET 8 service that watches `C:\job\` and maintains a tamper-evident audit log for every job processed by the BIS/Falcon AOI machine. It currently lives in a standalone GitHub repo (`amitA-camtek/jobMonitorManagment`). The goal is to integrate it into the corporate CamtekGit repo so it is built alongside the rest of BIS, and to bind its process lifecycle to AOI: started by `RunAOI.bat`, killed by `Kill_AOIEx.cmd`.

**Lifecycle model (revised):** Run as a regular .exe — not a Windows Service. `RunAOI.bat` launches `FalconAuditService.exe` **before** `AOI_main.exe`; `Kill_AOIEx.cmd` taskkills it last (after SystemLogger) so it captures the full AOI shutdown.

**Why RunAOI.bat and not AOI_Start_Script.cmd:** AOI_Main itself runs `C:\bis\bin\AOI_Start_Script.cmd` on startup (configurable via INI key `general.ScriptOnAOIStartPath`, see `clsInitAOI.cs::RunScriptOnAOIStart`). That hook runs *after* AOI_Main has already begun initializing — there would be a brief window where AOI is touching files and the audit service isn't up yet. Launching from `RunAOI.bat` puts the audit service online **before** AOI_Main even starts, eliminating that race.

**No code changes** to FalconAuditService are required. The existing `UseWindowsService()` call in `Program.cs` is a no-op when the process is launched outside the Service Control Manager — the same exe runs cleanly as a console / background process.

**Safety check:** AOI_Main's startup (`clsInitAOI.cs::InitPreparation`) kills 10 stale processes — `FalconAuditService` is **not** in that list, so launching it before AOI_Main does not cause AOI to kill it.

---

## What will be created / modified

| Path in CamtekGit | Action |
|---|---|
| `BIS\Sources\apps\FalconAuditService\` | **New** — entire source tree copied from jobMonitorManagment |
| `BIS\Sources\apps\FalconAuditService\NuGet.config` | **New** — project-scoped NuGet config (adds nuget.org alongside local feed) |
| `BIS\Sources\apps\FalconAuditService\azure-pipelines.yml` | **New** — standalone Azure DevOps CI pipeline |
| `BIS\Sources\apps\FalconAuditService\install.ps1` | **Modified** — strip Windows-Service registration; keep file deploy + log dir prep |
| `BIS\bin\Scripts\RunAOI.bat` | **Modified** — launch FalconAuditService before AOI_main |
| `BIS\bin\Kill_AOIEx.cmd` | **Modified** — taskkill FalconAuditService in the final section |

---

## Step 1 — Copy source into CamtekGit

```
robocopy "c:\Amit\jobMonitorManagment\Production\FalconAuditService" ^
         "C:\CamtekGit\BIS\Sources\apps\FalconAuditService" ^
         /E /XD bin obj .git .vs .idea /XF *.user *.suo *.log *.db *.db-shm *.db-wal
```

Includes: `FalconAuditService.slnx`, `FalconAuditWebServer.csproj` (net8.0-windows), all `*.cs`, `appsettings*.json`, `FileClassificationRules.json`, `ParameterDescriptions.json`, `install.ps1`.

---

## Step 2 — Add project-scoped NuGet.config

Create `C:\CamtekGit\BIS\Sources\apps\FalconAuditService\NuGet.config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
    <add key="LocalFeed" value="C:\CamtekGit\Packages" />
  </packageSources>
</configuration>
```

Picked up automatically by `dotnet restore/publish` (parent dir of csproj). Does **not** modify the repo-wide `BIS\build\NuGet.config`. Required public packages: DiffPlex, Microsoft.Data.Sqlite, Microsoft.Extensions.Hosting.WindowsServices, Microsoft.AspNetCore.Authentication.Negotiate, Serilog.* (5 packages), Swashbuckle.AspNetCore.

---

## Step 3 — Azure DevOps pipeline

Create `C:\CamtekGit\BIS\Sources\apps\FalconAuditService\azure-pipelines.yml`:

```yaml
name: FalconAuditService - $(Build.SourceBranchName) - $(Date:yyyyMMdd).$(Rev:r)

trigger:
  branches: { include: [main, dev/*, feature/*] }
  paths:    { include: [BIS/Sources/apps/FalconAuditService/**] }

pr:
  branches: { include: [main, dev/*] }
  paths:    { include: [BIS/Sources/apps/FalconAuditService/**] }

pool:
  name: W10VS2022

variables:
  BUILD_CONFIG: Release
  RUNTIME_ID:   win-x64
  PROJECT:      BIS\Sources\apps\FalconAuditService\FalconAuditWebServer\FalconAuditWebServer.csproj
  OUT_DIR:      $(Build.ArtifactStagingDirectory)\FalconAuditService

stages:
  - stage: Build
    jobs:
      - job: BuildPublish
        timeoutInMinutes: 20
        steps:
          - task: UseDotNet@2
            displayName: Use .NET 8 SDK
            inputs: { packageType: sdk, version: 8.x }

          - task: DotNetCoreCLI@2
            displayName: dotnet restore
            inputs:
              command: restore
              projects: $(PROJECT)
              arguments: --configfile BIS\Sources\apps\FalconAuditService\NuGet.config

          - task: DotNetCoreCLI@2
            displayName: dotnet publish (self-contained single-file win-x64)
            inputs:
              command: publish
              projects: $(PROJECT)
              arguments: >
                -c $(BUILD_CONFIG) -r $(RUNTIME_ID)
                --self-contained true
                /p:PublishSingleFile=true
                /p:IncludeNativeLibrariesForSelfExtract=true
                --no-restore -o $(OUT_DIR)
              zipAfterPublish: false
              modifyOutputPath: false

          - task: PowerShell@2
            displayName: Verify output
            inputs:
              targetType: inline
              script: |
                $exe = "$(OUT_DIR)\FalconAuditService.exe"
                if (-not (Test-Path $exe)) { Write-Error "Missing: $exe"; exit 1 }
                Write-Host "OK: $exe  ($([int]((Get-Item $exe).Length/1MB)) MB)"

          - task: PublishBuildArtifacts@1
            displayName: Publish artifact
            inputs:
              PathtoPublish: $(OUT_DIR)
              ArtifactName:  FalconAuditService
              publishLocation: Container
```

**After committing**, register in Azure DevOps:
> Pipelines → New pipeline → Azure Repos Git → CamtekGit → Existing YAML
> → path `BIS/Sources/apps/FalconAuditService/azure-pipelines.yml`

Artifact contents: `FalconAuditService.exe` (~100 MB self-contained), `appsettings.json`, `FileClassificationRules.json`, `ParameterDescriptions.json`.

---

## Step 4 — Simplify install.ps1 (file deploy only, no service)

Replace `install.ps1` with a deploy-only version. Strip all `sc.exe create/start`, virtual-account ACLs, and recovery configuration:

```powershell
param(
    [string]$InstallPath = "C:\bis\bin\FalconAuditService",
    [string]$DataPath    = "C:\bis\auditlog",
    [string]$LogPath     = "C:\bis\ErrorLog\AuditLog"
)

$ErrorActionPreference = "Stop"

# 1. Validate the published exe sits beside this script
$exe = Join-Path $PSScriptRoot "FalconAuditService.exe"
if (-not (Test-Path $exe)) {
    throw "FalconAuditService.exe not found beside install.ps1 ($exe)"
}

# 2. Create runtime directories
New-Item -ItemType Directory -Force -Path $DataPath, $LogPath | Out-Null

# 3. Seed config files into the data dir if absent
foreach ($f in @("FileClassificationRules.json", "ParameterDescriptions.json")) {
    $src = Join-Path $PSScriptRoot $f
    $dst = Join-Path $DataPath $f
    if ((Test-Path $src) -and (-not (Test-Path $dst))) {
        Copy-Item $src $dst
    }
}

Write-Host "FalconAuditService deployed to $InstallPath"
Write-Host "Data:  $DataPath"
Write-Host "Logs:  $LogPath"
Write-Host "It will be launched by RunAOI.bat alongside AOI_Main.exe."
```

Also remove the `uninstall.ps1` if present — no service to remove.

---

## Step 5 — Update RunAOI.bat (launch audit service first)

Replace `C:\CamtekGit\BIS\bin\Scripts\RunAOI.bat` with:

```bat
@echo off
setlocal

set AUDIT_EXE=C:\bis\bin\FalconAuditService\FalconAuditService.exe
set AOI_EXE=C:\bis\bin\AOI_main.exe

:: Start FalconAuditService if not already running
tasklist /FI "IMAGENAME eq FalconAuditService.exe" 2>nul | findstr /I "FalconAuditService.exe" >nul
if errorlevel 1 (
    if exist "%AUDIT_EXE%" (
        echo [RunAOI] Starting FalconAuditService...
        START "" "%AUDIT_EXE%"
        :: Brief delay to let it bind port 5100 before AOI starts
        timeout /t 2 /nobreak >nul
    ) else (
        echo [RunAOI] WARNING: FalconAuditService not installed - starting AOI without audit logging.
    )
) else (
    echo [RunAOI] FalconAuditService already running.
)

START "" "%AOI_EXE%"
endlocal
```

The `tasklist` guard handles the case where the operator double-launches AOI without first stopping it — we don't want a second audit-service instance fighting for port 5100.

---

## Step 6 — Update Kill_AOIEx.cmd (taskkill audit service last)

Modify `C:\CamtekGit\BIS\bin\Kill_AOIEx.cmd`. Add a `Taskkill` line for FalconAuditService in the **Final** section, *after* `Kill SystemLogger.exe` and *before* the `.mmf` cleanup, so the audit service captures every shutdown event including SystemLogger's death:

```
::================Final================================================================
Kill SystemLogger.exe
Taskkill /F /T /IM FalconAuditService.exe
del %FALCON_TEMP_FILES%\*.mmf /Q
@echo %USERNAME% %date% %time% After Kill_AOI.cmd >> "C:\bis\errorlog\FalconLog.txt"
```

**Do NOT** add it to `Kill_AOI_ClientsEx.cmd` — that script kills only the operator-facing client UIs and leaves service-tier processes alive. The audit service belongs in the service tier.

---

## Step 7 — First-time deployment on each BIS machine

After the pipeline produces an artifact:

```powershell
# Copy artifact to install location (no admin needed if user owns C:\bis\bin)
robocopy "<artifact-drop>\FalconAuditService" "C:\bis\bin\FalconAuditService" /MIR

# One-time prep (creates auditlog and AuditLog dirs, seeds JSON config)
cd C:\bis\bin\FalconAuditService
.\install.ps1
```

After this, the operator simply runs `RunAOI.bat` as usual — audit service comes up first, AOI follows.

---

## Verification

1. **Build pipeline:** trigger manually in Azure DevOps → confirm green, artifact contains exe + 3 JSON files.
2. **Cold start:** with no FalconAuditService.exe running, run `RunAOI.bat` → console prints `Starting FalconAuditService...`, then `tasklist | findstr FalconAuditService` shows it, then AOI window appears.
3. **Already-running guard:** run `RunAOI.bat` again → console prints `FalconAuditService already running.`, no second instance starts (`tasklist` still shows exactly one).
4. **Audit log:** drop a file into `C:\job\<test-job>\` → verify `C:\bis\auditlog\` gets a new SQLite shard, `http://localhost:5100/api/jobs` returns it.
5. **Shutdown:** run `Kill_AOI.cmd` → `tasklist` after completion shows neither AOI_Main.exe nor FalconAuditService.exe; the last entry in `C:\bis\ErrorLog\AuditLog\falconaudit-*.log` should be near the kill timestamp.
6. **Client-only kill is safe:** start AOI, run `Kill_AOI_Clients.cmd` → AOI_Main.exe dies but FalconAuditService.exe stays running (service tier intact).

---

## Key file paths

| File | Purpose |
|---|---|
| `c:\Amit\jobMonitorManagment\Production\FalconAuditService\` | Source to migrate from |
| `C:\CamtekGit\BIS\Sources\apps\FalconAuditService\` | Destination in CamtekGit |
| `C:\CamtekGit\BIS\Sources\apps\FalconAuditService\azure-pipelines.yml` | New CI pipeline |
| `C:\CamtekGit\BIS\Sources\apps\FalconAuditService\NuGet.config` | Project-scoped NuGet feed |
| `C:\CamtekGit\BIS\Sources\apps\FalconAuditService\install.ps1` | Simplified deploy-only installer |
| `C:\CamtekGit\BIS\bin\Scripts\RunAOI.bat` | Launches audit service then AOI_Main |
| `C:\CamtekGit\BIS\bin\Kill_AOIEx.cmd` | Taskkills audit service last in shutdown |
