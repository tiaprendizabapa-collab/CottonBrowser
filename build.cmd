@echo off
call "%~dp0LeanBrowser\build.cmd" %*
exit /b %errorlevel%
