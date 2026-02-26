@echo off
REM ──────────────────────────────────────────────────────────────────
REM  StingTools Build Script
REM  Builds StingTools.dll for Autodesk Revit 2025/2026/2027
REM ──────────────────────────────────────────────────────────────────

setlocal enabledelayedexpansion

REM ── Configuration ─────────────────────────────────────────────────
set "PROJECT=StingTools\StingTools.csproj"
set "CONFIG=Release"

REM ── Detect Revit API path ────────────────────────────────────────
REM Try common install locations, newest first
if defined RevitApiPath (
    echo Using RevitApiPath from environment: %RevitApiPath%
    goto :build
)

for %%V in (2027 2026 2025) do (
    if exist "C:\Program Files\Autodesk\Revit %%V\RevitAPI.dll" (
        set "RevitApiPath=C:\Program Files\Autodesk\Revit %%V"
        echo Found Revit %%V API at: !RevitApiPath!
        goto :build
    )
)

REM Fallback: prompt user
echo.
echo WARNING: Revit API not found in standard locations.
echo Set the RevitApiPath environment variable or pass it as argument:
echo.
echo   build.bat "C:\Program Files\Autodesk\Revit 2025"
echo   -- or --
echo   set RevitApiPath=C:\Program Files\Autodesk\Revit 2025
echo   build.bat
echo.

if "%~1" NEQ "" (
    set "RevitApiPath=%~1"
    echo Using provided path: !RevitApiPath!
    goto :build
)

echo ERROR: No Revit API path specified. Exiting.
exit /b 1

:build
REM ── Verify API DLLs exist ────────────────────────────────────────
if not exist "%RevitApiPath%\RevitAPI.dll" (
    echo ERROR: RevitAPI.dll not found at: %RevitApiPath%
    exit /b 1
)
if not exist "%RevitApiPath%\RevitAPIUI.dll" (
    echo ERROR: RevitAPIUI.dll not found at: %RevitApiPath%
    exit /b 1
)

echo.
echo ══════════════════════════════════════════════════════════════════
echo  Building StingTools (%CONFIG%)
echo  Revit API: %RevitApiPath%
echo ══════════════════════════════════════════════════════════════════
echo.

REM ── Restore NuGet packages ───────────────────────────────────────
echo [1/3] Restoring packages...
dotnet restore "%PROJECT%" --verbosity quiet
if errorlevel 1 (
    echo ERROR: Package restore failed.
    exit /b 1
)

REM ── Build ────────────────────────────────────────────────────────
echo [2/3] Building %CONFIG%...
dotnet build "%PROJECT%" -c %CONFIG% -p:RevitApiPath="%RevitApiPath%" --no-restore
if errorlevel 1 (
    echo.
    echo ═══════════════════════════════════════════════
    echo  BUILD FAILED — check errors above
    echo ═══════════════════════════════════════════════
    exit /b 1
)

REM ── Report output ────────────────────────────────────────────────
echo.
echo [3/3] Locating output...
set "OUTDIR=StingTools\bin\%CONFIG%"
if exist "%OUTDIR%\StingTools.dll" (
    echo.
    echo ═══════════════════════════════════════════════
    echo  BUILD SUCCEEDED
    echo ═══════════════════════════════════════════════
    echo.
    echo  Output:  %OUTDIR%\StingTools.dll
    echo  Data:    %OUTDIR%\data\
    echo.
    echo  Deploy to Revit:
    echo    1. Copy StingTools.dll + Newtonsoft.Json.dll + data\ to plugin folder
    echo    2. Copy StingTools.addin to:
    echo       %%APPDATA%%\Autodesk\Revit\Addins\2025\
    echo    3. Restart Revit
    echo.
) else (
    echo WARNING: StingTools.dll not found at expected location.
    echo Check build output above for actual path.
)

endlocal
