@echo off
pushd "%~dp0"
".dotnet\dotnet.exe" run --project ".\src\WindexBar.Windows"
popd
