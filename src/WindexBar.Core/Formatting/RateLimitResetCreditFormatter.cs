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
        var expiryText = resetCredits.NextExpiresAt is { } nextExpiresAt
            ? isKorean
                ? $"\uCCAB \uB9CC\uB8CC {FormatDayCode(nextExpiresAt, referenceTime)}"
                : $"First expiry {FormatDayCode(nextExpiresAt, referenceTime)}"
            : isKorean
                ? "\uB9CC\uB8CC \uC815\uBCF4 \uBBF8\uC81C\uACF5"
                : "Expiration unavailable";

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

        var lines = new List<string>();

        foreach (var group in resetCredits.Credits
            .Where(credit => credit.ExpiresAt is not null)
            .Select(credit => new
            {
                ExpiresAt = credit.ExpiresAt!.Value,
                Label = FormatExpiryBucket(credit.ExpiresAt.Value, language)
            })
            .GroupBy(credit => credit.Label, StringComparer.Ordinal)
            .Select(group => new
            {
                Label = group.Key,
                FirstExpiresAt = group.Min(credit => credit.ExpiresAt),
                Count = group.LongCount()
            })
            .OrderBy(group => group.FirstExpiresAt))
        {
            lines.Add(FormatDetailLine(group.Label, group.Count, language));
        }

        if (resetCredits.UnavailableExpirationCount > 0)
        {
            if (lines.Count > 0)
            {
                lines.Add("\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500");
            }

            var unavailableLabel = isKorean ? "\uB9CC\uB8CC \uC815\uBCF4 \uBBF8\uC81C\uACF5" : "Expiration unavailable";
            lines.Add(FormatDetailLine(unavailableLabel, resetCredits.UnavailableExpirationCount, language));
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

    private static string FormatDayCode(DateTimeOffset target, DateTimeOffset now)
    {
        var days = Math.Max(0, (int)Math.Ceiling((target - now).TotalDays));
        return days == 0 ? "D-Day" : $"D-{days}";
    }

    private static string FormatExpiryBucket(DateTimeOffset target, string? language)
    {
        var local = target.ToLocalTime().ToString("yy.M.dd H:mm", CultureInfo.InvariantCulture);
        return IsKorean(language) ? $"{local} \uB9CC\uB8CC" : $"Expires {local}";
    }

    private static string CountSuffix(string? language) => IsKorean(language) ? "\uAC1C" : string.Empty;

    private static string FormatDetailLine(string label, long count, string? language) =>
        $"{label}: {FormatCount(count)}{CountSuffix(language)}";

    private static string FormatCount(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Unknown() => "?";

    private static bool IsKorean(string? language) => WindexBarConfig.NormalizeLanguage(language) == "ko";
}
