namespace WindexBar.Core.Config;

public static class CodexInstallMethodNames
{
    public const string Auto = "auto";
    public const string PowerShell = "powershell";
    public const string Npm = "npm";
    public const string Bun = "bun";
    public const string Homebrew = "homebrew";
    public const string WinGet = "winget";
    public const string Custom = "custom";

    private static readonly HashSet<string> KnownMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        Auto,
        PowerShell,
        Npm,
        Bun,
        Homebrew,
        WinGet,
        Custom
    };

    public static string Normalize(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is not null && KnownMethods.Contains(normalized) ? normalized : Auto;
    }
}
