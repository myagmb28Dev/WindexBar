@echo off
setlocal

set "ROOT=%~dp0"
set "PUBLISH_ROOT=%ROOT%artifacts\publish"
set "ISS_FILE=%ROOT%installer\WindexBar.iss"
set "SETUP_ICON=%ROOT%src\WindexBar.Windows\Assets\TrayIcon.ico"
for /f "usebackq delims=" %%T in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-Date -Format 'yyyyMMdd-HHmmss'"`) do set "BUILD_STAMP=%%T"
set "PUBLISH_DIR=%PUBLISH_ROOT%\%BUILD_STAMP%\win-x64"
set "INSTALLER_DIR=%ROOT%artifacts\installer\%BUILD_STAMP%"
set "INNO_OUTPUT_DIR=%TEMP%\WindexBarInstaller\%BUILD_STAMP%"
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

powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference = 'Stop'; New-Item -ItemType Directory -Path '%PUBLISH_DIR%' -Force | Out-Null; New-Item -ItemType Directory -Path '%INSTALLER_DIR%' -Force | Out-Null; New-Item -ItemType Directory -Path '%INNO_OUTPUT_DIR%' -Force | Out-Null"
if errorlevel 1 exit /b %errorlevel%

"%ROOT%.dotnet\dotnet.exe" publish "%ROOT%src\WindexBar.Windows\WindexBar.Windows.csproj" -c Release -r win-x64 --self-contained true -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:PublishReadyToRun=false -p:PublishTrimmed=false -p:NuGetAudit=false -o "%PUBLISH_DIR%"
if errorlevel 1 exit /b %errorlevel%

powershell -NoProfile -ExecutionPolicy Bypass -Command "$files = Get-ChildItem -LiteralPath '%PUBLISH_DIR%' -File | Where-Object { $_.Name -like 'WindexBar*.exe' -or $_.Name -like 'WindexBar*.dll' }; if ($files) { & '%ROOT%scripts\sign-app.ps1' -Path $files.FullName -WarnOnly }"
if errorlevel 1 exit /b %errorlevel%

"%ISCC%" "%ISS_FILE%" "/DSourceDir=%PUBLISH_DIR%" "/DOutputDir=%INNO_OUTPUT_DIR%" "/DSetupIconFile=%SETUP_ICON%"
if errorlevel 1 exit /b %errorlevel%

powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%scripts\sign-app.ps1" -Path "%INNO_OUTPUT_DIR%\WindexBarSetup.exe" -WarnOnly
if errorlevel 1 exit /b %errorlevel%

copy /Y "%INNO_OUTPUT_DIR%\WindexBarSetup.exe" "%INSTALLER_DIR%\WindexBarSetup.exe" >nul
if errorlevel 1 exit /b %errorlevel%

echo.
echo Installer created:
echo   %INSTALLER_DIR%\WindexBarSetup.exe
