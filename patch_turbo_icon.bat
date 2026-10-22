@echo off
setlocal EnableExtensions

rem ============================================================
rem  Turbo one-click icon patcher - PORTABLE edition
rem  (bat + PowerShell + inbox csc.exe; no Python, no installs)
rem  Repo: https://github.com/leoshone/turbo-icon-patcher
rem  Usage:
rem    patch_turbo_icon.bat            apply icon patch
rem    patch_turbo_icon.bat dry        preview only (no writes)
rem    patch_turbo_icon.bat verify     verify live window icon
rem  Notes:
rem    - Turbo root = parent folder of this folder (must contain turbo.exe)
rem    - Requires only Windows 10/11 built-ins:
rem        Windows PowerShell 5.1 + .NET Framework 4 inbox compiler
rem    - Keep this folder on an ASCII path (no Chinese chars).
rem ============================================================

set "TOOL=%~dp0"
set "PS1=%TOOL%scripts\patch_turbo_icon.ps1"

set "ARGUMENTS=-AllProfiles -Apply"
if /i "%~1"=="dry"    set "ARGUMENTS="
if /i "%~1"=="verify" set "ARGUMENTS=-Verify"

rem ---- kill turbo background processes ----------------------
rem (tasklist sets errorlevel 1 when the filter matches nothing)
tasklist /FI "IMAGENAME eq turbo.exe" >nul 2>&1
if not errorlevel 1 (
    if /i not "%~1"=="verify" (
        echo [WARN] turbo.exe is running. Killing background processes ...
        taskkill /F /IM turbo.exe /T >nul 2>&1
        ping -n 3 127.0.0.1 >nul
    )
)

rem ---- run portable patcher ---------------------------------
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%" %ARGUMENTS%
set "RC=%ERRORLEVEL%"

if "%RC%"=="0" (
    if /i not "%~1"=="verify" if /i not "%~1"=="dry" (
        echo.
        echo Refreshing Windows icon cache ...
        ie4uinit -Show >nul 2>&1
        echo Done. Start Turbo to see the new icon.
        echo If taskbar still shows the old icon: unpin, then pin again.
        echo Stubborn case: restart explorer.exe.
    )
) else (
    echo.
    echo [ERROR] patcher exited with code %RC%. See output above.
)
pause
exit /b %RC%
