param(
    [string]$DotNet,
    [string]$RootPath
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RootPath)) {
    $RootPath = Split-Path -Parent $PSScriptRoot
}

$RootPath = (Resolve-Path -LiteralPath $RootPath).Path
if ([string]::IsNullOrWhiteSpace($DotNet)) {
    $localDotNet = Join-Path $RootPath '.dotnet\dotnet.exe'
    $programFilesDotNet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $localDotNet) {
        $DotNet = $localDotNet
    }
    elseif (Test-Path -LiteralPath $programFilesDotNet) {
        $DotNet = $programFilesDotNet
    }
    else {
        $DotNet = 'dotnet'
    }
}

$Project = Join-Path $RootPath 'src\WindexBar.Windows'
$RunDir = Join-Path $RootPath 'artifacts\run\win-x64'
$AppExe = Join-Path $RunDir 'WindexBar.Windows.exe'
$WatchedRoots = @(
    (Join-Path $RootPath 'src'),
    (Join-Path $RootPath 'Directory.Build.props'),
    (Join-Path $RootPath 'global.json')
)

$PublishArgs = @(
    'publish',
    $Project,
    '-c', 'Debug',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-p:WindowsPackageType=None',
    '-p:WindowsAppSDKSelfContained=true',
    '-p:PublishReadyToRun=false',
    '-p:PublishTrimmed=false',
    '-p:NuGetAudit=false',
    '-o', $RunDir
)

function Stop-WindexBar {
    foreach ($process in Get-Process -Name 'WindexBar.Windows' -ErrorAction SilentlyContinue) {
        try {
            if ($process.Path -eq $AppExe) {
                Stop-Process -Id $process.Id -Force
            }
        }
        catch {
        }
        finally {
            $process.Dispose()
        }
    }
}

function Restart-WindexBar {
    Write-Host ''
    Write-Host '[WindexBar] Publishing debug build...'
    Stop-WindexBar
    if (Test-Path -LiteralPath $AppExe) {
        Remove-Item -LiteralPath $AppExe -Force -ErrorAction SilentlyContinue
    }

    & $DotNet @PublishArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Publish failed with exit code $LASTEXITCODE. Fix the error and save a file to retry."
        return
    }

    Start-Process -FilePath $AppExe -WorkingDirectory $RunDir | Out-Null
    Write-Host '[WindexBar] Running.'
}

function Test-F5Pressed {
    try {
        if (-not [Console]::KeyAvailable) {
            return $false
        }

        $key = [Console]::ReadKey($true)
        return $key.Key -eq [ConsoleKey]::F5
    }
    catch {
        return $false
    }
}

function New-Watcher {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $watcher = [System.IO.FileSystemWatcher]::new()
    $watcher.Path = $Path
    $watcher.IncludeSubdirectories = $true
    $watcher.NotifyFilter = [System.IO.NotifyFilters]'FileName, LastWrite, Size'
    $watcher.Filter = '*.*'
    $watcher.EnableRaisingEvents = $true
    return $watcher
}

$pendingRestart = $false
$promptedForRestart = $false
$watchers = New-Object System.Collections.Generic.List[System.IO.FileSystemWatcher]
$subscriptions = New-Object System.Collections.Generic.List[System.Management.Automation.PSEventSubscriber]
$sourceId = 'WindexBarRunWatch'

try {
    foreach ($root in $WatchedRoots) {
        if ((Test-Path -LiteralPath $root -PathType Container)) {
            $watcher = New-Watcher -Path $root
            $watchers.Add($watcher)
            foreach ($eventName in @('Changed', 'Created', 'Deleted', 'Renamed')) {
                $subscriptions.Add((Register-ObjectEvent -InputObject $watcher -EventName $eventName -SourceIdentifier "$sourceId.$eventName.$($watchers.Count)"))
            }
        }
    }

    Restart-WindexBar
    Write-Host '[WindexBar] Watching for changes. Press F5 to restart after changes are detected.'
    while ($true) {
        $event = Wait-Event -Timeout 1
        if ($null -ne $event -and $event.SourceIdentifier.StartsWith($sourceId, [StringComparison]::Ordinal)) {
            Remove-Event -EventIdentifier $event.EventIdentifier -ErrorAction SilentlyContinue
            $pendingRestart = $true
            if (-not $promptedForRestart) {
                Write-Host '[WindexBar] Changes detected. Press F5 to restart.'
                $promptedForRestart = $true
            }
        }

        if (-not $pendingRestart) {
            continue
        }

        if (-not (Test-F5Pressed)) {
            continue
        }

        $pendingRestart = $false
        $promptedForRestart = $false
        Restart-WindexBar
    }
}
finally {
    foreach ($subscription in $subscriptions) {
        Unregister-Event -SubscriptionId $subscription.Id -ErrorAction SilentlyContinue
    }

    foreach ($watcher in $watchers) {
        $watcher.Dispose()
    }
}
