@echo off
setlocal enabledelayedexpansion

:: ──────────────────────────────────────────────────────────────────
::  package.bat — build + bundle the tester install zip in ONE step.
::
::  Produces  StingTools_Deploy_<yyyymmdd>_gated.zip  in the repo root
::  (git-ignored), containing the deploy\ scripts + guide alongside a
::  freshly-built CompiledPlugin\ (DLLs + data). Hand this zip to a
::  tester; they extract it and run install.bat (see deploy\INSTALL_GUIDE.md).
::
::  Usage:
::    package.bat            Build Release, stage, and zip (normal case).
::    package.bat --nobuild  Zip the existing CompiledPlugin\ as-is
::                           (skip the compile — useful for a re-zip).
::
::  This only reads the repo + writes the zip. It does NOT install into
::  Revit and does NOT touch the shared add-in slot (build.bat stages to
::  CompiledPlugin\ only; deploy.bat is the separate "make live" step).
:: ──────────────────────────────────────────────────────────────────

set "SCRIPT_DIR=%~dp0"

:: ── 1. Build + stage CompiledPlugin (unless --nobuild) ────────────
if /I "%~1"=="--nobuild" (
    echo Skipping build ^(--nobuild^) — packaging existing CompiledPlugin\.
) else (
    echo === Building Release + staging CompiledPlugin ===
    call "%SCRIPT_DIR%build.bat"
    if errorlevel 1 (
        echo.
        echo BUILD FAILED — aborting package.
        exit /b 1
    )
)

if not exist "%SCRIPT_DIR%CompiledPlugin\StingTools.dll" (
    echo.
    echo ERROR: CompiledPlugin\StingTools.dll not found.
    echo        Run package.bat without --nobuild to compile first.
    exit /b 1
)

:: ── 2. Date stamp (yyyymmdd) ──────────────────────────────────────
for /f %%d in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd"') do set "STAMP=%%d"

:: ── 3. Stage deploy\ + CompiledPlugin\ and zip ────────────────────
echo.
echo === Packaging StingTools_Deploy_%STAMP%_gated.zip ===
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $r='%SCRIPT_DIR%'.TrimEnd('\'); $pkg=Join-Path $r '_pkg_stage'; $stage=Join-Path $pkg 'StingTools_Deploy'; if(Test-Path $pkg){Remove-Item $pkg -Recurse -Force}; New-Item -ItemType Directory -Force $stage | Out-Null; Get-ChildItem (Join-Path $r 'deploy') -File | Copy-Item -Destination $stage -Force; Copy-Item (Join-Path $r 'CompiledPlugin') (Join-Path $stage 'CompiledPlugin') -Recurse -Force; $addin=Join-Path $stage 'CompiledPlugin\StingTools.addin'; if(Test-Path $addin){Remove-Item $addin -Force}; $zip=Join-Path $r 'StingTools_Deploy_%STAMP%_gated.zip'; if(Test-Path $zip){Remove-Item $zip -Force}; Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal; Remove-Item $pkg -Recurse -Force; $mb=[math]::Round((Get-Item $zip).Length/1MB,1); Write-Host ('PACKAGED: '+$zip+'  ('+$mb+' MB)') -ForegroundColor Green"
if errorlevel 1 (
    echo.
    echo PACKAGING FAILED.
    exit /b 1
)

echo.
echo Done. Send the zip above to the tester ^(see deploy\INSTALL_GUIDE.md^).
endlocal
