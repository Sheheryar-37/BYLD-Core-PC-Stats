<#
.SYNOPSIS
  Removes every trace of BYLD Core PC Stats Monitor and the drivers/services it
  installs. Use for clean-slate testing between builds and to verify the
  uninstaller left nothing behind.

.DESCRIPTION
  Handles: the app process + OpenRGB, the logon scheduled task, the app install
  folder, bundled OpenRGB, the PawnIO driver, the WinRing0 services, Start Menu /
  Desktop shortcuts, Add/Remove Programs registry keys, and any leftover Windows
  Defender exclusion from older builds.

  Run in an ELEVATED PowerShell (Run as Administrator).

.PARAMETER Check
  Report only - lists what is present and removes nothing. Use this to verify a
  machine is clean (or to see what a real uninstall would remove).

.NOTES
  PawnIO and WinRing0 are SHARED drivers. Other tools (LibreHardwareMonitor, Fan
  Control, OpenRGB) may rely on them. This script removes them for a full wipe;
  comment out the PawnIO / WinRing0 sections if you want to keep them.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\Uninstall-BYLD-Everything.ps1 -Check
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\Uninstall-BYLD-Everything.ps1
#>
param([switch]$Check)

$ErrorActionPreference = 'SilentlyContinue'

# --- Targets ----------------------------------------------------------------
$AppDir       = Join-Path $env:ProgramFiles 'BYLD Core\BYLD Core PC Stats Monitor'
$PublisherDir = Join-Path $env:ProgramFiles 'BYLD Core'
$PawnIoDir    = Join-Path $env:ProgramFiles 'PawnIO'
$TaskName     = 'BYLDCore_PCStatsMonitor_Startup'
$Processes    = @('PcStatsMonitor', 'OpenRGB')
$Services     = @('WinRing0_1_2_0', 'WinRing0x64')   # PawnIO handled by its own uninstaller
$AppId        = '{9A97C51E-3107-430E-8EB3-58D0EFEB179D}_is1'
$Shortcuts    = @(
    (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\BYLD Core\BYLD Core PC Stats Monitor.lnk'),
    (Join-Path $env:Public 'Desktop\BYLD Core PC Stats Monitor.lnk'),
    (Join-Path $env:USERPROFILE 'Desktop\BYLD Core PC Stats Monitor.lnk')
)
$UninstallKeys = @(
    "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\$AppId",
    "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\$AppId"
)

$mode = if ($Check) { 'CHECK' } else { 'REMOVE' }
Write-Host "`n=== BYLD Core full cleanup ($mode mode) ===`n" -ForegroundColor Cyan

# --- Admin guard ------------------------------------------------------------
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
          ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "This script must run as Administrator. Right-click PowerShell -> Run as administrator." -ForegroundColor Red
    exit 1
}

function Report([string]$item, [string]$state) {
    $color = switch ($state) {
        'present'   { 'Yellow' }
        'removed'   { 'Green' }
        'failed'    { 'Red' }
        default     { 'DarkGray' }   # not found
    }
    Write-Host ("  [{0,-9}] {1}" -f $state, $item) -ForegroundColor $color
}

# --- 1. Processes -----------------------------------------------------------
Write-Host "Processes:" -ForegroundColor White
foreach ($name in $Processes) {
    $proc = Get-Process -Name $name -ErrorAction SilentlyContinue
    if (-not $proc) { Report $name 'not found'; continue }
    if ($Check) { Report $name 'present'; continue }
    $proc | Stop-Process -Force -ErrorAction SilentlyContinue
    Report $name 'removed'
}

# --- 2. Scheduled task ------------------------------------------------------
Write-Host "`nScheduled task:" -ForegroundColor White
schtasks.exe /Query /TN $TaskName 2>$null | Out-Null
$taskExists = ($LASTEXITCODE -eq 0)
if (-not $taskExists) { Report $TaskName 'not found' }
elseif ($Check) { Report $TaskName 'present' }
else {
    schtasks.exe /Delete /TN $TaskName /F 2>$null | Out-Null
    Report $TaskName 'removed'
}

# --- 3. PawnIO (via its own installer CLI, then leftovers) ------------------
Write-Host "`nPawnIO driver:" -ForegroundColor White
$pawnSetup   = Join-Path $PSScriptRoot 'PawnIO_setup.exe'
$pawnService = Get-Service -Name 'PawnIO' -ErrorAction SilentlyContinue
if (-not $pawnService -and -not (Test-Path $PawnIoDir)) {
    Report 'PawnIO' 'not found'
}
elseif ($Check) { Report 'PawnIO' 'present' }
else {
    # PawnIO_setup.exe uses its own CLI: -uninstall -silent (it is NOT an Inno app).
    if (Test-Path $pawnSetup) { & $pawnSetup -uninstall -silent 2>$null | Out-Null; Start-Sleep -Seconds 2 }
    # Fallbacks if the setup is unavailable or left something behind
    sc.exe stop PawnIO   2>$null | Out-Null
    sc.exe delete PawnIO 2>$null | Out-Null
    if (Test-Path $PawnIoDir) { Remove-Item -Path $PawnIoDir -Recurse -Force -ErrorAction SilentlyContinue }
    $stillThere = (Get-Service -Name 'PawnIO' -ErrorAction SilentlyContinue) -or (Test-Path $PawnIoDir)
    if ($stillThere) { Report 'PawnIO' 'failed' } else { Report 'PawnIO' 'removed' }
}

# --- 4. WinRing0 services ---------------------------------------------------
Write-Host "`nWinRing0 services:" -ForegroundColor White
foreach ($svc in $Services) {
    $exists = Get-Service -Name $svc -ErrorAction SilentlyContinue
    if (-not $exists) { Report $svc 'not found'; continue }
    if ($Check) { Report $svc 'present'; continue }
    sc.exe stop $svc   2>$null | Out-Null
    sc.exe delete $svc 2>$null | Out-Null
    if (Get-Service -Name $svc -ErrorAction SilentlyContinue) { Report $svc 'failed' } else { Report $svc 'removed' }
}

# --- 5. Install folders -----------------------------------------------------
Write-Host "`nInstall folders:" -ForegroundColor White
foreach ($dir in @($AppDir, $PublisherDir)) {
    if (-not (Test-Path $dir)) { Report $dir 'not found'; continue }
    if ($Check) { Report $dir 'present'; continue }
    Remove-Item -Path $dir -Recurse -Force -ErrorAction SilentlyContinue
    if (Test-Path $dir) { Report $dir 'failed' } else { Report $dir 'removed' }
}

# --- 6. Shortcuts -----------------------------------------------------------
Write-Host "`nShortcuts:" -ForegroundColor White
foreach ($lnk in $Shortcuts) {
    if (-not (Test-Path $lnk)) { Report $lnk 'not found'; continue }
    if ($Check) { Report $lnk 'present'; continue }
    Remove-Item -Path $lnk -Force -ErrorAction SilentlyContinue
    Report $lnk 'removed'
}

# --- 7. Registry (Add/Remove Programs) --------------------------------------
Write-Host "`nRegistry (Add/Remove Programs):" -ForegroundColor White
foreach ($key in $UninstallKeys) {
    if (-not (Test-Path $key)) { Report $key 'not found'; continue }
    if ($Check) { Report $key 'present'; continue }
    Remove-Item -Path $key -Recurse -Force -ErrorAction SilentlyContinue
    Report $key 'removed'
}

# --- 8. Windows Defender exclusion (older builds added one) ------------------
Write-Host "`nDefender exclusion:" -ForegroundColor White
try {
    $pref = Get-MpPreference -ErrorAction Stop
    $exPath = $pref.ExclusionPath    | Where-Object { $_ -like '*BYLD Core*' }
    $exProc = $pref.ExclusionProcess | Where-Object { $_ -like '*PcStatsMonitor*' }
    if (-not $exPath -and -not $exProc) { Report 'BYLD Core exclusions' 'not found' }
    elseif ($Check) { Report 'BYLD Core exclusions' 'present' }
    else {
        foreach ($p in $exPath) { Remove-MpPreference -ExclusionPath $p -ErrorAction SilentlyContinue }
        foreach ($p in $exProc) { Remove-MpPreference -ExclusionProcess $p -ErrorAction SilentlyContinue }
        Report 'BYLD Core exclusions' 'removed'
    }
}
catch { Report 'Defender not queryable (skipped)' 'not found' }

Write-Host "`n=== Done ($mode). ===" -ForegroundColor Cyan
if ($Check) { Write-Host "Nothing was changed. Re-run without -Check to remove.`n" -ForegroundColor DarkGray }
else { Write-Host "Re-run with -Check to confirm everything reads 'not found'.`n" -ForegroundColor DarkGray }
