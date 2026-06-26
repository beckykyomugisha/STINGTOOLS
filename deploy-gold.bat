@echo off
REM ============================================================================
REM  deploy-gold.bat — refresh the isolated GOLD deploy folder Revit loads from.
REM
REM  Revit's StingTools.addin points at  C:\Dev\STING_PLACEMENT_GOLD\StingTools.dll
REM  (a flat deploy folder, deliberately isolated from this shared working
REM  checkout so parallel agents clobbering C:\Dev\STINGTOOLS can't break the
REM  live plugin). The trade-off: GOLD does NOT auto-update. Run this to build
REM  the current branch and copy the fresh DLL + data into GOLD.
REM
REM  Usage:  close Revit  ->  deploy-gold.bat  ->  reopen Revit.
REM  (If Revit is open it may hold StingTools.dll locked and the copy fails.)
REM ============================================================================
setlocal
set "SRC=%~dp0CompiledPlugin"
set "GOLD=C:\Dev\STING_PLACEMENT_GOLD"

echo [deploy-gold] Building (staging only, no Revit install)...
call "%~dp0build.bat"
if errorlevel 1 (
  echo [deploy-gold] BUILD FAILED — GOLD not touched.
  exit /b 1
)

if not exist "%SRC%\StingTools.dll" (
  echo [deploy-gold] ERROR: %SRC%\StingTools.dll not found after build.
  exit /b 1
)
if not exist "%GOLD%" (
  echo [deploy-gold] ERROR: GOLD folder %GOLD% does not exist.
  echo                Revit's manifest points there; create it or repoint the addin.
  exit /b 1
)

echo [deploy-gold] Copying fresh DLL + dependencies into GOLD...
copy /Y "%SRC%\*.dll" "%GOLD%\" >nul
if errorlevel 1 (
  echo [deploy-gold] DLL COPY FAILED — is Revit open and holding StingTools.dll?
  echo                Close Revit and re-run.
  exit /b 1
)

echo [deploy-gold] Syncing data\ (rules, category->seed map, alias map, ...)...
robocopy "%SRC%\data" "%GOLD%\data" /E /NJH /NJS /NDL /NFL >nul

echo.
echo [deploy-gold] DONE. GOLD refreshed from this checkout.
echo                Restart Revit to load the new build.
endlocal
