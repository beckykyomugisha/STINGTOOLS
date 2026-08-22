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

:: ── Locate Git Bash ───────────────────────────────────────────────
:: NOT plain `bash`. On Windows that resolves to C:\Windows\System32\bash.exe —
:: the WSL launcher — which dies with "execvpe(/bin/bash) failed" when no WSL
:: distro is installed, AFTER a perfectly good compile. Git ships bash at
:: <git>\bin\bash.exe but only puts <git>\cmd on PATH, so System32 always wins
:: and Git Bash must be resolved explicitly.
set "GIT_BASH="
if exist "%ProgramFiles%\Git\bin\bash.exe" set "GIT_BASH=%ProgramFiles%\Git\bin\bash.exe"
if not defined GIT_BASH if exist "%ProgramFiles(x86)%\Git\bin\bash.exe" set "GIT_BASH=%ProgramFiles(x86)%\Git\bin\bash.exe"
if not defined GIT_BASH if exist "%LOCALAPPDATA%\Programs\Git\bin\bash.exe" set "GIT_BASH=%LOCALAPPDATA%\Programs\Git\bin\bash.exe"

:: Fall back to deriving it from wherever git.exe lives (git\cmd\ -> git\bin\).
if not defined GIT_BASH (
    for /f "delims=" %%G in ('where git 2^>nul') do (
        if not defined GIT_BASH (
            if exist "%%~dpG..\bin\bash.exe" set "GIT_BASH=%%~dpG..\bin\bash.exe"
        )
    )
)

if not defined GIT_BASH (
    echo.
    echo ERROR: Git Bash not found. extract_plugin.sh needs it.
    echo        Looked in: %%ProgramFiles%%\Git\bin, %%ProgramFiles^(x86^)%%\Git\bin,
    echo                   %%LOCALAPPDATA%%\Programs\Git\bin, and beside git.exe on PATH.
    echo.
    echo        Install Git for Windows, or stage manually with:
    echo          "C:\path\to\Git\bin\bash.exe" extract_plugin.sh
    exit /b 1
)
echo Found Git Bash at: !GIT_BASH!

:: ── Stage (and, when STING_DEPLOY=1, install into Revit) ──────────
echo.
"!GIT_BASH!" "%SCRIPT_DIR%extract_plugin.sh"
if errorlevel 1 (
    echo.
    if "%STING_DEPLOY%"=="1" (echo DEPLOY FAILED.) else (echo STAGING FAILED.)
    exit /b 1
)

endlocal
