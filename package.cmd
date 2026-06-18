@echo off
setlocal

set "ROOT=%~dp0"
set "CERT_DIR=%ROOT%.certs"
set "CERT_PFX=%CERT_DIR%\WinCodexBar.pfx"
set "CERT_CER=%CERT_DIR%\WinCodexBar.cer"
set "CERT_PASSWORD=WinCodexBarLocalDev"
set "OUT_DIR=%ROOT%artifacts\msix"

if not exist "%CERT_DIR%" mkdir "%CERT_DIR%"
if not exist "%OUT_DIR%" mkdir "%OUT_DIR%"

if not exist "%CERT_PFX%" (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $cert=New-SelfSignedCertificate -Type Custom -Subject 'CN=WinCodexBar' -KeyUsage DigitalSignature -FriendlyName 'Win CodexBar MSIX Dev Cert' -CertStoreLocation 'Cert:\CurrentUser\My' -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3'); $password=ConvertTo-SecureString -String '%CERT_PASSWORD%' -Force -AsPlainText; Export-PfxCertificate -Cert $cert -FilePath '%CERT_PFX%' -Password $password | Out-Null; Export-Certificate -Cert $cert -FilePath '%CERT_CER%' | Out-Null"
    if errorlevel 1 exit /b %errorlevel%
)

"%ROOT%.dotnet\dotnet.exe" msbuild "%ROOT%src\CodexBar.Windows\CodexBar.Windows.csproj" -restore -t:Publish -p:Configuration=Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:SelfContained=true -p:EnableMsixPackaging=true -p:GenerateAppxPackageOnBuild=true -p:AppxBundle=Never -p:UapAppxPackageBuildMode=SideloadOnly "-p:AppxPackageDir=%OUT_DIR%\\" -p:AppxPackageSigningEnabled=true "-p:PackageCertificateKeyFile=%CERT_PFX%" "-p:PackageCertificatePassword=%CERT_PASSWORD%"

if errorlevel 1 exit /b %errorlevel%

echo.
echo MSIX package output:
echo   %OUT_DIR%
echo.
echo To install it:
echo   .\install-msix.cmd
