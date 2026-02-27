@echo off
REM Open Git Bash at the STINGTOOLS project root
REM Tries common Git for Windows install locations

set "PROJECT_DIR=%~dp0"
echo Looking for Git Bash...
echo Project directory: %PROJECT_DIR%
echo.

if exist "C:\Program Files\Git\git-bash.exe" (
    echo Found Git Bash — opening...
    start "" "C:\Program Files\Git\git-bash.exe" --cd="%PROJECT_DIR%"
    timeout /t 3 >nul
    exit /b 0
)

if exist "C:\Program Files (x86)\Git\git-bash.exe" (
    echo Found Git Bash (x86) — opening...
    start "" "C:\Program Files (x86)\Git\git-bash.exe" --cd="%PROJECT_DIR%"
    timeout /t 3 >nul
    exit /b 0
)

REM Fallback: try PATH
where git-bash.exe >nul 2>&1
if %errorlevel%==0 (
    echo Found Git Bash on PATH — opening...
    start "" git-bash.exe --cd="%PROJECT_DIR%"
    timeout /t 3 >nul
    exit /b 0
)

echo.
echo =============================================
echo  Git Bash NOT FOUND on this machine.
echo =============================================
echo.
echo Searched:
echo   C:\Program Files\Git\git-bash.exe
echo   C:\Program Files (x86)\Git\git-bash.exe
echo.
echo To fix: Install Git for Windows from https://git-scm.com
echo.
pause
