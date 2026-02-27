@echo off
REM Open STINGTOOLS in Visual Studio
REM Detects VS 2022, falls back to VS 2019, then devenv on PATH

set "PROJECT=%~dp0StingTools\StingTools.csproj"
echo Looking for Visual Studio...
echo Project: %PROJECT%
echo.

REM VS 2022 (Community, Professional, Enterprise)
for %%E in (Community Professional Enterprise) do (
    if exist "C:\Program Files\Microsoft Visual Studio\2022\%%E\Common7\IDE\devenv.exe" (
        echo Found VS 2022 %%E — opening...
        start "" "C:\Program Files\Microsoft Visual Studio\2022\%%E\Common7\IDE\devenv.exe" "%PROJECT%"
        timeout /t 3 >nul
        exit /b 0
    )
)

REM VS 2019
for %%E in (Community Professional Enterprise) do (
    if exist "C:\Program Files (x86)\Microsoft Visual Studio\2019\%%E\Common7\IDE\devenv.exe" (
        echo Found VS 2019 %%E — opening...
        start "" "C:\Program Files (x86)\Microsoft Visual Studio\2019\%%E\Common7\IDE\devenv.exe" "%PROJECT%"
        timeout /t 3 >nul
        exit /b 0
    )
)

REM Fallback: try PATH
where devenv.exe >nul 2>&1
if %errorlevel%==0 (
    echo Found devenv on PATH — opening...
    start "" devenv.exe "%PROJECT%"
    timeout /t 3 >nul
    exit /b 0
)

echo.
echo =============================================
echo  Visual Studio NOT FOUND on this machine.
echo =============================================
echo.
echo Searched:
echo   C:\Program Files\Microsoft Visual Studio\2022\Community
echo   C:\Program Files\Microsoft Visual Studio\2022\Professional
echo   C:\Program Files\Microsoft Visual Studio\2022\Enterprise
echo   C:\Program Files (x86)\Microsoft Visual Studio\2019\Community
echo   C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional
echo   C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise
echo.
echo To fix: Install Visual Studio from https://visualstudio.microsoft.com
echo Or open the project manually: %PROJECT%
echo.
pause
