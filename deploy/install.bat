@echo off
REM ============================================================
REM  STING Tools - one-click installer
REM  Just double-click this file. It detects your Revit version
REM  and registers the plugin from wherever you extracted it.
REM ============================================================
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
echo.
pause
