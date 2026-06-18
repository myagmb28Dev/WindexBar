@echo off
setlocal

set "ROOT=%~dp0"
set "CERT_CER=%ROOT%.certs\WinCodexBar.cer"
set "OUT_DIR=%ROOT%artifacts\msix"

if not exist "%CERT_CER%" (
    echo Missing certificate. Run package.cmd first.
    exit /b 1
)

certutil -user -addstore TrustedPeople "%CERT_CER%"
if errorlevel 1 exit /b %errorlevel%

powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $package=Get-ChildItem -LiteralPath '%OUT_DIR%' -Recurse -Include '*.msix','*.msixbundle','*.appx','*.appxbundle' | Sort-Object LastWriteTime -Descending | Select-Object -First 1; if ($null -eq $package) { Write-Error 'No MSIX package found. Run package.cmd first.'; exit 1 }; Add-AppxPackage -ForceApplicationShutdown -ForceUpdateFromAnyVersion -Path $package.FullName"
