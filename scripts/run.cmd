@echo off
setlocal
if not exist "%~dp0..\src\LeanBrowser\dist\CottonBrowser.exe" goto :build
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0needs-build.ps1"
if errorlevel 2 exit /b 1
if not errorlevel 1 goto :run
:build
call "%~dp0build.cmd" --no-pause
if errorlevel 1 (
    pause
    exit /b 1
)
:run
start "" "%~dp0..\src\LeanBrowser\dist\CottonBrowser.exe"
