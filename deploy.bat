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
:: ──────────────────────────────────────────────────────────────────
setlocal
set "STING_DEPLOY=1"
call "%~dp0build.bat"
endlocal
