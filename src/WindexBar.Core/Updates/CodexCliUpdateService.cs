using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using WindexBar.Core.Config;
using WindexBar.Core.Providers.Codex;

namespace WindexBar.Core.Updates;

public interface ICodexVersionSource
{
    Task<CodexCliVersion> FetchLatestStableVersionAsync(CancellationToken cancellationToken);
}

public interface ICodexProcessRunner
{
    Task<CodexCommandResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

public sealed class GithubCodexVersionSource(HttpClient client) : ICodexVersionSource
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/openai/codex/releases/latest";

    public async Task<CodexCliVersion> FetchLatestStableVersionAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("WindexBar", "1.0"));
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("tag_name", out var tag)
            || !CodexCliVersion.TryParse(tag.GetString(), out var version))
        {
            throw new InvalidOperationException("The latest Codex release did not include a valid version tag.");
        }

        return version;
    }
}

public sealed class CodexProcessRunner : ICodexProcessRunner
{
    public async Task<CodexCommandResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = BuildStartInfo(executable, arguments);
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start {Path.GetFileName(executable)}.");
        }

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new CodexCommandResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }

    private static ProcessStartInfo BuildStartInfo(string executable, IReadOnlyList<string> arguments)
    {
        var extension = Path.GetExtension(executable);
        var isCommandScript = string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase);
        var startInfo = new ProcessStartInfo
        {
            FileName = isCommandScript ? Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe" : executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (isCommandScript)
        {
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/s");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(executable);
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}

public sealed class CodexCliUpdateService
{
    public static readonly TimeSpan LatestVersionCacheDuration = TimeSpan.FromHours(24);

    private readonly ICodexVersionSource _versionSource;
    private readonly ICodexProcessRunner _processRunner;
    private readonly Func<DateTimeOffset> _now;
    private readonly IReadOnlyDictionary<string, string>? _environment;

    public CodexCliUpdateService(
        ICodexVersionSource versionSource,
        ICodexProcessRunner processRunner,
        Func<DateTimeOffset>? now = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        _versionSource = versionSource;
        _processRunner = processRunner;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _environment = environment;
    }

    public async Task<CodexVersionCheckResult> CheckAsync(
        CodexUpdateConfig config,
        bool forceLatestVersionRefresh,
        CancellationToken cancellationToken)
    {
        var executable = CommandLocator.ResolveExecutable(null, _environment);
        if (executable is null)
        {
            return new CodexVersionCheckResult(
                CodexVersionStatus.Missing,
                null,
                CodexVersionPolicy.MinimumRequiredVersion,
                ReadCachedVersion(config),
                CodexInstallMethodNames.PowerShell,
                true,
                "Codex CLI was not found on PATH.");
        }

        var installed = await ReadInstalledVersionAsync(executable, cancellationToken).ConfigureAwait(false);
        var detectedMethod = DetectInstallMethod(executable);
        var cachedVersion = ReadCachedVersion(config);
        var cacheIsFresh = !forceLatestVersionRefresh
            && cachedVersion is not null
            && config.LastCheckedAt is { } checkedAt
            && _now() - checkedAt < LatestVersionCacheDuration;
        CodexCliVersion? latest = cachedVersion;
        string? latestError = null;

        if (!cacheIsFresh)
        {
            try
            {
                latest = await _versionSource.FetchLatestStableVersionAsync(cancellationToken).ConfigureAwait(false);
                config.LatestVersion = latest.Value.ToString();
                config.LastCheckedAt = _now();
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                latestError = error.Message;
            }
        }

        var status = installed < CodexVersionPolicy.MinimumRequiredVersion
            ? CodexVersionStatus.RequiredUpdate
            : latest is null
                ? CodexVersionStatus.CompatibleWithoutLatestVersion
                : installed < latest.Value
                    ? CodexVersionStatus.RecommendedUpdate
                    : CodexVersionStatus.Current;

        return new CodexVersionCheckResult(
            status,
            installed,
            CodexVersionPolicy.MinimumRequiredVersion,
            latest,
            detectedMethod,
            cacheIsFresh,
            latestError);
    }

    public async Task<CodexUpdateResult> UpdateAsync(
        string installMethod,
        string? customCommand,
        CodexCliVersion targetVersion,
        CancellationToken cancellationToken)
    {
        var normalizedMethod = CodexInstallMethodNames.Normalize(installMethod);
        if (normalizedMethod == CodexInstallMethodNames.Auto)
        {
            var executable = CommandLocator.ResolveExecutable(null, _environment);
            normalizedMethod = executable is null ? CodexInstallMethodNames.PowerShell : DetectInstallMethod(executable);
        }

        var command = BuildUpdateCommand(normalizedMethod, customCommand, targetVersion);
        var result = await _processRunner.RunAsync(
            command.Executable,
            command.Arguments,
            cancellationToken).ConfigureAwait(false);
        var currentExecutable = CommandLocator.ResolveExecutable(null, _environment);
        CodexCliVersion? installed = null;
        if (currentExecutable is not null)
        {
            try
            {
                installed = await ReadInstalledVersionAsync(currentExecutable, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                return new CodexUpdateResult(false, null, targetVersion, result, error.Message);
            }
        }

        var succeeded = result.IsSuccess && installed is not null && installed.Value >= targetVersion;
        var errorDescription = succeeded
            ? null
            : !result.IsSuccess
                ? $"The update command exited with code {result.ExitCode}."
                : installed is null
                    ? "Codex CLI was not found after the update."
                    : $"Codex CLI {installed} is still older than {targetVersion}.";
        return new CodexUpdateResult(succeeded, installed, targetVersion, result, errorDescription);
    }

    public static string DetectInstallMethod(string executablePath)
    {
        var normalized = executablePath.Replace('/', '\\');
        if (normalized.Contains("\\.bun\\", StringComparison.OrdinalIgnoreCase))
        {
            return CodexInstallMethodNames.Bun;
        }

        if (normalized.Contains("\\npm\\", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("node_modules", StringComparison.OrdinalIgnoreCase))
        {
            return CodexInstallMethodNames.Npm;
        }

        if (normalized.Contains("WinGet", StringComparison.OrdinalIgnoreCase))
        {
            return CodexInstallMethodNames.WinGet;
        }

        if (normalized.Contains("Homebrew", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("\\brew\\", StringComparison.OrdinalIgnoreCase))
        {
            return CodexInstallMethodNames.Homebrew;
        }

        return CodexInstallMethodNames.PowerShell;
    }

    public static (string Executable, IReadOnlyList<string> Arguments) BuildUpdateCommand(
        string installMethod,
        string? customCommand,
        CodexCliVersion targetVersion)
    {
        var method = CodexInstallMethodNames.Normalize(installMethod);
        var script = method switch
        {
            CodexInstallMethodNames.PowerShell => "irm https://chatgpt.com/codex/install.ps1 | iex",
            CodexInstallMethodNames.Npm => "npm install -g @openai/codex@latest",
            CodexInstallMethodNames.Bun => "bun install -g @openai/codex@latest",
            CodexInstallMethodNames.Homebrew => "brew upgrade --cask codex",
            CodexInstallMethodNames.WinGet => "winget upgrade --id OpenAI.Codex --exact --source winget --accept-source-agreements --accept-package-agreements --silent",
            CodexInstallMethodNames.Custom when !string.IsNullOrWhiteSpace(customCommand) =>
                customCommand.Replace("{latestVersion}", targetVersion.ToString(), StringComparison.OrdinalIgnoreCase),
            CodexInstallMethodNames.Custom => throw new InvalidOperationException("A custom update command is required."),
            _ => throw new InvalidOperationException($"Unsupported Codex installation method: {installMethod}")
        };

        return (
            "powershell.exe",
            ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script]);
    }

    private async Task<CodexCliVersion> ReadInstalledVersionAsync(string executable, CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(executable, ["--version"], cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || !CodexCliVersion.TryParse(result.CombinedOutput, out var version))
        {
            throw new InvalidOperationException("Codex CLI returned an invalid version.");
        }

        return version;
    }

    private static CodexCliVersion? ReadCachedVersion(CodexUpdateConfig config) =>
        CodexCliVersion.TryParse(config.LatestVersion, out var version) ? version : null;
}
