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

        var extraWindows = MapExtraWindows(limits).GetEnumerator();
        var primary = MapWindow(limits.Primary);
        if (primary is null && extraWindows.MoveNext())
        {
            primary = extraWindows.Current;
        }

        var secondary = MapWindow(limits.Secondary);
        if (secondary is null && extraWindows.MoveNext())
        {
            secondary = extraWindows.Current;
        }

        var tertiary = extraWindows.MoveNext() ? extraWindows.Current : null;

        if (primary is null && secondary is null && tertiary is null && identity.AccountEmail is null && identity.LoginMethod is null)
        {
            return null;
        }

        return new UsageSnapshot(primary, secondary, tertiary, now, identity);
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
}
