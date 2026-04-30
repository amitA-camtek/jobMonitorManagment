<#
.SYNOPSIS
    Install or uninstall the FalconAuditService Windows Service.
.EXAMPLE
    .\install.ps1 -Action Install
    .\install.ps1 -Action Uninstall
#>
#Requires -RunAsAdministrator

param(
    [ValidateSet('Install','Uninstall')]
    [string]$Action      = 'Install',
    [string]$InstallPath = 'C:\bis\bin\FalconAuditService',
    [string]$DbPath      = 'C:\bis\auditlog'
)

$ServiceName = 'FalconAuditService'
$DisplayName = 'Falcon Audit Log Service'
$Description = 'Monitors c:\job\ for file changes and writes per-job audit shards to SQLite.'
$ExePath     = Join-Path $InstallPath 'FalconAuditService.exe'
$DbDir       = $DbPath

if ($Action -eq 'Install') {
    if (-not (Test-Path $ExePath)) {
        Write-Error "Executable not found: $ExePath"
        exit 1
    }

    if (-not (Test-Path $DbDir)) {
        New-Item -ItemType Directory -Path $DbDir | Out-Null
        Write-Host "Created directory: $DbDir"
    }

    # Copy FileClassificationRules.json and ParameterDescriptions.json on first install
    $rulesSource = Join-Path $InstallPath 'FileClassificationRules.json'
    $rulesDest   = Join-Path $DbDir 'FileClassificationRules.json'
    if ((Test-Path $rulesSource) -and -not (Test-Path $rulesDest)) {
        Copy-Item $rulesSource $rulesDest
        Write-Host "Installed FileClassificationRules.json to $DbDir"
    }
    $pdSource = Join-Path $InstallPath 'ParameterDescriptions.json'
    $pdDest   = Join-Path $DbDir 'ParameterDescriptions.json'
    if ((Test-Path $pdSource) -and -not (Test-Path $pdDest)) {
        Copy-Item $pdSource $pdDest
        Write-Host "Installed ParameterDescriptions.json to $DbDir"
    }

    # Grant the virtual service account least-privilege access
    icacls "C:\job"           /grant "NT SERVICE\FalconAuditSvc:(OI)(CI)R" /T | Out-Null
    icacls "C:\bis\auditlog"  /grant "NT SERVICE\FalconAuditSvc:(OI)(CI)M" /T | Out-Null
    Write-Host "ACLs set for NT SERVICE\FalconAuditSvc."

    sc.exe create $ServiceName `
        binPath= "`"$ExePath`"" `
        start=   auto `
        obj=     "NT SERVICE\FalconAuditSvc"

    sc.exe description $ServiceName $Description
    sc.exe failure      $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000

    Start-Service -Name $ServiceName
    Write-Host "Service '$ServiceName' installed and started."

} elseif ($Action -eq 'Uninstall') {
    if ((Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)?.Status -eq 'Running') {
        Stop-Service -Name $ServiceName -Force
        Write-Host "Service stopped."
    }
    sc.exe delete $ServiceName
    Write-Host "Service '$ServiceName' uninstalled."
}
