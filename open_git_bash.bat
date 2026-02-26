@echo off
REM Open Git Bash at the STINGTOOLS project root
REM Tries common Git for Windows install locations

set "PROJECT_DIR=%~dp0"

if exist "C:\Program Files\Git\git-bash.exe" (
    start "" "C:\Program Files\Git\git-bash.exe" --cd="%PROJECT_DIR%"
    exit /b 0
)

if exist "C:\Program Files (x86)\Git\git-bash.exe" (
    start "" "C:\Program Files (x86)\Git\git-bash.exe" --cd="%PROJECT_DIR%"
    exit /b 0
)

REM Fallback: try PATH
where git-bash.exe >nul 2>&1
if %errorlevel%==0 (
    start "" git-bash.exe --cd="%PROJECT_DIR%"
    exit /b 0
)

echo Git Bash not found. Install Git for Windows from https://git-scm.com
pause
