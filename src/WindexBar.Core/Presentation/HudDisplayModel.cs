using WindexBar.Core.Models;

namespace WindexBar.Core.Presentation;

public sealed record GaugeWindowDisplay(string Percent, string Detail, double TargetValue);

public sealed record HudDisplayModel(
    string Header,
    string Meta,
    string Error,
    string Account,
    GaugeWindowDisplay Current,
    GaugeWindowDisplay Weekly,
    bool IsFastServiceTier);

public static class HudDisplayModelFactory
{
    public static HudDisplayModel Create(
        UsageSnapshot? snapshot,
        string? lastError,
        bool providerDisabled,
        string language,
        DateTimeOffset now)
    {
        var activeModel = snapshot?.ActiveModel;
        var currentSessionModel = FindCurrentSessionModel(snapshot?.Models, activeModel);
        var current = currentSessionModel?.Current ?? snapshot?.Primary;
        var weekly = currentSessionModel?.Weekly ?? snapshot?.Secondary;
        var hasRateLimitDisplay = currentSessionModel is not null
            || snapshot?.Primary is not null
            || snapshot?.Secondary is not null;
        var header = hasRateLimitDisplay
            ? FirstNonBlank(activeModel?.DisplayName, activeModel?.Model, "Codex")
            : "Codex";

        return new HudDisplayModel(
            header,
            providerDisabled ? Text(language, "Provider disabled", "제공자 비활성화") : lastError ?? string.Empty,
            lastError ?? string.Empty,
            FormatIdentity(snapshot?.Identity, language),
            FormatWindow(current, language, now),
            FormatWindow(weekly, language, now),
            string.Equals(activeModel?.ServiceTier, "fast", StringComparison.OrdinalIgnoreCase));
    }

    private static GaugeWindowDisplay FormatWindow(RateWindow? window, string language, DateTimeOffset now)
    {
        if (window is null)
        {
            var unknown = Text(language, "unknown", "알 수 없음");
            return new GaugeWindowDisplay(unknown, unknown, 0);
        }

        var reset = window.ResetsAt is null
            ? string.Empty
            : IsKorean(language)
                ? $", 초기화 {FormatResetDescription(window.ResetsAt.Value, language, now)}"
                : $", resets {FormatResetDescription(window.ResetsAt.Value, language, now)}";
        var detail = IsKorean(language)
            ? $"{window.UsedPercent:0.#}% 사용{reset}"
            : $"Used {window.UsedPercent:0.#}%{reset}";
        return new GaugeWindowDisplay($"{window.RemainingPercent:0.#}%", detail, window.RemainingPercent);
    }

    private static string FormatResetDescription(DateTimeOffset resetsAt, string language, DateTimeOffset now)
    {
        var delta = resetsAt - now;
        if (delta.TotalSeconds <= 0)
        {
            return Text(language, "now", "지금");
        }

        if (delta.TotalHours >= 24)
        {
            var days = delta.TotalDays >= 10 ? Math.Round(delta.TotalDays) : Math.Round(delta.TotalDays, 1);
            return IsKorean(language) ? $"{days:0.#}일 후" : $"in {days:0.#}d";
        }

        if (delta.TotalHours >= 1)
        {
            var hours = Math.Max(1, (int)Math.Round(delta.TotalHours));
            return IsKorean(language) ? $"{hours}시간 후" : $"in {hours}h";
        }

        var minutes = Math.Max(1, (int)Math.Round(delta.TotalMinutes));
        return IsKorean(language) ? $"{minutes}분 후" : $"in {minutes}m";
    }

    private static string FormatIdentity(ProviderIdentitySnapshot? identity, string language)
    {
        var unknown = Text(language, "unknown", "알 수 없음");
        if (identity is null || (string.IsNullOrWhiteSpace(identity.AccountEmail) && string.IsNullOrWhiteSpace(identity.LoginMethod)))
        {
            return unknown;
        }

        if (string.IsNullOrWhiteSpace(identity.AccountEmail))
        {
            return identity.LoginMethod ?? unknown;
        }

        return string.IsNullOrWhiteSpace(identity.LoginMethod)
            ? identity.AccountEmail
            : $"{identity.AccountEmail} ({identity.LoginMethod})";
    }

    private static ModelUsageSnapshot? FindCurrentSessionModel(
        IReadOnlyList<ModelUsageSnapshot>? models,
        CodexModelSelection? activeModel)
    {
        var modelsWithLimits = models?.Where(model => model.HasRateLimitWindows).ToArray() ?? [];
        if (modelsWithLimits.Length == 0)
        {
            return null;
        }

        if (activeModel is not null)
        {
            var matching = modelsWithLimits.FirstOrDefault(model =>
                IsSameModelName(model.ModelName, activeModel.Model)
                || IsSameModelName(model.ModelName, activeModel.DisplayName));
            if (matching is not null)
            {
                return matching;
            }
        }

        return modelsWithLimits.FirstOrDefault(model => IsGenericCodexModel(model.ModelName))
            ?? modelsWithLimits[0];
    }

    private static bool IsSameModelName(string lhs, string rhs) =>
        string.Equals(NormalizeModelKey(lhs), NormalizeModelKey(rhs), StringComparison.OrdinalIgnoreCase);

    private static bool IsGenericCodexModel(string modelName) =>
        string.Equals(modelName.Trim(), "Codex", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeModelKey(string value)
    {
        var normalized = value.Replace('_', ' ').Replace('-', ' ').Trim();
        var suffixes = new[]
        {
            " ultra reasoning effort", " max reasoning effort", " ultra reasoning", " max reasoning",
            " extra high reasoning effort", " extra high reasoning", " xhigh reasoning effort", " xhigh reasoning",
            " high reasoning effort", " high reasoning", " medium reasoning effort", " medium reasoning",
            " low reasoning effort", " low reasoning", " minimal reasoning effort", " minimal reasoning",
            " no reasoning", " none reasoning", " extra high", " ultra", " max", " xhigh", " high",
            " medium", " low", " minimal", " none", " reasoning effort", " reasoning"
        };

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var suffix in suffixes)
            {
                if (!normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                normalized = normalized[..^suffix.Length].Trim();
                changed = true;
                break;
            }
        }

        return new string(normalized.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static bool IsKorean(string language) =>
        string.Equals(language, "ko", StringComparison.OrdinalIgnoreCase);

    private static string Text(string language, string english, string korean) =>
        IsKorean(language) ? korean : english;
}
