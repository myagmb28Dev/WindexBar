param(
    [Parameter(Mandatory = $true)]
    [string[]]$Path,
    [switch]$WarnOnly,
    [switch]$QuietMissingCertificate
)

$ErrorActionPreference = 'Stop'

function Write-SignWarning {
    param([string]$Message)

    if ($WarnOnly) {
        Write-Warning $Message
        return
    }

    throw $Message
}

function Get-WindexBarCodeSigningCertificate {
    $now = Get-Date
    $certs = @(
        Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
            Where-Object { $_.HasPrivateKey -and $_.NotAfter -gt $now }
    )

    if ($certs.Count -eq 0) {
        return $null
    }

    foreach ($subject in @('CN=WindexBar')) {
        $match = $certs |
            Where-Object { $_.Subject -eq $subject } |
            Sort-Object NotAfter -Descending |
            Select-Object -First 1
        if ($match) {
            return $match
        }
    }

    return $certs | Sort-Object NotAfter -Descending | Select-Object -First 1
}

$certificate = Get-WindexBarCodeSigningCertificate
if (-not $certificate) {
    if ($QuietMissingCertificate) {
        exit 0
    }

    Write-SignWarning 'No current-user code signing certificate with a private key was found. Windows Application Control may block the app.'
    exit 0
}

foreach ($file in $Path) {
    if (-not (Test-Path -LiteralPath $file)) {
        throw "File to sign was not found: $file"
    }

    $signature = Set-AuthenticodeSignature -FilePath $file -Certificate $certificate -HashAlgorithm SHA256
    if ($signature.Status -ne 'Valid') {
        Write-SignWarning "Signing did not produce a valid signature for $file. Status: $($signature.Status). $($signature.StatusMessage)"
        continue
    }

    Write-Host "Signed $file with $($certificate.Subject)."
}
