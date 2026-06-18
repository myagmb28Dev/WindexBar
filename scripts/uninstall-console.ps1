param(
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'
$AppName = 'WindexBar'
$ProgramsRoot = Join-Path $env:LOCALAPPDATA 'Programs'
$InstallDir = Join-Path $ProgramsRoot $AppName
$ProgramsMenuRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$StartupRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::Startup)
$DesktopRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
$UninstallRegistryKeys = @(
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$AppName"
)

function Test-IsChildPath {
    param(
        [string]$Path,
        [string]$Parent
    )

    $separator = [IO.Path]::DirectorySeparatorChar
    $fullPath = [IO.Path]::GetFullPath($Path).TrimEnd($separator) + $separator
    $parentPath = [IO.Path]::GetFullPath($Parent).TrimEnd($separator) + $separator
    $fullPath.StartsWith($parentPath, [StringComparison]::OrdinalIgnoreCase)
}

function Remove-IfSafe {
    param(
        [string]$Path,
        [string]$Parent
    )

    if ((Test-Path -LiteralPath $Path) -and (Test-IsChildPath -Path $Path -Parent $Parent)) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Stop-RunningApp {
    $processes = @(Get-Process -Name 'WindexBar.Windows' -ErrorAction SilentlyContinue)
    foreach ($process in $processes) {
        try {
            $process.Kill()
            [void]$process.WaitForExit(3000)
        }
        catch {
        }
    }
}

function Remove-Shortcuts {
    $startMenuNames = @('WindexBar')
    foreach ($name in $startMenuNames) {
        Remove-IfSafe -Path (Join-Path $ProgramsMenuRoot $name) -Parent $ProgramsMenuRoot
    }

    $shortcutNames = @('WindexBar.lnk')
    foreach ($name in $shortcutNames) {
        Remove-IfSafe -Path (Join-Path $StartupRoot $name) -Parent $StartupRoot
        Remove-IfSafe -Path (Join-Path $DesktopRoot $name) -Parent $DesktopRoot
    }
}

function Remove-UninstallRegistry {
    foreach ($key in $UninstallRegistryKeys) {
        Remove-Item -Path $key -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Start-InstallFolderRemoval {
    if (-not (Test-Path -LiteralPath $InstallDir)) {
        return
    }

    if (-not (Test-IsChildPath -Path $InstallDir -Parent $ProgramsRoot)) {
        throw "Refusing to remove outside $ProgramsRoot."
    }

    $command = 'ping 127.0.0.1 -n 2 >nul & rmdir /s /q "{0}"' -f $InstallDir
    Start-Process -FilePath $env:COMSPEC -ArgumentList @('/c', $command) -WindowStyle Hidden
}

try {
    if (-not $Quiet) {
        Write-Host 'Uninstalling WindexBar...'
    }

    Stop-RunningApp
    Remove-Shortcuts
    Remove-UninstallRegistry
    Start-InstallFolderRemoval

    if (-not $Quiet) {
        Write-Host 'WindexBar was uninstalled.'
    }
}
catch {
    if (-not $Quiet) {
        Write-Host "Uninstall failed: $($_.Exception.Message)"
    }
    exit 1
}
