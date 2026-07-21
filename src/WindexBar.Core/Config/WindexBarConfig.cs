using System.Text.Json.Serialization;
using WindexBar.Core.Models;

namespace WindexBar.Core.Config;

public sealed class WindexBarConfig
{
    public const int CurrentVersion = 7;
    public const int MinRefreshIntervalSeconds = 1;
    public const int DefaultRefreshIntervalSeconds = 30;
    public const int MaxRefreshIntervalSeconds = 3600;
    public const string DefaultLanguage = "en";
    public const string DefaultToggleWindowHotkey = "Alt+O";
    public const string DefaultToggleSidebarHotkey = "Alt+B";
    public const bool DefaultStartWithWindows = true;
    public const bool DefaultAutoShowWithCodex = false;

    [JsonPropertyName("version")]
    public int Version { get; set; } = CurrentVersion;

    [JsonPropertyName("providers")]
    public List<ProviderConfig> Providers { get; set; } = [ProviderConfig.DefaultCodex()];

    [JsonPropertyName("clickThroughHud")]
    public bool ClickThroughHud { get; set; }

    [JsonPropertyName("startWithWindows")]
    public bool StartWithWindows { get; set; } = DefaultStartWithWindows;

    [JsonPropertyName("autoShowWithCodex")]
    public bool AutoShowWithCodex { get; set; } = DefaultAutoShowWithCodex;

    [JsonPropertyName("language")]
    public string Language { get; set; } = DefaultLanguage;

    [JsonPropertyName("hotkeys")]
    public HotkeyConfig Hotkeys { get; set; } = new();

    [JsonPropertyName("codexUpdates")]
    public CodexUpdateConfig CodexUpdates { get; set; } = new();

    [JsonPropertyName("appUpdates")]
    public AppUpdateConfig AppUpdates { get; set; } = new();

    [JsonPropertyName("style")]
    public StyleConfig Style { get; set; } = new();

    public static WindexBarConfig Default() => new();

    public WindexBarConfig Normalized()
    {
        Version = CurrentVersion;
        Language = NormalizeLanguage(Language);
        Hotkeys = (Hotkeys ?? new HotkeyConfig()).Normalized();
        CodexUpdates = (CodexUpdates ?? new CodexUpdateConfig()).Normalized();
        AppUpdates = (AppUpdates ?? new AppUpdateConfig()).Normalized();
        Style = (Style ?? new StyleConfig()).Normalized();

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

public sealed class AppUpdateConfig
{
    [JsonPropertyName("automaticallyUpdate")]
    public bool AutomaticallyUpdate { get; set; } = true;

    [JsonPropertyName("latestVersion")]
    public string? LatestVersion { get; set; }

    [JsonPropertyName("lastCheckedAt")]
    public DateTimeOffset? LastCheckedAt { get; set; }

    [JsonPropertyName("pendingVersion")]
    public string? PendingVersion { get; set; }

    [JsonPropertyName("retryAfter")]
    public DateTimeOffset? RetryAfter { get; set; }

    [JsonPropertyName("lastFailure")]
    public string? LastFailure { get; set; }

    public AppUpdateConfig Normalized()
    {
        LatestVersion = NormalizeText(LatestVersion);
        PendingVersion = NormalizeText(PendingVersion);
        LastFailure = NormalizeText(LastFailure);
        return this;
    }

    private static string? NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class StyleConfig
{
    public const string DefaultGaugeThickness = "default";
    public const string DefaultGaugeColor = "#8D78D6";
    public const string DefaultGaugeAnimation = "smooth";

    [JsonPropertyName("gaugeThickness")]
    public string GaugeThickness { get; set; } = DefaultGaugeThickness;

    [JsonPropertyName("gaugeColor")]
    public string GaugeColor { get; set; } = DefaultGaugeColor;

    [JsonPropertyName("gaugeAnimation")]
    public string GaugeAnimation { get; set; } = DefaultGaugeAnimation;

    public StyleConfig Normalized()
    {
        GaugeThickness = NormalizeGaugeThickness(GaugeThickness);
        GaugeColor = NormalizeGaugeColor(GaugeColor);
        GaugeAnimation = NormalizeGaugeAnimation(GaugeAnimation);
        return this;
    }

    public static string NormalizeGaugeThickness(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "thin" => "thin",
            "thick" => "thick",
            _ => DefaultGaugeThickness
        };

    public static string NormalizeGaugeColor(string? value)
    {
        var candidate = value?.Trim();
        if (candidate is { Length: 7 }
            && candidate[0] == '#'
            && uint.TryParse(
                candidate.AsSpan(1),
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out _))
        {
            return candidate.ToUpperInvariant();
        }

        return DefaultGaugeColor;
    }

    public static string NormalizeGaugeAnimation(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "fast" => "fast",
            "off" => "off",
            _ => DefaultGaugeAnimation
        };
}

public sealed class CodexUpdateConfig
{
    public const string DefaultInstallMethod = "auto";

    [JsonPropertyName("installMethod")]
    public string InstallMethod { get; set; } = DefaultInstallMethod;

    [JsonPropertyName("automaticallyUpdate")]
    public bool AutomaticallyUpdate { get; set; } = true;

    [JsonPropertyName("customCommand")]
    public string? CustomCommand { get; set; }

    [JsonPropertyName("latestVersion")]
    public string? LatestVersion { get; set; }

    [JsonPropertyName("lastCheckedAt")]
    public DateTimeOffset? LastCheckedAt { get; set; }

    public CodexUpdateConfig Normalized()
    {
        InstallMethod = CodexInstallMethodNames.Normalize(InstallMethod);
        AutomaticallyUpdate = true;
        CustomCommand = string.IsNullOrWhiteSpace(CustomCommand) ? null : CustomCommand.Trim();
        LatestVersion = string.IsNullOrWhiteSpace(LatestVersion) ? null : LatestVersion.Trim();
        return this;
    }
}

public sealed class HotkeyConfig
{
    [JsonPropertyName("toggleWindow")]
    public string ToggleWindow { get; set; } = WindexBarConfig.DefaultToggleWindowHotkey;

    [JsonPropertyName("toggleSidebar")]
    public string ToggleSidebar { get; set; } = WindexBarConfig.DefaultToggleSidebarHotkey;

    public HotkeyConfig Normalized()
    {
        ToggleWindow = HotkeyShortcut.NormalizeOrDefault(
            ToggleWindow,
            WindexBarConfig.DefaultToggleWindowHotkey);
        ToggleSidebar = HotkeyShortcut.NormalizeOrDefault(
            ToggleSidebar,
            WindexBarConfig.DefaultToggleSidebarHotkey);
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
