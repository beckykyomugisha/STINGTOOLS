@echo off
:: ──────────────────────────────────────────────────────────────────
::  Make THIS checkout the live STING plugin in Revit (build + install).
::
::  A plain `build.bat` now only COMPILES + STAGES to CompiledPlugin\
::  (so parallel checkouts / background agents can verify a build without
::  hijacking the single shared Revit add-in slot). This script is the
::  explicit, opt-in step that installs THIS checkout's build into Revit.
::
::  Run this in whichever checkout you want active, then restart Revit.
::
::  Per Revit year (see build.bat): Addins\2025 and Addins\2026 point at
::  CompiledPlugin\ as before; Addins\2027 points at CompiledPlugin-R2027\ (its
::  own .NET 10 build). Limit the years with STING_YEARS, e.g. set STING_YEARS=2025
::
::  Close Revit AND the Planscape Companion tray app first — both hold
::  StingTools.dll and its dependencies, and the copy half-fails silently.
::
::  Git Bash is resolved inside build.bat (plain `bash` hits the WSL launcher
::  in System32 and fails after the compile succeeds).
:: ──────────────────────────────────────────────────────────────────
setlocal
set "STING_DEPLOY=1"
call "%~dp0build.bat"
if errorlevel 1 (
    endlocal
    exit /b 1
)
endlocal
