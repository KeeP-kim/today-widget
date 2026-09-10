@echo off
rem Thin wrapper. The real installer is install.ps1, which is UTF-8 and holds the
rem Korean text; .cmd files are read in the ANSI codepage so they stay ASCII-only.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
echo.
pause
