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
    echo ═══════════════════════════════════
    echo  BUILD FAILED — check errors above
    echo ═══════════════════════════════════
    exit /b 1
)

REM ── Locate output ──────────────────────────────────────────────
echo.
echo [3/3] Locating output...
set "OUTDIR="

REM Check possible output locations
if exist "StingTools\bin\%CONFIG%\StingTools.dll" (
    set "OUTDIR=StingTools\bin\%CONFIG%"
)
if exist "StingTools\bin\%CONFIG%\net8.0-windows\StingTools.dll" (
    set "OUTDIR=StingTools\bin\%CONFIG%\net8.0-windows"
)

if "!OUTDIR!"=="" (
    echo WARNING: StingTools.dll not found at expected location.
    echo Searching...
    for /r "StingTools\bin" %%F in (StingTools.dll) do (
        set "OUTDIR=%%~dpF"
        set "OUTDIR=!OUTDIR:~0,-1!"
        echo Found at: !OUTDIR!
        goto :deploy
    )
    echo ERROR: StingTools.dll not found anywhere in build output.
    exit /b 1
)

:deploy
echo.
echo ═══════════════════════════════════
echo  BUILD SUCCEEDED
echo ═══════════════════════════════════
echo.
echo  Output: !OUTDIR!\StingTools.dll
echo.

REM ── Auto-deploy to CompiledPlugin ──────────────────────────────
call :do_deploy
goto :done

:do_deploy
echo [Deploy] Copying to CompiledPlugin...
if not exist "CompiledPlugin" mkdir "CompiledPlugin"
if not exist "CompiledPlugin\data" mkdir "CompiledPlugin\data"

REM Copy all DLLs except Revit API
for %%F in ("!OUTDIR!\*.dll") do (
    set "DLLNAME=%%~nxF"
    if /i not "!DLLNAME!"=="RevitAPI.dll" if /i not "!DLLNAME!"=="RevitAPIUI.dll" (
        copy /y "%%F" "CompiledPlugin\" >nul
    )
)

REM Copy data files
if exist "!OUTDIR!\data" (
    xcopy /y /e /q "!OUTDIR!\data\*" "CompiledPlugin\data\" >nul
) else if exist "StingTools\Data" (
    xcopy /y /e /q "StingTools\Data\*" "CompiledPlugin\data\" >nul
)

REM Copy addin manifest
if exist "StingTools.addin" copy /y "StingTools.addin" "CompiledPlugin\" >nul

echo.
echo ═══════════════════════════════════
echo  DEPLOYED TO CompiledPlugin\
echo ═══════════════════════════════════
echo.
echo  Deploy to Revit:
echo    1. Update Assembly path in StingTools.addin to match plugin folder
echo    2. Copy StingTools.addin to your Revit addins folder:
echo       %%APPDATA%%\Autodesk\Revit\Addins\2025\  (or 2026, 2027)
echo    3. Restart Revit
echo.
exit /b 0

:done
endlocal
