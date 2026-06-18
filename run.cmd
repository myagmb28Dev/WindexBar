@echo off
pushd "%~dp0"
".dotnet\dotnet.exe" run --project ".\src\CodexBar.Windows"
popd
