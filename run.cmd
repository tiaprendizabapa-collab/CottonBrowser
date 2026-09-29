@echo off
setlocal
if not exist "%~dp0LeanBrowser\dist\CottonBrowser.exe" goto :build
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0LeanBrowser\needs-build.ps1"
if errorlevel 2 exit /b 1
if not errorlevel 1 goto :run
:build
call "%~dp0LeanBrowser\build.cmd" --no-pause
if errorlevel 1 (
    pause
    exit /b 1
)
:run
start "" "%~dp0LeanBrowser\dist\CottonBrowser.exe"

