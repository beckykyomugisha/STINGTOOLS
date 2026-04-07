@echo off
REM Open STINGTOOLS in Visual Studio 2022 Community
REM Searches multiple possible install locations

set "PROJECT=%~dp0StingTools\StingTools.csproj"
echo Looking for Visual Studio 2022 Community...
echo Project: %PROJECT%
echo.

REM Standard location
if exist "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.exe" (
    echo Found at standard location — opening...
    start "" "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\devenv.exe" "%PROJECT%"
    timeout /t 3 >nul
    exit /b 0
)

REM Search via vswhere (most reliable method)
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist "%VSWHERE%" (
    echo Found vswhere — searching for VS install...
    for /f "tokens=*" %%i in ('"%VSWHERE%" -latest -property installationPath 2^>nul') do (
        set "VS_PATH=%%i"
    )
    if defined VS_PATH (
        if exist "%VS_PATH%\Common7\IDE\devenv.exe" (
            echo Found VS at: %VS_PATH%
            start "" "%VS_PATH%\Common7\IDE\devenv.exe" "%PROJECT%"
            timeout /t 3 >nul
            exit /b 0
        )
    )
)

REM Search common alternative drives
for %%D in (C D E) do (
    for %%E in (Community Professional Enterprise) do (
        if exist "%%D:\Program Files\Microsoft Visual Studio\2022\%%E\Common7\IDE\devenv.exe" (
            echo Found VS 2022 %%E on %%D: drive — opening...
            start "" "%%D:\Program Files\Microsoft Visual Studio\2022\%%E\Common7\IDE\devenv.exe" "%PROJECT%"
            timeout /t 3 >nul
            exit /b 0
        )
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
echo  Visual Studio NOT FOUND automatically.
echo =============================================
echo.
echo Please find devenv.exe manually:
echo   1. Open File Explorer
echo   2. Search your C: drive for "devenv.exe"
echo   3. Note the full path and let me know
echo.
echo Or just open Visual Studio manually, then:
echo   File ^> Open ^> Project/Solution
echo   Navigate to: %PROJECT%
echo.
pause
