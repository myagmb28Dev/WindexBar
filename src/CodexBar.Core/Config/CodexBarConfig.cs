using System.Text.Json.Serialization;
using CodexBar.Core.Models;

namespace CodexBar.Core.Config;

public sealed class CodexBarConfig
{
    public const int CurrentVersion = 2;
    public const int MinRefreshIntervalSeconds = 1;
    public const int DefaultRefreshIntervalSeconds = 30;
    public const int MaxRefreshIntervalSeconds = 3600;

    [JsonPropertyName("version")]
    public int Version { get; set; } = CurrentVersion;

    [JsonPropertyName("providers")]
    public List<ProviderConfig> Providers { get; set; } = [ProviderConfig.DefaultCodex()];

    [JsonPropertyName("clickThroughHud")]
    public bool ClickThroughHud { get; set; }

    public static CodexBarConfig Default() => new();

    public CodexBarConfig Normalized()
    {
        Version = CurrentVersion;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<ProviderConfig>();
        foreach (var provider in Providers)
        {
            if (string.IsNullOrWhiteSpace(provider.Id) || !seen.Add(provider.Id))
            {
                continue;
            }

            normalized.Add(provider.Normalized());
        }

        if (!seen.Contains("codex"))
        {
            normalized.Add(ProviderConfig.DefaultCodex());
        }

        Providers = normalized;
        return this;
    }

    public ProviderConfig GetProviderConfig(UsageProvider provider)
    {
        var id = provider.ToConfigValue();
        return Providers.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? ProviderConfig.DefaultCodex();
    }

    public void SetProviderConfig(ProviderConfig value)
    {
        var index = Providers.FindIndex(p => string.Equals(p.Id, value.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            Providers[index] = value.Normalized();
            return;
        }

        Providers.Add(value.Normalized());
    }
}

public sealed class ProviderConfig
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "codex";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("source")]
    public string Source { get; set; } = "cli";

    [JsonPropertyName("refreshIntervalSeconds")]
    public int RefreshIntervalSeconds { get; set; } = CodexBarConfig.DefaultRefreshIntervalSeconds;

    public static ProviderConfig DefaultCodex() => new();

    public ProviderConfig Normalized()
    {
        Id = string.IsNullOrWhiteSpace(Id) ? "codex" : Id.Trim().ToLowerInvariant();
        Source = string.IsNullOrWhiteSpace(Source) ? "cli" : Source.Trim().ToLowerInvariant();
        RefreshIntervalSeconds = Math.Clamp(
            RefreshIntervalSeconds <= 0 ? CodexBarConfig.DefaultRefreshIntervalSeconds : RefreshIntervalSeconds,
            CodexBarConfig.MinRefreshIntervalSeconds,
            CodexBarConfig.MaxRefreshIntervalSeconds);
        return this;
    }
}

