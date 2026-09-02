@echo off
rem Thin wrapper. The real build script is build.ps1 (kept in UTF-8 so it can
rem handle the Korean executable name; .cmd files are read in the ANSI codepage).
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
exit /b %errorlevel%
