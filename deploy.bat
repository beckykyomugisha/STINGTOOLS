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
