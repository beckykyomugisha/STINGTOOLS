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

REM ── Repoint every Revit addin at GOLD ─────────────────────────────────────
REM  A parallel agent's `deploy.bat` (STING_DEPLOY=1) rewrites the addin to
REM  point at the SHARED CompiledPlugin folder — which every agent rebuilds,
REM  so Revit silently loads whoever built last. Re-pinning the manifest to the
REM  isolated GOLD folder on every deploy-gold run keeps Revit on THIS build.
echo [deploy-gold] Pinning Revit addins to GOLD...
for %%Y in (2025 2026 2027) do (
  set "ADDIN=%APPDATA%\Autodesk\Revit\Addins\%%Y\StingTools.addin"
  call :pin "%APPDATA%\Autodesk\Revit\Addins\%%Y\StingTools.addin" %%Y
)

echo.
echo [deploy-gold] DONE. GOLD refreshed and Revit addins pinned to GOLD.
echo                Restart Revit to load the new build.
endlocal
exit /b 0

:pin
if exist %1 (
  powershell -NoProfile -Command "$f=%1; (Get-Content $f -Raw) -replace '<Assembly>.*?</Assembly>','<Assembly>C:\Dev\STING_PLACEMENT_GOLD\StingTools.dll</Assembly>' | Set-Content $f -Encoding UTF8"
  echo   %2 pinned -^> C:\Dev\STING_PLACEMENT_GOLD\StingTools.dll
)
exit /b 0
