using System.Globalization;
using System.Text.Json;
using CodexBar.Core.Models;

namespace CodexBar.Core.Providers.Codex;

public static class CodexUsageMapper
{
    public static UsageSnapshot? MapUsage(RpcRateLimitSnapshot limits, RpcAccountResponse? account, DateTimeOffset now)
    {
        var accountDetails = account?.Account;
        var identity = new ProviderIdentitySnapshot(
            UsageProvider.Codex,
            IsAccountType(accountDetails?.Type, "chatgpt") ? NormalizeField(accountDetails?.Email) : null,
            null,
            NormalizeField(accountDetails?.PlanType) ?? NormalizeField(limits.PlanType));

        var explicitPrimary = MapWindow(limits.Primary);
        var explicitSecondary = MapWindow(limits.Secondary);
        var models = MapModelUsages(limits, explicitPrimary, explicitSecondary);
        var mappedWindows = models
            .SelectMany(model => new[] { model.Current, model.Weekly })
            .Where(window => window is not null)
            .Cast<RateWindow>()
            .ToArray();

        var primary = explicitPrimary ?? mappedWindows.FirstOrDefault();
        var secondary = explicitSecondary ?? models.Select(model => model.Weekly).FirstOrDefault(window => window is not null);
        var tertiary = mappedWindows.FirstOrDefault(window => !Equals(window, primary) && !Equals(window, secondary));

        if (primary is null
            && secondary is null
            && tertiary is null
            && models.Count == 0
            && identity.AccountEmail is null
            && identity.LoginMethod is null)
        {
            return null;
        }

        return new UsageSnapshot(primary, secondary, tertiary, now, identity, models);
    }

    public static CreditsSnapshot? MapCredits(RpcCreditsSnapshot? credits, DateTimeOffset now)
    {
        if (credits is null)
        {
            return null;
        }

        var remaining = double.TryParse(credits.Balance, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
        return new CreditsSnapshot(remaining, Array.Empty<CreditEvent>(), now);
    }

    public static RateWindow? MapWindow(RpcRateLimitWindow? window)
    {
        if (window is null)
        {
            return null;
        }

        var resetsAt = window.ResetsAt.HasValue
            ? DateTimeOffset.FromUnixTimeSeconds(window.ResetsAt.Value)
            : (DateTimeOffset?)null;
        var resetDescription = resetsAt.HasValue ? ResetDescription(resetsAt.Value) : null;
        return new RateWindow(window.UsedPercent, window.WindowDurationMins, resetsAt, resetDescription);
    }

    public static string ResetDescription(DateTimeOffset resetsAt)
    {
        var delta = resetsAt - DateTimeOffset.Now;
        if (delta.TotalSeconds <= 0)
        {
            return "now";
        }

        if (delta.TotalHours >= 24)
        {
            return $"in {(int)Math.Round(delta.TotalDays)}d";
        }

        if (delta.TotalHours >= 1)
        {
            return $"in {(int)Math.Round(delta.TotalHours)}h";
        }

        return $"in {Math.Max(1, (int)Math.Round(delta.TotalMinutes))}m";
    }

    private static IReadOnlyList<ModelUsageSnapshot> MapModelUsages(
        RpcRateLimitSnapshot limits,
        RateWindow? defaultCurrent,
        RateWindow? defaultWeekly)
    {
        var builders = new List<ModelUsageBuilder>();
        if (defaultCurrent is not null || defaultWeekly is not null)
        {
            var defaultModel = GetOrAddModel(builders, "Codex");
            defaultModel.Current = defaultCurrent;
            defaultModel.Weekly = defaultWeekly;
        }

        foreach (var extraWindow in limits.UnknownWindows())
        {
            AddModelUsage(builders, extraWindow.Key, extraWindow.Value);
        }

        return builders
            .Where(builder => builder.Current is not null || builder.Weekly is not null)
            .Select(builder => new ModelUsageSnapshot(builder.DisplayName, builder.Current, builder.Weekly))
            .ToArray();
    }

    private static void AddModelUsage(List<ModelUsageBuilder> builders, string rawKey, JsonElement source)
    {
        if (TryMapWindow(source, out var directWindow))
        {
            var kind = ResolveWindowKind(rawKey, directWindow);
            var modelName = FormatModelName(StripWindowSuffix(rawKey));
            ApplyWindow(GetOrAddModel(builders, modelName), kind, directWindow);
            return;
        }

        if (!source.ValueKind.Equals(JsonValueKind.Object))
        {
            return;
        }

        var model = GetOrAddModel(builders, FormatModelName(rawKey));
        foreach (var property in source.EnumerateObject())
        {
            if (!TryMapWindow(property.Value, out var window))
            {
                continue;
            }

            ApplyWindow(model, ResolveWindowKind(property.Name, window), window);
        }
    }

    private static ModelUsageBuilder GetOrAddModel(List<ModelUsageBuilder> builders, string displayName)
    {
        var existing = builders.FirstOrDefault(builder => string.Equals(builder.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var created = new ModelUsageBuilder(displayName);
        builders.Add(created);
        return created;
    }

    private static void ApplyWindow(ModelUsageBuilder model, LimitWindowKind kind, RateWindow window)
    {
        if (kind == LimitWindowKind.Weekly)
        {
            model.Weekly = window;
            return;
        }

        model.Current = window;
    }

    private static LimitWindowKind ResolveWindowKind(string key, RateWindow window)
    {
        if (ContainsWindowName(key, "weekly")
            || ContainsWindowName(key, "secondary")
            || IsWeeklyWindow(window))
        {
            return LimitWindowKind.Weekly;
        }

        return LimitWindowKind.Current;
    }

    private static bool IsWeeklyWindow(RateWindow window) =>
        window.WindowMinutes is >= 7 * 24 * 60 and < 8 * 24 * 60;

    private static bool ContainsWindowName(string key, string windowName)
    {
        var parts = key.Split(['-', '_', '.', '/', ':'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Any(part => string.Equals(part, windowName, StringComparison.OrdinalIgnoreCase));
    }

    private static string StripWindowSuffix(string key)
    {
        var trimmed = key.Trim();
        foreach (var suffix in new[] { "primary", "current", "session", "secondary", "weekly" })
        {
            foreach (var separator in new[] { "-", "_", ".", "/", ":" })
            {
                var candidate = separator + suffix;
                if (trimmed.EndsWith(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed[..^candidate.Length];
                }
            }
        }

        return trimmed;
    }

    private static string FormatModelName(string rawName)
    {
        var normalized = rawName.Replace('_', ' ').Replace('-', ' ').Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "Codex";
        }

        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (words.Count >= 2
            && string.Equals(words[0], "gpt", StringComparison.OrdinalIgnoreCase)
            && char.IsDigit(words[1][0]))
        {
            words[0] = $"GPT-{words[1]}";
            words.RemoveAt(1);
        }

        return string.Join(" ", words.Select(FormatModelWord));
    }

    private static string FormatModelWord(string word)
    {
        if (word.StartsWith("GPT-", StringComparison.OrdinalIgnoreCase))
        {
            return "GPT-" + word[4..];
        }

        if (string.Equals(word, "gpt", StringComparison.OrdinalIgnoreCase))
        {
            return "GPT";
        }

        if (string.Equals(word, "codex", StringComparison.OrdinalIgnoreCase))
        {
            return "Codex";
        }

        if (string.Equals(word, "spark", StringComparison.OrdinalIgnoreCase))
        {
            return "Spark";
        }

        if (word.All(character => !char.IsLetter(character)))
        {
            return word;
        }

        return char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
    }

    private static IEnumerable<RateWindow> MapExtraWindows(RpcRateLimitSnapshot limits)
    {
        foreach (var extraWindow in limits.UnknownWindows())
        {
            if (TryMapWindow(extraWindow.Value, out var window))
            {
                yield return window;
            }
        }
    }

    private static bool TryMapWindow(JsonElement source, out RateWindow window)
    {
        if (!source.ValueKind.Equals(JsonValueKind.Object))
        {
            window = null!;
            return false;
        }

        if (!TryGetDouble(source, "usedPercent", out var usedPercent))
        {
            window = null!;
            return false;
        }

        int? windowMinutes = null;
        if (TryGetInt(source, "windowDurationMinutes", out var minutesByAlt))
        {
            windowMinutes = minutesByAlt;
        }
        else
        {
            TryGetInt(source, "windowDurationMins", out windowMinutes);
        }

        long? resetsAtUnix = null;
        TryGetLong(source, "resetsAt", out resetsAtUnix);

        DateTimeOffset? resetsAt = resetsAtUnix is null ? null : DateTimeOffset.FromUnixTimeSeconds(resetsAtUnix.Value);
        var resetDescription = resetsAt.HasValue ? ResetDescription(resetsAt.Value) : null;
        window = new RateWindow(usedPercent, windowMinutes, resetsAt, resetDescription);
        return true;
    }

    private static bool TryGetDouble(JsonElement node, string propertyName, out double value)
    {
        value = 0;
        return TryGetProperty(node, propertyName, out var property) && TryReadDouble(property, out value);
    }

    private static bool TryGetInt(JsonElement node, string propertyName, out int? value)
    {
        value = null;
        if (!TryGetProperty(node, propertyName, out var property))
        {
            return false;
        }

        if (!TryReadInt64(property, out var parsed))
        {
            return false;
        }

        value = (int)parsed;
        return true;
    }

    private static bool TryGetLong(JsonElement node, string propertyName, out long? value)
    {
        value = null;
        if (!TryGetProperty(node, propertyName, out var property))
        {
            return false;
        }

        if (!TryReadInt64(property, out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryGetProperty(JsonElement node, string propertyName, out JsonElement value)
    {
        foreach (var candidate in new[] { propertyName, $"{propertyName}Mins", $"{propertyName}Minutes" })
        {
            if (node.TryGetProperty(candidate, out value) && value.ValueKind != JsonValueKind.Null && value.ValueKind != JsonValueKind.Undefined)
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryReadDouble(JsonElement value, out double result)
    {
        if (value.ValueKind is JsonValueKind.Number)
        {
            return value.TryGetDouble(out result);
        }

        if (value.ValueKind is JsonValueKind.String)
        {
            return double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        result = 0;
        return false;
    }

    private static bool TryReadInt64(JsonElement value, out long result)
    {
        if (value.ValueKind is JsonValueKind.Number)
        {
            return value.TryGetInt64(out result);
        }

        if (value.ValueKind is JsonValueKind.String)
        {
            return long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }

        result = 0;
        return false;
    }

    private static string? NormalizeField(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static bool IsAccountType(string? lhs, string rhs) => string.Equals(lhs?.Trim(), rhs, StringComparison.OrdinalIgnoreCase);

    private enum LimitWindowKind
    {
        Current,
        Weekly
    }

    private sealed class ModelUsageBuilder(string displayName)
    {
        public string DisplayName { get; } = displayName;
        public RateWindow? Current { get; set; }
        public RateWindow? Weekly { get; set; }
    }
}
