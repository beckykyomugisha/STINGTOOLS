@echo off
REM Removes the STING Tools manifest from all Revit versions (per-user).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1"
echo.
pause
