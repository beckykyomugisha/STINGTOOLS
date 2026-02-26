@echo off
REM Open STINGTOOLS in Visual Studio
REM Detects VS 2022, falls back to VS 2019, then devenv on PATH

set "PROJECT=%~dp0StingTools\StingTools.csproj"

REM VS 2022 (Community, Professional, Enterprise)
for %%E in (Community Professional Enterprise) do (
    if exist "C:\Program Files\Microsoft Visual Studio\2022\%%E\Common7\IDE\devenv.exe" (
        start "" "C:\Program Files\Microsoft Visual Studio\2022\%%E\Common7\IDE\devenv.exe" "%PROJECT%"
        exit /b 0
    )
)

REM VS 2019
for %%E in (Community Professional Enterprise) do (
    if exist "C:\Program Files (x86)\Microsoft Visual Studio\2019\%%E\Common7\IDE\devenv.exe" (
        start "" "C:\Program Files (x86)\Microsoft Visual Studio\2019\%%E\Common7\IDE\devenv.exe" "%PROJECT%"
        exit /b 0
    )
)

REM Fallback: try PATH
where devenv.exe >nul 2>&1
if %errorlevel%==0 (
    start "" devenv.exe "%PROJECT%"
    exit /b 0
)

echo Visual Studio not found.
echo You can open the project manually: %PROJECT%
pause
