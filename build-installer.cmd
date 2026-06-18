@echo off
setlocal

set "ROOT=%~dp0"
set "PUBLISH_DIR=%ROOT%artifacts\publish\win-x64"
set "ISS_FILE=%ROOT%installer\WinCodexBar.iss"
for /f "usebackq delims=" %%T in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-Date -Format 'yyyyMMdd-HHmmss'"`) do set "BUILD_STAMP=%%T"
set "INSTALLER_DIR=%ROOT%artifacts\installer\%BUILD_STAMP%"
set "ISCC="

if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not defined ISCC (
    for /f "usebackq delims=" %%I in (`where ISCC.exe 2^>nul`) do (
        if not defined ISCC set "ISCC=%%I"
    )
)

if not defined ISCC (
    echo Inno Setup compiler was not found.
    echo Install it, then run this command again:
    echo   winget install JRSoftware.InnoSetup
    exit /b 1
)

if not exist "%PUBLISH_DIR%" mkdir "%PUBLISH_DIR%"
if not exist "%INSTALLER_DIR%" mkdir "%INSTALLER_DIR%"

"%ROOT%.dotnet\dotnet.exe" publish "%ROOT%src\CodexBar.Windows\CodexBar.Windows.csproj" -c Release -r win-x64 --self-contained true -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:PublishTrimmed=false -o "%PUBLISH_DIR%"
if errorlevel 1 exit /b %errorlevel%

"%ISCC%" "%ISS_FILE%" "/DSourceDir=%PUBLISH_DIR%" "/DOutputDir=%INSTALLER_DIR%"
if errorlevel 1 exit /b %errorlevel%

echo.
echo Installer created:
echo   %INSTALLER_DIR%\WinCodexBarSetup.exe
