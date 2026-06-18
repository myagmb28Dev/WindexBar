namespace CodexBar.Core.Models;

public enum UsageProvider
{
    Codex
}

public static class UsageProviderNames
{
    public static string ToConfigValue(this UsageProvider provider) => provider switch
    {
        UsageProvider.Codex => "codex",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
    };

    public static bool TryParse(string? value, out UsageProvider provider)
    {
        if (string.Equals(value, "codex", StringComparison.OrdinalIgnoreCase))
        {
            provider = UsageProvider.Codex;
            return true;
        }

        provider = default;
        return false;
    }
}

