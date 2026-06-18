param(
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..')
$InstallerRoot = Join-Path $Root 'artifacts\installer'
$AppExe = Join-Path $env:LOCALAPPDATA 'Programs\Win CodexBar\CodexBar.Windows.exe'

function Find-LatestInstaller {
    if (-not (Test-Path -LiteralPath $InstallerRoot)) {
        return $null
    }

    Get-ChildItem -LiteralPath $InstallerRoot -Recurse -File -Filter 'WinCodexBarSetup.exe' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
}

function Write-Bar {
    param(
        [int]$Percent,
        [string]$Label
    )

    $width = 30
    $safePercent = [Math]::Clamp($Percent, 0, 100)
    $filled = [int][Math]::Round($width * ($safePercent / 100))
    $empty = $width - $filled
    $bar = ('#' * $filled) + ('-' * $empty)
    Write-Host ("`r[{0}] {1,3}%  {2}" -f $bar, $safePercent, $Label) -NoNewline
}

function Complete-Bar {
    param([string]$Label)

    Write-Bar 100 $Label
    Write-Host ''
}

function Invoke-WithProgress {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$Label,
        [int]$StartPercent,
        [int]$EndPercent,
        [string]$LogPath
    )

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $FilePath
    foreach ($argument in $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }
    $startInfo.WorkingDirectory = $Root
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $tick = 0
    while (-not $process.HasExited) {
        $range = [Math]::Max(1, $EndPercent - $StartPercent)
        $wave = [Math]::Min($range - 1, [int]($tick * 2.5))
        Write-Bar ($StartPercent + $wave) $Label
        Start-Sleep -Milliseconds 120
        $tick++
    }

    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    if ($LogPath) {
        Set-Content -LiteralPath $LogPath -Value ($stdout + [Environment]::NewLine + $stderr)
    }

    if ($process.ExitCode -ne 0) {
        Write-Host ''
        if ($LogPath) {
            Write-Host "Failed. Log: $LogPath"
        }
        throw "$Label failed with exit code $($process.ExitCode)."
    }

    Write-Bar $EndPercent $Label
}

try {
    Write-Host ''
    Write-Host 'Win CodexBar console installer'
    Write-Host ''

    $installer = Find-LatestInstaller
    if ($null -eq $installer) {
        Write-Bar 5 'Preparing release build'
        $logPath = Join-Path $Root 'artifacts\installer-build.log'
        Invoke-WithProgress -FilePath (Join-Path $Root 'build-installer.cmd') -Arguments @() -Label 'Building installer' -StartPercent 5 -EndPercent 45 -LogPath $logPath
        $installer = Find-LatestInstaller
    }

    if ($null -eq $installer) {
        throw 'No WinCodexBarSetup.exe was found after building.'
    }

    Write-Bar 50 'Installer ready'
    Start-Sleep -Milliseconds 180

    Invoke-WithProgress -FilePath $installer.FullName -Arguments @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -Label 'Installing Win CodexBar' -StartPercent 55 -EndPercent 92 -LogPath $null

    if (-not $NoLaunch -and (Test-Path -LiteralPath $AppExe)) {
        Write-Bar 96 'Launching app'
        Start-Process -FilePath $AppExe -WorkingDirectory (Split-Path -Parent $AppExe)
    }

    Complete-Bar 'Done'
    Write-Host ''
    Write-Host 'Installed successfully.'
}
catch {
    Write-Host ''
    Write-Host "Install failed: $($_.Exception.Message)"
    exit 1
}
