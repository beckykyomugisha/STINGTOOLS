@echo off
REM ──────────────────────────────────────────────────────────────────
REM  StingTools Build Script
REM  Builds StingTools.dll for Autodesk Revit 2025/2026/2027
REM  Usage:
REM    build.bat              - Build Release (auto-detect Revit)
REM    build.bat clean        - Clean build output
REM    build.bat debug        - Build Debug configuration
REM    build.bat deploy       - Build and copy to Revit addins folder
REM    build.bat "C:\..."     - Build with explicit Revit API path
REM ──────────────────────────────────────────────────────────────────

setlocal enabledelayedexpansion

REM ── Configuration ─────────────────────────────────────────────────
set "PROJECT=StingTools\StingTools.csproj"
set "CONFIG=Release"

REM ── Handle options ─────────────────────────────────────────────────
if /i "%~1"=="clean" (
    echo Cleaning build output...
    dotnet clean "%PROJECT%" -c Release --verbosity quiet 2>nul
    dotnet clean "%PROJECT%" -c Debug --verbosity quiet 2>nul
    if exist "StingTools\bin" rd /s /q "StingTools\bin"
    if exist "StingTools\obj" rd /s /q "StingTools\obj"
    echo Clean complete.
    exit /b 0
)

if /i "%~1"=="debug" (
    set "CONFIG=Debug"
    shift
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

if "%~1" NEQ "" if /i "%~1" NEQ "deploy" (
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
echo [1/4] Restoring packages...
dotnet restore "%PROJECT%" --verbosity quiet
if errorlevel 1 (
    echo ERROR: Package restore failed.
    exit /b 1
)

REM ── Build ────────────────────────────────────────────────────────
echo [2/4] Building %CONFIG%...
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
echo [3/4] Locating output...
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

    REM ── Count source stats ─────────────────────────────────────
    echo [4/4] Assembly stats:
    for /f %%A in ('dir /s /b StingTools\*.cs 2^>nul ^| find /c /v ""') do echo   C# files:   %%A
    for /f %%A in ('dir /s /b StingTools\*.xaml 2^>nul ^| find /c /v ""') do echo   XAML files:  %%A
    for /f %%A in ('dir /s /b StingTools\Data\* 2^>nul ^| find /c /v ""') do echo   Data files:  %%A
    echo.

    REM ── Deploy option ──────────────────────────────────────────
    if /i "%~1"=="deploy" goto :deploy
    if /i "%~2"=="deploy" goto :deploy

    echo  Deploy to Revit:
    echo    1. Copy StingTools.dll + Newtonsoft.Json.dll + ClosedXML.dll + data\ to plugin folder
    echo    2. Update Assembly path in StingTools.addin to match plugin folder
    echo    3. Copy StingTools.addin to your Revit version addins folder:
    echo       %%APPDATA%%\Autodesk\Revit\Addins\2025\  (or 2026, 2027)
    echo    4. Restart Revit
    echo.
    echo  Or run: build.bat deploy
    echo.
) else (
    echo WARNING: StingTools.dll not found at expected location.
    echo Check build output above for actual path.
)

goto :end

:deploy
REM ── Auto-deploy to Revit addins folder ─────────────────────────
echo.
echo  Deploying to Revit...

REM Detect target Revit version from API path
set "REVIT_VER=2025"
echo %RevitApiPath% | findstr "2027" >nul && set "REVIT_VER=2027"
echo %RevitApiPath% | findstr "2026" >nul && set "REVIT_VER=2026"

set "ADDINS_DIR=%APPDATA%\Autodesk\Revit\Addins\%REVIT_VER%"
set "PLUGIN_DIR=%ADDINS_DIR%\StingTools"

if not exist "%ADDINS_DIR%" (
    echo ERROR: Revit addins directory not found: %ADDINS_DIR%
    echo Is Revit %REVIT_VER% installed?
    exit /b 1
)

REM Create plugin directory
if not exist "%PLUGIN_DIR%" mkdir "%PLUGIN_DIR%"
if not exist "%PLUGIN_DIR%\data" mkdir "%PLUGIN_DIR%\data"

REM Copy files
echo   Copying StingTools.dll...
copy /y "%OUTDIR%\StingTools.dll" "%PLUGIN_DIR%\" >nul
echo   Copying Newtonsoft.Json.dll...
copy /y "%OUTDIR%\Newtonsoft.Json.dll" "%PLUGIN_DIR%\" >nul 2>nul
echo   Copying ClosedXML.dll...
copy /y "%OUTDIR%\ClosedXML.dll" "%PLUGIN_DIR%\" >nul 2>nul
echo   Copying data files...
xcopy /y /q "%OUTDIR%\data\*" "%PLUGIN_DIR%\data\" >nul 2>nul

REM Copy addin manifest (update path)
echo   Installing addin manifest...
powershell -Command "(Get-Content 'StingTools.addin') -replace '<Assembly>.*</Assembly>', '<Assembly>%PLUGIN_DIR:\=\\%\\StingTools.dll</Assembly>' | Set-Content '%ADDINS_DIR%\StingTools.addin'"

echo.
echo ═══════════════════════════════════════════════
echo  DEPLOY COMPLETE — Revit %REVIT_VER%
echo ═══════════════════════════════════════════════
echo.
echo  Plugin:  %PLUGIN_DIR%\StingTools.dll
echo  Addin:   %ADDINS_DIR%\StingTools.addin
echo  Data:    %PLUGIN_DIR%\data\
echo.
echo  Restart Revit to load the updated plugin.
echo.

:end
endlocal
