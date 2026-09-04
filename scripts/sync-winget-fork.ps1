[CmdletBinding()]
param(
    [string] $Token = $env:WINGET_CREATE_GITHUB_TOKEN,

    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string] $ForkRepository = 'myagmb28Dev/winget-pkgs',

    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string] $UpstreamRepository = 'microsoft/winget-pkgs',

    [ValidatePattern('^[A-Za-z0-9._/-]+$')]
    [string] $Branch = 'master',

    [ValidateRange(1, 10)]
    [int] $MaxSyncAttempts = 5
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$headers = @{
    Accept = 'application/vnd.github+json'
    'User-Agent' = 'WindexBar-release-workflow'
    'X-GitHub-Api-Version' = '2022-11-28'
}

if (-not [string]::IsNullOrWhiteSpace($Token)) {
    $headers.Authorization = "Bearer $Token"
}

function Invoke-GitHubApi {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Get', 'Patch')]
        [string] $Method,

        [Parameter(Mandatory)]
        [string] $Path,

        [hashtable] $Body
    )

    $parameters = @{
        Uri = "https://api.github.com/$Path"
        Method = $Method
        Headers = $headers
    }

    if ($Body) {
        $parameters.ContentType = 'application/json'
        $parameters.Body = $Body | ConvertTo-Json -Compress
    }

    Invoke-RestMethod @parameters
}

function Get-BranchSha {
    param(
        [Parameter(Mandatory)]
        [string] $Repository
    )

    $encodedBranch = [Uri]::EscapeDataString($Branch)
    $branchDetails = Invoke-GitHubApi -Method Get -Path "repos/$Repository/branches/$encodedBranch"
    return [string] $branchDetails.commit.sha
}

function Write-SyncSummary {
    param(
        [Parameter(Mandatory)]
        [string] $Message
    )

    Write-Host $Message
    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
        '### WinGet fork synchronization' >> $env:GITHUB_STEP_SUMMARY
        $Message >> $env:GITHUB_STEP_SUMMARY
    }
}

$fork = Invoke-GitHubApi -Method Get -Path "repos/$ForkRepository"
if (-not $fork.fork -or
    -not $fork.parent -or
    -not [string]::Equals([string] $fork.parent.full_name, $UpstreamRepository, [StringComparison]::OrdinalIgnoreCase)) {
    throw "$ForkRepository is not a fork of $UpstreamRepository. Refusing to update it."
}

for ($attempt = 1; $attempt -le $MaxSyncAttempts; $attempt++) {
    $upstreamSha = Get-BranchSha -Repository $UpstreamRepository
    $forkSha = Get-BranchSha -Repository $ForkRepository

    if ([string]::Equals($forkSha, $upstreamSha, [StringComparison]::OrdinalIgnoreCase)) {
        Write-SyncSummary "$ForkRepository@$Branch is synchronized with $UpstreamRepository at $upstreamSha."
        return
    }

    $comparison = Invoke-GitHubApi -Method Get -Path "repos/$UpstreamRepository/compare/$forkSha...$upstreamSha"
    $mergeBaseSha = [string] $comparison.merge_base_commit.sha
    $isFastForward =
        [string]::Equals($mergeBaseSha, $forkSha, [StringComparison]::OrdinalIgnoreCase) -and
        [int] $comparison.behind_by -eq 0

    if (-not $isFastForward) {
        throw "$ForkRepository@$Branch has commits that are not in $UpstreamRepository@$Branch. Refusing to force-update the fork."
    }

    if ([string]::IsNullOrWhiteSpace($Token)) {
        throw 'WINGET_TOKEN is required to synchronize the WinGet fork.'
    }

    Write-Host "Fast-forwarding $ForkRepository@$Branch from $forkSha to $upstreamSha (attempt $attempt of $MaxSyncAttempts)."
    $encodedBranch = [Uri]::EscapeDataString($Branch)
    $null = Invoke-GitHubApi -Method Patch -Path "repos/$ForkRepository/git/refs/heads/$encodedBranch" -Body @{
        sha = $upstreamSha
        force = $false
    }

    Start-Sleep -Seconds 2
}

$finalUpstreamSha = Get-BranchSha -Repository $UpstreamRepository
$finalForkSha = Get-BranchSha -Repository $ForkRepository
if (-not [string]::Equals($finalForkSha, $finalUpstreamSha, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Could not synchronize $ForkRepository@$Branch after $MaxSyncAttempts attempts. Fork: $finalForkSha; upstream: $finalUpstreamSha."
}

Write-SyncSummary "$ForkRepository@$Branch is synchronized with $UpstreamRepository at $finalUpstreamSha."
