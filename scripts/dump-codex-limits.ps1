param(
    [string]$Codex = "codex",
    [int]$TimeoutSeconds = 10
)

$ErrorActionPreference = "Stop"

$command = Get-Command $Codex -ErrorAction Stop
$executable = $command.Source
if ([string]::IsNullOrWhiteSpace($executable)) {
    $executable = $Codex
}

$processInfo = [System.Diagnostics.ProcessStartInfo]::new()
$processInfo.FileName = $executable
$processInfo.Arguments = "-s read-only -a untrusted app-server"
$processInfo.RedirectStandardInput = $true
$processInfo.RedirectStandardOutput = $true
$processInfo.RedirectStandardError = $true
$processInfo.UseShellExecute = $false
$processInfo.CreateNoWindow = $true

$process = [System.Diagnostics.Process]::Start($processInfo)
if ($null -eq $process) {
    throw "Failed to start Codex app-server."
}

function Send-RpcRequest {
    param(
        [int]$Id,
        [string]$Method,
        [object]$Params = $null
    )

    if ($null -eq $Params) {
        $Params = [pscustomobject]@{}
    }

    $message = [ordered]@{
        id = $Id
        method = $Method
        params = $Params
    }

    $process.StandardInput.WriteLine(($message | ConvertTo-Json -Compress -Depth 32))
    $process.StandardInput.Flush()
}

function Send-RpcNotification {
    param(
        [string]$Method,
        [object]$Params = $null
    )

    if ($null -eq $Params) {
        $Params = [pscustomobject]@{}
    }

    $message = [ordered]@{
        method = $Method
        params = $Params
    }

    $process.StandardInput.WriteLine(($message | ConvertTo-Json -Compress -Depth 32))
    $process.StandardInput.Flush()
}

function Read-RpcResponse {
    param(
        [int]$Id,
        [int]$TimeoutMs
    )

    $deadline = [DateTimeOffset]::Now.AddMilliseconds($TimeoutMs)
    while ([DateTimeOffset]::Now -lt $deadline) {
        $remainingMs = [int][Math]::Max(1, ($deadline - [DateTimeOffset]::Now).TotalMilliseconds)
        $readTask = $process.StandardOutput.ReadLineAsync()
        if (-not $readTask.Wait($remainingMs)) {
            throw "Timed out waiting for JSON-RPC response id $Id."
        }

        $line = $readTask.Result
        if ($null -eq $line) {
            $stderr = if ($process.HasExited) { $process.StandardError.ReadToEnd() } else { "" }
            throw "Codex app-server closed stdout. $stderr"
        }

        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        try {
            $message = $line | ConvertFrom-Json -ErrorAction Stop
        }
        catch {
            continue
        }

        if ($null -eq $message.id -or [int]$message.id -ne $Id) {
            continue
        }

        if ($null -ne $message.error) {
            $errorMessage = if ($message.error.message) { $message.error.message } else { ($message.error | ConvertTo-Json -Compress -Depth 8) }
            throw "Codex RPC error: $errorMessage"
        }

        return $message.result
    }

    throw "Timed out waiting for JSON-RPC response id $Id."
}

function Format-Window {
    param([object]$Window)

    if ($null -eq $Window) {
        return "-"
    }

    $used = [double]$Window.usedPercent
    $left = 100 - $used
    $reset = "-"
    if ($null -ne $Window.resetsAt) {
        $reset = [DateTimeOffset]::FromUnixTimeSeconds([int64]$Window.resetsAt).ToLocalTime().ToString("MM-dd HH:mm")
    }

    return "{0:0.#}% left ({1:0.#}% used, reset {2})" -f $left, $used, $reset
}

try {
    Send-RpcRequest -Id 1 -Method "initialize" -Params @{
        clientInfo = @{
            name = "wincodexbar-dump"
            version = "0.1.0"
        }
    }
    [void](Read-RpcResponse -Id 1 -TimeoutMs ($TimeoutSeconds * 1000))
    Send-RpcNotification -Method "initialized"

    Send-RpcRequest -Id 2 -Method "account/rateLimits/read"
    $result = Read-RpcResponse -Id 2 -TimeoutMs ($TimeoutSeconds * 1000)

    $bucketProperties = @()
    if ($null -ne $result.rateLimitsByLimitId) {
        $bucketProperties = @($result.rateLimitsByLimitId.PSObject.Properties)
    }

    if ($bucketProperties.Count -eq 0) {
        $bucketProperties = @([pscustomobject]@{
            Name = "rateLimits"
            Value = $result.rateLimits
        })
    }

    $rows = foreach ($property in $bucketProperties) {
        $bucket = $property.Value
        $name = $bucket.limitName
        if ([string]::IsNullOrWhiteSpace($name)) {
            $name = $bucket.limitId
        }
        if ([string]::IsNullOrWhiteSpace($name)) {
            $name = $property.Name
        }

        [pscustomobject]@{
            Key = $property.Name
            Name = $name
            Current = Format-Window $bucket.primary
            Weekly = Format-Window $bucket.secondary
        }
    }

    $rows | Format-Table -AutoSize
}
finally {
    if (-not $process.HasExited) {
        $process.Kill()
    }

    $process.Dispose()
}
