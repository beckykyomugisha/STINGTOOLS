@echo off
REM ──────────────────────────────────────────────────────────────────
REM  StingTools Build Script
REM  Builds StingTools.dll for Autodesk Revit 2025/2026/2027
REM ──────────────────────────────────────────────────────────────────

setlocal enabledelayedexpansion

REM ── Configuration ─────────────────────────────────────────────────
set "PROJECT=StingTools\StingTools.csproj"
set "CONFIG=Release"

REM ── Handle clean option ──────────────────────────────────────────
if /i "%~1"=="clean" (
    echo Cleaning build output...
    dotnet clean "%PROJECT%" -c %CONFIG% --verbosity quiet 2>nul
    if exist "StingTools\bin" rd /s /q "StingTools\bin"
    if exist "StingTools\obj" rd /s /q "StingTools\obj"
    echo Clean complete.
    exit /b 0
)

REM ── Detect Revit API path ────────────────────────────────────────
REM Try common install locations (2025 preferred)
if defined RevitApiPath (
    echo Using RevitApiPath from environment: %RevitApiPath%
    goto :build
)

for %%V in (2025 2026 2027) do (
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
if not exist "%OUTDIR%\StingTools.dll" (
    REM Fallback: some .NET SDK versions append TFM despite AppendTargetFrameworkToOutputPath=false
    if exist "%OUTDIR%\net8.0-windows\StingTools.dll" set "OUTDIR=%OUTDIR%\net8.0-windows"
)
if not exist "%OUTDIR%\StingTools.dll" (
    REM Fallback: Debug config when Release was requested but build defaulted
    if exist "StingTools\bin\Debug\StingTools.dll" set "OUTDIR=StingTools\bin\Debug"
    if exist "StingTools\bin\Debug\net8.0-windows\StingTools.dll" set "OUTDIR=StingTools\bin\Debug\net8.0-windows"
)
if not exist "%OUTDIR%\StingTools.dll" (
    REM Last resort: search recursively under bin
    for /r "StingTools\bin" %%F in (StingTools.dll) do (
        set "OUTDIR=%%~dpF"
        set "OUTDIR=!OUTDIR:~0,-1!"
        goto :found
    )
)
:found
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
    echo    2. Update Assembly path in StingTools.addin to match plugin folder
    echo    3. Copy StingTools.addin to your Revit version addins folder:
    echo       %%APPDATA%%\Autodesk\Revit\Addins\2025\  (or 2026, 2027)
    echo    4. Restart Revit
    echo.
) else (
    echo.
    echo ═══════════════════════════════════════════════
    echo  BUILD FAILED — StingTools.dll not produced
    echo ═══════════════════════════════════════════════
    echo.
    echo  Check the build errors above. Common causes:
    echo    - Missing Revit API DLLs at: %RevitApiPath%
    echo    - C# compilation errors in source files
    echo    - NuGet package restore failures
    echo.
    exit /b 1
)

endlocal
