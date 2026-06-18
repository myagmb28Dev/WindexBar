param(
    [switch]$NoLaunch,
    [switch]$NoStartup
)

$ErrorActionPreference = 'Stop'
$AppName = 'WindexBar'
$AppVersion = '1.0.0'
$Publisher = 'WindexBar'
$Root = Resolve-Path (Join-Path $PSScriptRoot '..')
$RootPath = $Root.Path
$ArtifactsRoot = Join-Path $RootPath 'artifacts'
$PublishDir = Join-Path $ArtifactsRoot 'install\win-x64'
$ProgramsRoot = Join-Path $env:LOCALAPPDATA 'Programs'
$InstallDir = Join-Path $ProgramsRoot $AppName
$AppExe = Join-Path $InstallDir 'WindexBar.Windows.exe'
$ProgramsMenuRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$StartupRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::Startup)
$DesktopRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
$StartMenuDir = Join-Path $ProgramsMenuRoot $AppName
$StartMenuShortcut = Join-Path $StartMenuDir "$AppName.lnk"
$StartupShortcut = Join-Path $StartupRoot "$AppName.lnk"
$UninstallScriptSource = Join-Path $RootPath 'scripts\uninstall-console.ps1'
$SignScriptSource = Join-Path $RootPath 'scripts\sign-app.ps1'
$UninstallScript = Join-Path $InstallDir 'uninstall.ps1'
$UninstallRegistryKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$AppName"

function ConvertTo-CommandLine {
    param([string[]]$Arguments)

    $quoted = foreach ($argument in $Arguments) {
        if ($null -eq $argument) {
            continue
        }

        $text = [string]$argument
        if ($text.Length -eq 0) {
            '""'
        }
        elseif ($text -match '[\s"]') {
            '"' + ($text -replace '"', '\"') + '"'
        }
        else {
            $text
        }
    }

    [string]::Join(' ', $quoted)
}

function Write-Bar {
    param(
        [int]$Percent,
        [string]$Label
    )

    $width = 32
    $safePercent = [Math]::Max(0, [Math]::Min(100, $Percent))
    $filled = [int][Math]::Round($width * ($safePercent / 100))
    $filledChar = [char]0x2588
    $emptyChar = [char]0x2591
    $palette = @('DarkMagenta', 'Magenta', 'Blue', 'Cyan')

    Write-Host "`r[" -NoNewline
    for ($i = 0; $i -lt $width; $i++) {
        if ($i -lt $filled) {
            $colorIndex = [Math]::Min($palette.Count - 1, [int][Math]::Floor(($i / [Math]::Max(1, $width - 1)) * $palette.Count))
            Write-Host $filledChar -ForegroundColor $palette[$colorIndex] -NoNewline
        }
        else {
            Write-Host $emptyChar -ForegroundColor DarkGray -NoNewline
        }
    }

    Write-Host ("] {0,3}%  {1}    " -f $safePercent, $Label) -NoNewline
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
    $startInfo.Arguments = ConvertTo-CommandLine $Arguments
    $startInfo.WorkingDirectory = $RootPath
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $tick = 0
    while (-not $process.HasExited) {
        $range = [Math]::Max(1, $EndPercent - $StartPercent)
        $wave = [Math]::Min($range - 1, [int]($tick * 1.8))
        Write-Bar ($StartPercent + $wave) $Label
        Start-Sleep -Milliseconds 130
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

function Stop-RunningApp {
    $processes = @(Get-Process -Name 'WindexBar.Windows' -ErrorAction SilentlyContinue)
    if ($processes.Count -eq 0) {
        return
    }

    Write-Bar 8 'Stopping running app'
    foreach ($process in $processes) {
        try {
            $process.Kill()
            [void]$process.WaitForExit(3000)
        }
        catch {
        }
    }
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

function Reset-InstallDirectory {
    if (-not (Test-IsChildPath -Path $InstallDir -Parent $ProgramsRoot)) {
        throw "Refusing to install outside $ProgramsRoot."
    }

    if (Test-Path -LiteralPath $InstallDir) {
        Remove-Item -LiteralPath $InstallDir -Recurse -Force
    }

    [void](New-Item -ItemType Directory -Force -Path $InstallDir)
}

function Copy-PublishedFiles {
    param(
        [string]$Source,
        [string]$Destination,
        [int]$StartPercent,
        [int]$EndPercent
    )

    if (-not (Test-Path -LiteralPath $Source)) {
        throw "Publish output was not found: $Source"
    }

    Reset-InstallDirectory
    $separator = [IO.Path]::DirectorySeparatorChar
    $sourceRoot = [IO.Path]::GetFullPath($Source).TrimEnd($separator) + $separator
    $files = @(Get-ChildItem -LiteralPath $Source -Recurse -File -Force)
    if ($files.Count -eq 0) {
        throw "Publish output is empty: $Source"
    }

    for ($i = 0; $i -lt $files.Count; $i++) {
        $file = $files[$i]
        $relativePath = $file.FullName.Substring($sourceRoot.Length)
        $targetPath = Join-Path $Destination $relativePath
        $targetDir = Split-Path -Parent $targetPath
        if (-not (Test-Path -LiteralPath $targetDir)) {
            [void](New-Item -ItemType Directory -Force -Path $targetDir)
        }

        Copy-Item -LiteralPath $file.FullName -Destination $targetPath -Force
        if (($i % 10) -eq 0 -or $i -eq ($files.Count - 1)) {
            $percent = $StartPercent + [int](($EndPercent - $StartPercent) * (($i + 1) / [Math]::Max(1, $files.Count)))
            Write-Bar $percent 'Installing files'
        }
    }
}

function New-AppShortcut {
    param(
        [string]$ShortcutPath,
        [string]$TargetPath,
        [string]$WorkingDirectory,
        [string]$IconPath
    )

    $shortcutDir = Split-Path -Parent $ShortcutPath
    if (-not (Test-Path -LiteralPath $shortcutDir)) {
        [void](New-Item -ItemType Directory -Force -Path $shortcutDir)
    }

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    if (Test-Path -LiteralPath $IconPath) {
        $shortcut.IconLocation = $IconPath
    }
    $shortcut.Save()
}

function Get-DirectorySizeKb {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return 0
    }

    $size = 0L
    foreach ($file in Get-ChildItem -LiteralPath $Path -Recurse -File -Force -ErrorAction SilentlyContinue) {
        $size += $file.Length
    }

    [int][Math]::Max(1, [Math]::Ceiling($size / 1KB))
}

function Copy-Uninstaller {
    if (-not (Test-Path -LiteralPath $UninstallScriptSource)) {
        throw "Uninstall script was not found: $UninstallScriptSource"
    }

    Copy-Item -LiteralPath $UninstallScriptSource -Destination $UninstallScript -Force
}

function Set-UninstallRegistryValue {
    param(
        [string]$Name,
        [object]$Value,
        [string]$PropertyType = 'String'
    )

    New-ItemProperty -Path $UninstallRegistryKey -Name $Name -Value $Value -PropertyType $PropertyType -Force | Out-Null
}

function Register-UninstallEntry {
    param([string]$IconPath)

    $powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $uninstallCommand = '"{0}" -NoProfile -ExecutionPolicy Bypass -File "{1}"' -f $powershell, $UninstallScript
    $quietUninstallCommand = $uninstallCommand + ' -Quiet'

    New-Item -Path $UninstallRegistryKey -Force | Out-Null
    Set-UninstallRegistryValue -Name 'DisplayName' -Value $AppName
    Set-UninstallRegistryValue -Name 'DisplayVersion' -Value $AppVersion
    Set-UninstallRegistryValue -Name 'Publisher' -Value $Publisher
    Set-UninstallRegistryValue -Name 'InstallLocation' -Value $InstallDir
    Set-UninstallRegistryValue -Name 'DisplayIcon' -Value ('"{0}",0' -f $IconPath)
    Set-UninstallRegistryValue -Name 'UninstallString' -Value $uninstallCommand
    Set-UninstallRegistryValue -Name 'QuietUninstallString' -Value $quietUninstallCommand
    Set-UninstallRegistryValue -Name 'InstallDate' -Value (Get-Date -Format 'yyyyMMdd')
    Set-UninstallRegistryValue -Name 'EstimatedSize' -Value (Get-DirectorySizeKb $InstallDir) -PropertyType 'DWord'
    Set-UninstallRegistryValue -Name 'NoModify' -Value 1 -PropertyType 'DWord'
    Set-UninstallRegistryValue -Name 'NoRepair' -Value 1 -PropertyType 'DWord'
}

try {
    Write-Host ''
    Write-Host 'WindexBar console installer'
    Write-Host ''

    if (-not (Test-Path -LiteralPath $ArtifactsRoot)) {
        [void](New-Item -ItemType Directory -Force -Path $ArtifactsRoot)
    }

    $dotnet = Join-Path $RootPath '.dotnet\dotnet.exe'
    if (-not (Test-Path -LiteralPath $dotnet)) {
        throw "Bundled .NET was not found: $dotnet"
    }

    $project = Join-Path $RootPath 'src\WindexBar.Windows\WindexBar.Windows.csproj'
    $logPath = Join-Path $ArtifactsRoot 'install-build.log'
    $publishArgs = @(
        'publish',
        $project,
        '-c',
        'Release',
        '-r',
        'win-x64',
        '--self-contained',
        'true',
        '-p:WindowsPackageType=None',
        '-p:WindowsAppSDKSelfContained=true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:PublishReadyToRun=false',
        '-p:PublishTrimmed=false',
        '-p:NuGetAudit=false',
        '-o',
        $PublishDir
    )

    Write-Bar 3 'Preparing install'
    Stop-RunningApp
    if ((Test-Path -LiteralPath $PublishDir) -and (Test-IsChildPath -Path $PublishDir -Parent $ArtifactsRoot)) {
        Remove-Item -LiteralPath $PublishDir -Recurse -Force
    }
    Invoke-WithProgress -FilePath $dotnet -Arguments $publishArgs -Label 'Building app' -StartPercent 10 -EndPercent 52 -LogPath $logPath
    if (Test-Path -LiteralPath $SignScriptSource) {
        Write-Bar 55 'Signing app'
        & $SignScriptSource -Path (Join-Path $PublishDir 'WindexBar.Windows.exe') -WarnOnly
    }

    Stop-RunningApp
    Copy-PublishedFiles -Source $PublishDir -Destination $InstallDir -StartPercent 58 -EndPercent 86

    $iconPath = Join-Path $InstallDir 'Assets\TrayIcon.ico'
    Write-Bar 90 'Creating shortcuts'
    New-AppShortcut -ShortcutPath $StartMenuShortcut -TargetPath $AppExe -WorkingDirectory $InstallDir -IconPath $iconPath
    if (-not $NoStartup) {
        New-AppShortcut -ShortcutPath $StartupShortcut -TargetPath $AppExe -WorkingDirectory $InstallDir -IconPath $iconPath
    }

    Write-Bar 93 'Registering uninstall'
    Copy-Uninstaller
    Register-UninstallEntry -IconPath $iconPath

    $launchWarning = $null
    if (-not $NoLaunch -and (Test-Path -LiteralPath $AppExe)) {
        Write-Bar 96 'Launching app'
        try {
            Start-Process -FilePath $AppExe -WorkingDirectory $InstallDir
        }
        catch {
            $launchWarning = $_.Exception.Message
        }
    }

    Complete-Bar 'Done'
    Write-Host ''
    Write-Host "Installed to: $InstallDir"
    if ($launchWarning) {
        Write-Host "Installed, but Windows blocked launching the app: $launchWarning" -ForegroundColor Yellow
    }
    else {
        Write-Host 'Installed successfully.'
    }
}
catch {
    Write-Host ''
    Write-Host "Install failed: $($_.Exception.Message)"
    exit 1
}
