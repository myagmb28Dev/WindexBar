namespace WindexBar.Core.Updates;

public static class CodexVersionPolicy
{
    public const string MinimumRequiredVersionText = "0.144.4";
    public static CodexCliVersion MinimumRequiredVersion { get; } = ParseRequired();

    private static CodexCliVersion ParseRequired() =>
        CodexCliVersion.TryParse(MinimumRequiredVersionText, out var version)
            ? version
            : throw new InvalidOperationException("The minimum Codex CLI version is invalid.");
}

public enum CodexVersionStatus
{
    Missing,
    RequiredUpdate,
    RecommendedUpdate,
    Current,
    CompatibleWithoutLatestVersion
}

public sealed record CodexVersionCheckResult(
    CodexVersionStatus Status,
    CodexCliVersion? InstalledVersion,
    CodexCliVersion RequiredVersion,
    CodexCliVersion? LatestVersion,
    string DetectedInstallMethod,
    bool UsedCachedLatestVersion,
    string? ErrorDescription);

public sealed record CodexCommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool IsSuccess => ExitCode == 0;
    public string CombinedOutput => string.Join(
        Environment.NewLine,
        new[] { StandardOutput, StandardError }.Where(value => !string.IsNullOrWhiteSpace(value)));
}

public sealed record CodexUpdateResult(
    bool IsSuccess,
    CodexCliVersion? InstalledVersion,
    CodexCliVersion TargetVersion,
    CodexCommandResult Command,
    string? ErrorDescription);
