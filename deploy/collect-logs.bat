@echo off
REM Gathers the STING log + newest Revit journal into a zip on your Desktop
REM so you can send it back for debugging.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0collect-logs.ps1"
echo.
pause
