using System.Globalization;
using WindexBar.Core.Formatting;
using WindexBar.Core.Models;

namespace WindexBar.Core.Tests;

public sealed class RateLimitResetCreditFormatterTests
{
    [Fact]
    public void DirectSnapshotCapsDetailsToAuthoritativeAvailableCount()
    {
        var now = DateTimeOffset.Parse("2026-07-10T12:00:00+09:00");
        var earlierExpiry = DateTimeOffset.Parse("2026-08-01T05:05:10+09:00");
        var laterExpiry = DateTimeOffset.Parse("2026-08-03T06:06:00+09:00");
        var snapshot = new RateLimitResetCreditsSnapshot(
            1,
            now,
            [
                new RateLimitResetCredit("later", now.AddDays(-7), laterExpiry, "codexRateLimits", "available", null, null),
                new RateLimitResetCredit("earlier", now.AddDays(-8), earlierExpiry, "codexRateLimits", "available", null, null)
            ]);

        var credit = Assert.Single(snapshot.Credits);
        Assert.Equal("earlier", credit.Id);
        Assert.Equal(earlierExpiry, snapshot.NextExpiresAt);
        var localExpiry = earlierExpiry.ToLocalTime().ToString("yy.M.dd H:mm", CultureInfo.InvariantCulture);
        Assert.Equal($"Expires {localExpiry}: 1", RateLimitResetCreditFormatter.FormatDetail(snapshot, "en", now));
    }

    [Fact]
    public void FormatsExactResetCreditExpirationsAndUnavailableCount()
    {
        var now = DateTimeOffset.Parse("2026-07-10T12:00:00+09:00");
        var earliestExpiry = DateTimeOffset.Parse("2026-08-01T05:05:10+09:00");
        var sameMinuteExpiry = DateTimeOffset.Parse("2026-08-01T05:05:47+09:00");
        var laterExpiry = DateTimeOffset.Parse("2026-08-03T06:06:00+09:00");
        var snapshot = new RateLimitResetCreditsSnapshot(
            5,
            now,
            [
                new RateLimitResetCredit("later", now.AddDays(-8), laterExpiry, "codexRateLimits", "available", null, null),
                new RateLimitResetCredit("a", now.AddDays(-8), earliestExpiry, "codexRateLimits", "available", null, null),
                new RateLimitResetCredit("missing", now.AddDays(-8), null, "codexRateLimits", "available", null, null),
                new RateLimitResetCredit("b", now.AddDays(-8), sameMinuteExpiry, "codexRateLimits", "available", null, null)
            ]);

        Assert.Equal("5\uAC1C \uBCF4\uC720" + Environment.NewLine + "\uCCAB \uB9CC\uB8CC D-22", RateLimitResetCreditFormatter.FormatSummary(snapshot, "ko", now));
        Assert.Equal("5 held" + Environment.NewLine + "First expiry D-22", RateLimitResetCreditFormatter.FormatSummary(snapshot, "en", now));
        var earliestLocalExpiry = earliestExpiry.ToLocalTime().ToString("yy.M.dd H:mm", CultureInfo.InvariantCulture);
        var laterLocalExpiry = laterExpiry.ToLocalTime().ToString("yy.M.dd H:mm", CultureInfo.InvariantCulture);

        Assert.Equal(
            $"{earliestLocalExpiry} \uB9CC\uB8CC: 2\uAC1C" + Environment.NewLine
            + $"{laterLocalExpiry} \uB9CC\uB8CC: 1\uAC1C" + Environment.NewLine
            + "\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500" + Environment.NewLine
            + "\uB9CC\uB8CC \uC815\uBCF4 \uBBF8\uC81C\uACF5: 2\uAC1C",
            RateLimitResetCreditFormatter.FormatDetail(snapshot, "ko", now));
        Assert.Equal(
            $"Expires {earliestLocalExpiry}: 2" + Environment.NewLine
            + $"Expires {laterLocalExpiry}: 1" + Environment.NewLine
            + "\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500" + Environment.NewLine
            + "Expiration unavailable: 2",
            RateLimitResetCreditFormatter.FormatDetail(snapshot, "en", now));
    }

    [Fact]
    public void FormatsUnavailableWhenAllExactRowsOmitExpiration()
    {
        var now = DateTimeOffset.Parse("2026-07-10T12:00:00+09:00");
        var snapshot = new RateLimitResetCreditsSnapshot(
            2,
            now,
            [
                new RateLimitResetCredit("a", now, null, "codexRateLimits", "available", null, null),
                new RateLimitResetCredit("b", now, null, "codexRateLimits", "available", null, null)
            ]);

        Assert.Equal("2 held" + Environment.NewLine + "Expiration unavailable", RateLimitResetCreditFormatter.FormatSummary(snapshot, "en", now));
        Assert.Equal("Expiration unavailable: 2", RateLimitResetCreditFormatter.FormatDetail(snapshot, "en", now));
    }

    [Fact]
    public void FormatsUnavailableWhenAppServerReturnsCountOnly()
    {
        var snapshot = new RateLimitResetCreditsSnapshot(1, DateTimeOffset.UnixEpoch);

        Assert.Equal("1\uAC1C \uBCF4\uC720" + Environment.NewLine + "\uB9CC\uB8CC \uC815\uBCF4 \uBBF8\uC81C\uACF5", RateLimitResetCreditFormatter.FormatSummary(snapshot, "ko", DateTimeOffset.UnixEpoch));
        Assert.Equal("\uB9CC\uB8CC \uC815\uBCF4 \uBBF8\uC81C\uACF5: 1\uAC1C", RateLimitResetCreditFormatter.FormatDetail(snapshot, "ko", DateTimeOffset.UnixEpoch));
    }
}
