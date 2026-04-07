@echo off
setlocal enabledelayedexpansion

:: ──────────────────────────────────────────────────────────────────
::  StingTools Build + Deploy Script
::  Compiles the plugin and copies output to CompiledPlugin/
:: ──────────────────────────────────────────────────────────────────

set "SCRIPT_DIR=%~dp0"
set "PROJECT=%SCRIPT_DIR%StingTools\StingTools.csproj"

:: ── Locate Revit API ──────────────────────────────────────────────
set "REVIT_API="
for %%V in (2025 2026 2027) do (
    if exist "C:\Program Files\Autodesk\Revit %%V\RevitAPI.dll" (
        if "!REVIT_API!"=="" set "REVIT_API=C:\Program Files\Autodesk\Revit %%V"
    )
)

if "!REVIT_API!"=="" (
    echo ERROR: Revit API not found in Program Files.
    echo        Checked: Revit 2025, 2026, 2027
    exit /b 1
)
echo Found Revit API at: !REVIT_API!

:: ── Build ─────────────────────────────────────────────────────────
echo.
echo Building StingTools (Release^)...
dotnet build "%PROJECT%" -c Release -p:RevitApiPath="!REVIT_API!" --nologo -v minimal
if errorlevel 1 (
    echo.
    echo BUILD FAILED.
    exit /b 1
)

:: ── Deploy ────────────────────────────────────────────────────────
echo.
bash "%SCRIPT_DIR%extract_plugin.sh"
if errorlevel 1 (
    echo.
    echo DEPLOY FAILED.
    exit /b 1
)

endlocal
