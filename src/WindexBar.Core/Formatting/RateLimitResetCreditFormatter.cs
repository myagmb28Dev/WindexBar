using System.Globalization;
using WindexBar.Core.Config;
using WindexBar.Core.Models;

namespace WindexBar.Core.Formatting;

public static class RateLimitResetCreditFormatter
{
    public static string Format(
        RateLimitResetCreditsSnapshot? resetCredits,
        string? language,
        DateTimeOffset? now = null)
    {
        return FormatSummary(resetCredits, language, now);
    }

    public static string FormatSummary(
        RateLimitResetCreditsSnapshot? resetCredits,
        string? language,
        DateTimeOffset? now = null)
    {
        if (resetCredits is null)
        {
            return Unknown();
        }

        var isKorean = IsKorean(language);
        var availableText = isKorean
            ? $"{FormatCount(resetCredits.AvailableCount)}\uAC1C \uBCF4\uC720"
            : $"{FormatCount(resetCredits.AvailableCount)} held";
        if (resetCredits.AvailableCount <= 0)
        {
            return availableText;
        }

        var referenceTime = now ?? DateTimeOffset.Now;
        var expiryText = resetCredits.NextEstimatedExpiresAt is { } nextEstimatedExpiresAt
            ? isKorean
                ? $"\uCCAB \uB9CC\uB8CC {FormatDayCode(nextEstimatedExpiresAt, referenceTime)}"
                : $"First expiry {FormatDayCode(nextEstimatedExpiresAt, referenceTime)}"
            : isKorean
                ? "\uAE30\uC874 \uD06C\uB808\uB527\uB9CC \uBCF4\uC720"
                : "Legacy credits only";

        return string.Join(Environment.NewLine, availableText, expiryText);
    }

    public static string FormatDetail(
        RateLimitResetCreditsSnapshot? resetCredits,
        string? language,
        DateTimeOffset? now = null)
    {
        var isKorean = IsKorean(language);
        if (resetCredits is null)
        {
            return Unknown();
        }

        var referenceTime = now ?? DateTimeOffset.Now;
        var lines = new List<string>();

        foreach (var group in resetCredits.Credits
            .Where(credit => credit.EstimatedExpiresAt is not null)
            .GroupBy(credit => credit.EstimatedExpiresAt!.Value)
            .OrderBy(group => group.Key)
            .Select(group => new
            {
                Label = FormatExpiryBucket(group.Key, referenceTime, language),
                Count = group.LongCount()
            }))
        {
            lines.Add(FormatDetailLine(group.Label, group.Count, language));
        }

        if (resetCredits.UnknownExpirationCount > 0)
        {
            if (lines.Count > 0)
            {
                lines.Add("\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500");
            }

            var legacyLabel = isKorean ? "\uAE30\uC874 \uCD08\uAE30\uD654\uAD8C" : "Legacy reset credits";
            lines.Add(FormatDetailLine(legacyLabel, resetCredits.UnknownExpirationCount, language));
        }

        return lines.Count == 0
            ? (isKorean ? "\uBCF4\uC720 \uCD08\uAE30\uD654\uAD8C \uC5C6\uC74C" : "No reset credits held")
            : string.Join(Environment.NewLine, lines);
    }

    public static string FormatCompact(
        RateLimitResetCreditsSnapshot resetCredits,
        string? language,
        DateTimeOffset? now = null)
    {
        return Format(resetCredits, language, now);
    }

    public static string FormatRelative(DateTimeOffset target, DateTimeOffset now, string? language)
    {
        var isKorean = IsKorean(language);
        var delta = target - now;
        if (delta.TotalSeconds <= 0)
        {
            return isKorean ? "\uC9C0\uAE08" : "now";
        }

        if (delta.TotalHours >= 24)
        {
            var days = delta.TotalDays >= 10 ? Math.Round(delta.TotalDays) : Math.Round(delta.TotalDays, 1);
            var daysText = days.ToString("0.#", CultureInfo.InvariantCulture);
            return isKorean ? $"{daysText}\uC77C \uD6C4" : $"in {daysText}d";
        }

        if (delta.TotalHours >= 1)
        {
            var hours = Math.Max(1, (int)Math.Round(delta.TotalHours));
            return isKorean ? $"{hours}\uC2DC\uAC04 \uD6C4" : $"in {hours}h";
        }

        var minutes = Math.Max(1, (int)Math.Round(delta.TotalMinutes));
        return isKorean ? $"{minutes}\uBD84 \uD6C4" : $"in {minutes}m";
    }

    private static string FormatRelativeShort(DateTimeOffset target, DateTimeOffset now, string? language)
    {
        var isKorean = IsKorean(language);
        var delta = target - now;
        if (delta.TotalSeconds <= 0)
        {
            return isKorean ? "\uC9C0\uAE08" : "now";
        }

        if (delta.TotalHours >= 24)
        {
            var days = delta.TotalDays >= 10 ? Math.Round(delta.TotalDays) : Math.Round(delta.TotalDays, 1);
            var daysText = days.ToString("0.#", CultureInfo.InvariantCulture);
            return isKorean ? $"{daysText}\uC77C" : $"{daysText}d";
        }

        if (delta.TotalHours >= 1)
        {
            var hours = Math.Max(1, (int)Math.Round(delta.TotalHours));
            return isKorean ? $"{hours}\uC2DC\uAC04" : $"{hours}h";
        }

        var minutes = Math.Max(1, (int)Math.Round(delta.TotalMinutes));
        return isKorean ? $"{minutes}\uBD84" : $"{minutes}m";
    }

    private static string FormatDayCode(DateTimeOffset target, DateTimeOffset now)
    {
        var days = Math.Max(0, (int)Math.Ceiling((target - now).TotalDays));
        return days == 0 ? "D-Day" : $"D-{days}";
    }

    private static string FormatExpiryBucket(DateTimeOffset target, DateTimeOffset now, string? language)
    {
        var isKorean = IsKorean(language);
        var days = Math.Max(0, (int)Math.Ceiling((target - now).TotalDays));
        if (days == 0)
        {
            return isKorean ? "\uC624\uB298 \uB9CC\uB8CC" : "Expires today";
        }

        return isKorean ? $"{days}\uC77C \uD6C4 \uB9CC\uB8CC" : $"Expires in {days}d";
    }

    private static string CountSuffix(string? language) => IsKorean(language) ? "\uAC1C" : string.Empty;

    private static string FormatDetailLine(string label, long count, string? language) =>
        $"{label}: {FormatCount(count)}{CountSuffix(language)}";

    private static string FormatCount(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Unknown() => "?";

    private static bool IsKorean(string? language) => WindexBarConfig.NormalizeLanguage(language) == "ko";
}
