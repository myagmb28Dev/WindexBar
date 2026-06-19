@echo off
setlocal
pushd "%~dp0"
set "DOTNET=.dotnet\dotnet.exe"
set "PROJECT=.\src\WindexBar.Windows"
set "RUN_DIR=.\artifacts\run\win-x64"
set "APP_EXE=%RUN_DIR%\WindexBar.Windows.exe"
set "PUBLISH_ARGS=publish %PROJECT% -c Debug -r win-x64 --self-contained true -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:PublishReadyToRun=false -p:PublishTrimmed=false -p:NuGetAudit=false -o %RUN_DIR%"

if exist "%APP_EXE%" (
    del /f /q "%APP_EXE%" >nul 2>nul
)

"%DOTNET%" %PUBLISH_ARGS%
if errorlevel 1 (
    set "EXITCODE=%ERRORLEVEL%"
) else if /i "%~1"=="--wait" (
    "%APP_EXE%"
    set "EXITCODE=%ERRORLEVEL%"
) else (
    start "WindexBar" "%APP_EXE%"
    set "EXITCODE=0"
)

popd
exit /b %EXITCODE%
