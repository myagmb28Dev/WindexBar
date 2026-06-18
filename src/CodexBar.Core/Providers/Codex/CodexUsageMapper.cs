using System.Globalization;
using System.Text.Json;
using CodexBar.Core.Models;

namespace CodexBar.Core.Providers.Codex;

public static class CodexUsageMapper
{
    private static readonly string[] ModelContainerKeys = ["models", "modelLimits", "modelRateLimits", "rateLimitsByModel"];
    private static readonly string[] ReasoningSuffixes =
    [
        " extra high reasoning effort",
        " extra high reasoning",
        " xhigh reasoning effort",
        " xhigh reasoning",
        " high reasoning effort",
        " high reasoning",
        " medium reasoning effort",
        " medium reasoning",
        " low reasoning effort",
        " low reasoning",
        " minimal reasoning effort",
        " minimal reasoning",
        " no reasoning",
        " none reasoning",
        " extra high",
        " xhigh",
        " high",
        " medium",
        " low",
        " minimal",
        " none",
        " reasoning effort",
        " reasoning"
    ];

    public static UsageSnapshot? MapUsage(RpcRateLimitsResponse response, RpcAccountResponse? account, DateTimeOffset now)
    {
        var usage = MapUsage(response.RateLimits, account, now);
        var bucketModels = MapRateLimitBucketModels(response.RateLimitsByLimitId);
        if (bucketModels.Count == 0)
        {
            return usage;
        }

        var models = MergeModelUsages(usage?.Models, bucketModels);
        var mappedWindows = models
            .SelectMany(model => new[] { model.Current, model.Weekly })
            .Where(window => window is not null)
            .Cast<RateWindow>()
            .ToArray();

        var primary = usage?.Primary ?? mappedWindows.FirstOrDefault();
        var secondary = usage?.Secondary ?? models.Select(model => model.Weekly).FirstOrDefault(window => window is not null);
        var tertiary = usage?.Tertiary ?? mappedWindows.FirstOrDefault(window => !Equals(window, primary) && !Equals(window, secondary));

        return usage is null
            ? new UsageSnapshot(primary, secondary, tertiary, now, MapIdentity(response.RateLimits, account), models)
            : usage with { Primary = primary, Secondary = secondary, Tertiary = tertiary, Models = models };
    }

    internal static UsageSnapshot WithMergedModels(UsageSnapshot usage, IReadOnlyList<ModelUsageSnapshot> additionalModels)
    {
        if (additionalModels.Count == 0)
        {
            return usage;
        }

        var models = MergeModelUsages(usage.Models, additionalModels);
        var mappedWindows = models
            .SelectMany(model => new[] { model.Current, model.Weekly })
            .Where(window => window is not null)
            .Cast<RateWindow>()
            .ToArray();

        var primary = usage.Primary ?? mappedWindows.FirstOrDefault();
        var secondary = usage.Secondary ?? models.Select(model => model.Weekly).FirstOrDefault(window => window is not null);
        var tertiary = usage.Tertiary ?? mappedWindows.FirstOrDefault(window => !Equals(window, primary) && !Equals(window, secondary));
        return usage with { Primary = primary, Secondary = secondary, Tertiary = tertiary, Models = models };
    }

    public static UsageSnapshot? MapUsage(RpcRateLimitSnapshot limits, RpcAccountResponse? account, DateTimeOffset now)
    {
        var identity = MapIdentity(limits, account);

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

    private static ProviderIdentitySnapshot MapIdentity(RpcRateLimitSnapshot limits, RpcAccountResponse? account)
    {
        var accountDetails = account?.Account;
        return new ProviderIdentitySnapshot(
            UsageProvider.Codex,
            IsAccountType(accountDetails?.Type, "chatgpt") ? NormalizeField(accountDetails?.Email) : null,
            null,
            NormalizeField(accountDetails?.PlanType) ?? NormalizeField(limits.PlanType));
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

    private static IReadOnlyList<ModelUsageSnapshot> MapRateLimitBucketModels(IReadOnlyDictionary<string, RpcRateLimitSnapshot>? buckets)
    {
        if (buckets is null || buckets.Count == 0)
        {
            return [];
        }

        var models = new List<ModelUsageSnapshot>();
        foreach (var bucket in buckets)
        {
            var displayName = ResolveLimitModelName(bucket.Key, bucket.Value);
            var current = MapWindow(bucket.Value.Primary);
            var weekly = MapWindow(bucket.Value.Secondary);
            foreach (var model in MapModelUsages(bucket.Value, current, weekly))
            {
                var modelName = string.Equals(model.ModelName, "Codex", StringComparison.OrdinalIgnoreCase)
                    ? displayName
                    : model.ModelName;
                AddOrMergeModel(models, new ModelUsageSnapshot(modelName, model.Current, model.Weekly));
            }
        }

        return models;
    }

    private static IReadOnlyList<ModelUsageSnapshot> MergeModelUsages(
        IReadOnlyList<ModelUsageSnapshot>? existingModels,
        IReadOnlyList<ModelUsageSnapshot> additionalModels)
    {
        var merged = new List<ModelUsageSnapshot>();
        if (existingModels is not null)
        {
            foreach (var model in existingModels)
            {
                AddOrMergeModel(merged, model);
            }
        }

        foreach (var model in additionalModels)
        {
            AddOrMergeModel(merged, model);
        }

        return merged;
    }

    private static void AddOrMergeModel(List<ModelUsageSnapshot> models, ModelUsageSnapshot candidate)
    {
        if (!candidate.HasRateLimitWindows)
        {
            return;
        }

        var existingIndex = models.FindIndex(model => IsSameModelName(model.ModelName, candidate.ModelName));
        if (existingIndex < 0)
        {
            models.Add(candidate);
            return;
        }

        var existing = models[existingIndex];
        models[existingIndex] = new ModelUsageSnapshot(
            existing.ModelName,
            existing.Current ?? candidate.Current,
            existing.Weekly ?? candidate.Weekly);
    }

    private static string ResolveLimitModelName(string rawKey, RpcRateLimitSnapshot bucket)
    {
        var rawName = FirstNonBlank(bucket.LimitName, bucket.LimitId, rawKey, "Codex");
        return FormatModelName(StripLimitSuffix(rawName));
    }

    private static bool IsSameModelName(string lhs, string rhs) =>
        string.Equals(NormalizeModelKey(lhs), NormalizeModelKey(rhs), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeModelKey(string value)
    {
        var chars = StripReasoningSuffix(value)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray();
        return new string(chars);
    }

    private static string StripLimitSuffix(string rawName)
    {
        var trimmed = rawName.Trim().TrimEnd(':').Trim();
        foreach (var suffix in new[] { " rate limits", " rate limit", " limits", " limit" })
        {
            if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[..^suffix.Length].Trim();
            }
        }

        return trimmed;
    }

    private static string StripReasoningSuffix(string rawName)
    {
        var trimmed = rawName.Replace('_', ' ').Replace('-', ' ').Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return trimmed;
        }

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var suffix in ReasoningSuffixes)
            {
                if (!trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                trimmed = trimmed[..^suffix.Length].Trim();
                changed = true;
                break;
            }
        }

        return trimmed;
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

        if (source.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var element in source.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    index++;
                    continue;
                }

                AddModelUsage(builders, ResolveArrayModelName(element, index), element);
                index++;
            }

            return;
        }

        if (!source.ValueKind.Equals(JsonValueKind.Object))
        {
            return;
        }

        var sourceObject = source.EnumerateObject().ToArray();
        if (sourceObject.Length == 0)
        {
            return;
        }

        if (IsLikelyModelContainer(rawKey, sourceObject))
        {
            foreach (var property in sourceObject)
            {
                AddModelUsage(builders, property.Name, property.Value);
            }

            return;
        }

        var model = GetOrAddModel(builders, FormatModelName(rawKey));
        foreach (var property in sourceObject)
        {
            if (TryMapWindow(property.Value, out var window))
            {
                ApplyWindow(model, ResolveWindowKind(property.Name, window), window);
                continue;
            }

            if (!IsWindowContainerKey(property.Name) || !property.Value.ValueKind.Equals(JsonValueKind.Object))
            {
                continue;
            }

            foreach (var nestedProperty in property.Value.EnumerateObject())
            {
                if (TryMapWindow(nestedProperty.Value, out var nestedWindow))
                {
                    ApplyWindow(model, ResolveWindowKind(nestedProperty.Name, nestedWindow), nestedWindow);
                }
            }
        }
    }

    private static string ResolveArrayModelName(JsonElement element, int index)
    {
        if (TryGetString(element, "model", out var model) && !string.IsNullOrWhiteSpace(model))
        {
            return model;
        }

        if (TryGetString(element, "name", out var name) && !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        if (TryGetString(element, "id", out var id) && !string.IsNullOrWhiteSpace(id))
        {
            return id;
        }

        return $"Model {index + 1}";
    }

    private static bool IsLikelyModelContainer(string rawKey, JsonProperty[] properties)
    {
        if (!IsModelContainerKey(rawKey) && IsWindowContainerKey(rawKey))
        {
            return false;
        }

        if (properties.Length <= 1)
        {
            return IsModelContainerKey(rawKey);
        }

        return IsModelContainerKey(rawKey)
            || (
                rawKey.Length > 0
                && !ContainsWindowName(rawKey, "current")
                && !ContainsWindowName(rawKey, "weekly")
                && !ContainsWindowName(rawKey, "primary")
                && !ContainsWindowName(rawKey, "secondary")
                && properties.All(property => property.Value.ValueKind == JsonValueKind.Object)
                && properties.All(property =>
                    !TryMapWindow(property.Value, out _)
                    && !ContainsWindowName(property.Name, "primary")
                    && !ContainsWindowName(property.Name, "secondary")
                    && !ContainsWindowName(property.Name, "weekly")
                    && !ContainsWindowName(property.Name, "current"))
            );
    }

    private static bool IsModelContainerKey(string key) =>
        ModelContainerKeys.Any(candidate => string.Equals(key, candidate, StringComparison.OrdinalIgnoreCase))
        || (key.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0
            && key.IndexOf("limit", StringComparison.OrdinalIgnoreCase) >= 0);

    private static bool IsWindowContainerKey(string key) =>
        string.Equals(key, "limits", StringComparison.OrdinalIgnoreCase)
        || string.Equals(key, "windows", StringComparison.OrdinalIgnoreCase)
        || string.Equals(key, "rateLimits", StringComparison.OrdinalIgnoreCase)
        || ContainsWindowName(key, "limits")
        || ContainsWindowName(key, "windows")
        || ContainsWindowName(key, "rate");

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

    internal static string FormatModelName(string rawName)
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

        NormalizeReasoningDisplayWords(words);
        return string.Join(" ", words.Select(FormatModelWord));
    }

    private static void NormalizeReasoningDisplayWords(List<string> words)
    {
        while (words.Count > 0
            && (
                string.Equals(words[^1], "reasoning", StringComparison.OrdinalIgnoreCase)
                || string.Equals(words[^1], "effort", StringComparison.OrdinalIgnoreCase)))
        {
            words.RemoveAt(words.Count - 1);
        }

        if (words.Count >= 2
            && string.Equals(words[^2], "extra", StringComparison.OrdinalIgnoreCase)
            && string.Equals(words[^1], "high", StringComparison.OrdinalIgnoreCase))
        {
            words.RemoveAt(words.Count - 1);
            words[^1] = "xhigh";
        }
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

        if (string.Equals(word, "xhigh", StringComparison.OrdinalIgnoreCase))
        {
            return "XHigh";
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

    private static bool TryGetString(JsonElement node, string propertyName, out string? value)
    {
        value = null;
        if (!TryGetProperty(node, propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return !string.IsNullOrWhiteSpace(value);
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

    private static string FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
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
