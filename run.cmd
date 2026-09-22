@echo off
setlocal
if exist "%~dp0LeanBrowser\dist\CottonBrowser.exe" goto :run
call "%~dp0LeanBrowser\build.cmd" --no-pause
if errorlevel 1 (
    pause
    exit /b 1
)
:run
start "" "%~dp0LeanBrowser\dist\CottonBrowser.exe"
