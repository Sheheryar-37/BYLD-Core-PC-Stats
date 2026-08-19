@echo off
REM ===========================================================================
REM  BYLD Core - full cleanup launcher
REM
REM  Just DOUBLE-CLICK this file. It elevates itself and runs the PowerShell
REM  cleanup script sitting next to it.
REM
REM  This exists because typing the script path by hand is error prone: the
REM  client's attempt failed with "the argument ... does not exist" simply
REM  because the extracted folder was not the one in the command (round 23,
REM  item 2). %~dp0 is this file's own folder, so the path is always right.
REM ===========================================================================

setlocal
set "SCRIPT=%~dp0Uninstall-BYLD-Everything.ps1"

if not exist "%SCRIPT%" (
    echo.
    echo   ERROR: Uninstall-BYLD-Everything.ps1 was not found next to this file.
    echo   Extract the WHOLE zip to one folder, then double-click this file again.
    echo.
    pause
    exit /b 1
)

REM Re-launch elevated if we are not already running as Administrator.
net session >nul 2>&1
if errorlevel 1 (
    echo Requesting Administrator rights...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b 0
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" %*

echo.
echo ============================================================
echo   RESTART THE PC before reinstalling BYLD Core.
echo   The hardware drivers stay loaded until you do.
echo ============================================================
echo.
pause
