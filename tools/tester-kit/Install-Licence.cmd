@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Licence.ps1" %*
pause
