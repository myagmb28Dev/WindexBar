using System.Text.Json.Serialization;
using WindexBar.Core.Models;

namespace WindexBar.Core.Config;

public sealed class WindexBarConfig
{
    public const int CurrentVersion = 4;
    public const int MinRefreshIntervalSeconds = 1;
    public const int DefaultRefreshIntervalSeconds = 30;
    public const int MaxRefreshIntervalSeconds = 3600;
    public const string DefaultLanguage = "en";
    public const string DefaultToggleWindowHotkey = "Alt+O";
    public const bool DefaultStartWithWindows = true;

    [JsonPropertyName("version")]
    public int Version { get; set; } = CurrentVersion;

    [JsonPropertyName("providers")]
    public List<ProviderConfig> Providers { get; set; } = [ProviderConfig.DefaultCodex()];

    [JsonPropertyName("clickThroughHud")]
    public bool ClickThroughHud { get; set; }

    [JsonPropertyName("startWithWindows")]
    public bool StartWithWindows { get; set; } = DefaultStartWithWindows;

    [JsonPropertyName("language")]
    public string Language { get; set; } = DefaultLanguage;

    [JsonPropertyName("hotkeys")]
    public HotkeyConfig Hotkeys { get; set; } = new();

    public static WindexBarConfig Default() => new();

    public WindexBarConfig Normalized()
    {
        Version = CurrentVersion;
        Language = NormalizeLanguage(Language);
        Hotkeys = (Hotkeys ?? new HotkeyConfig()).Normalized();

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

    public static string NormalizeLanguage(string? language) =>
        language?.Trim().ToLowerInvariant() switch
        {
            "ko" or "ko-kr" or "korean" or "\uD55C\uAD6D\uC5B4" => "ko",
            "en" or "en-us" or "english" => "en",
            _ => DefaultLanguage
        };

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

public sealed class HotkeyConfig
{
    [JsonPropertyName("toggleWindow")]
    public string ToggleWindow { get; set; } = WindexBarConfig.DefaultToggleWindowHotkey;

    public HotkeyConfig Normalized()
    {
        ToggleWindow = HotkeyShortcut.NormalizeOrDefault(
            ToggleWindow,
            WindexBarConfig.DefaultToggleWindowHotkey);
        return this;
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
    public int RefreshIntervalSeconds { get; set; } = WindexBarConfig.DefaultRefreshIntervalSeconds;

    public static ProviderConfig DefaultCodex() => new();

    public ProviderConfig Normalized()
    {
        Id = string.IsNullOrWhiteSpace(Id) ? "codex" : Id.Trim().ToLowerInvariant();
        Source = string.IsNullOrWhiteSpace(Source) ? "cli" : Source.Trim().ToLowerInvariant();
        RefreshIntervalSeconds = Math.Clamp(
            RefreshIntervalSeconds <= 0 ? WindexBarConfig.DefaultRefreshIntervalSeconds : RefreshIntervalSeconds,
            WindexBarConfig.MinRefreshIntervalSeconds,
            WindexBarConfig.MaxRefreshIntervalSeconds);
        return this;
    }
}
