@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Get-MachineCode.ps1" %*
pause
