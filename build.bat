@echo off
setlocal enabledelayedexpansion

:: ──────────────────────────────────────────────────────────────────
::  StingTools Build + Deploy Script
::  Compiles the plugin once per installed Revit year and stages each build:
::    Revit 2025 -> StingTools\bin\Release        -> CompiledPlugin\        (as before)
::    Revit 2026 -> StingTools\bin\R2026\Release  -> CompiledPlugin-R2026\
::    Revit 2027 -> StingTools\bin\R2027\Release  -> CompiledPlugin-R2027\  (.NET 10)
::  Limit the years with STING_YEARS, e.g.  set STING_YEARS=2025
::
::  With STING_DEPLOY=1 (deploy.bat) the manifests point:
::    Addins\2025 and Addins\2026 -> CompiledPlugin\  (unchanged; the 2025 build runs in
::                                  2026 — CompiledPlugin-R2026 is a compile check, and
::                                  is installed into 2026 only when 2025 was not built)
::    Addins\2027                 -> CompiledPlugin-R2027\ (2027 removed APIs the 2025
::                                  build calls, so it must never load CompiledPlugin\)
:: ──────────────────────────────────────────────────────────────────

set "SCRIPT_DIR=%~dp0"
set "PROJECT=%SCRIPT_DIR%StingTools\StingTools.csproj"

:: ── Which Revit years ─────────────────────────────────────────────
set "YEARS=%STING_YEARS%"
if "!YEARS!"=="" (
    for %%V in (2025 2026 2027) do (
        if exist "C:\Program Files\Autodesk\Revit %%V\RevitAPI.dll" set "YEARS=!YEARS! %%V"
    )
)
if "!YEARS!"=="" (
    echo ERROR: Revit API not found in Program Files.
    echo        Checked: Revit 2025, 2026, 2027
    goto :fail
)
for %%V in (!YEARS!) do (
    if not "%%V"=="2025" if not "%%V"=="2026" if not "%%V"=="2027" (
        echo ERROR: Revit %%V is not supported ^(2025, 2026, 2027^).
        goto :fail
    )
    if not exist "C:\Program Files\Autodesk\Revit %%V\RevitAPI.dll" (
        echo ERROR: STING_YEARS asks for Revit %%V, which is not installed.
        goto :fail
    )
)
echo Revit years to build:!YEARS!

:: ── Build each year ───────────────────────────────────────────────
set "BUILT_2025="
for %%V in (!YEARS!) do (
    echo.
    echo Building StingTools for Revit %%V ^(Release^)...
    dotnet build "%PROJECT%" -c Release -p:RevitApiPath="C:\Program Files\Autodesk\Revit %%V" --nologo -v minimal
    if errorlevel 1 (
        echo.
        echo BUILD FAILED for Revit %%V.
        goto :fail
    )
    if "%%V"=="2025" set "BUILT_2025=1"
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
    goto :fail
)
echo Found Git Bash at: !GIT_BASH!

:: ── Stage each year (and, when STING_DEPLOY=1, install into Revit) ─
for %%V in (!YEARS!) do (
    if "%%V"=="2025" (
        set "STING_BUILD_DIR=%SCRIPT_DIR%StingTools\bin\Release"
        set "STING_STAGE_DIR=%SCRIPT_DIR%CompiledPlugin"
        set "STING_INSTALL_YEARS=2025 2026"
    ) else (
        set "STING_BUILD_DIR=%SCRIPT_DIR%StingTools\bin\R%%V\Release"
        set "STING_STAGE_DIR=%SCRIPT_DIR%CompiledPlugin-R%%V"
        set "STING_INSTALL_YEARS=%%V"
        rem "none", not empty: cmd's set "X=" DELETES X, and extract_plugin.sh then
        rem falls back to installing into every year.
        if "%%V"=="2026" if defined BUILT_2025 set "STING_INSTALL_YEARS=none"
    )
    echo.
    echo Staging Revit %%V -^> !STING_STAGE_DIR!
    "!GIT_BASH!" "%SCRIPT_DIR%extract_plugin.sh"
    if errorlevel 1 (
        echo.
        if "%STING_DEPLOY%"=="1" (echo DEPLOY FAILED for Revit %%V.) else (echo STAGING FAILED for Revit %%V.)
        goto :fail
    )
)

endlocal
exit /b 0

:fail
:: One exit for every failure: `exit /b 1` inside a ( ) block does not always reach the
:: caller as a non-zero exit code when this file is run directly (cmd /c build.bat).
endlocal
exit /b 1
