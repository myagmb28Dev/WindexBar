namespace WindexBar.Core.Models;

public sealed record ProviderIdentitySnapshot(
    UsageProvider ProviderId,
    string? AccountEmail,
    string? AccountOrganization,
    string? LoginMethod);

public sealed record UsageSnapshot(
    RateWindow? Primary,
    RateWindow? Secondary,
    RateWindow? Tertiary,
    DateTimeOffset UpdatedAt,
    ProviderIdentitySnapshot? Identity,
    IReadOnlyList<ModelUsageSnapshot>? Models = null,
    CodexModelSelection? ActiveModel = null,
    TokenUsageSnapshot? TokenUsage = null,
    RateLimitResetCreditsSnapshot? RateLimitResetCredits = null,
    IReadOnlyList<CodexSessionUsageSnapshot>? Sessions = null)
{
    public bool HasRateLimitWindows =>
        Primary is not null
        || Secondary is not null
        || Tertiary is not null
        || (Models?.Any(model => model.HasRateLimitWindows) ?? false);
}

public sealed record ModelUsageSnapshot(string ModelName, RateWindow? Current, RateWindow? Weekly)
{
    public bool HasRateLimitWindows => Current is not null || Weekly is not null;
}

public sealed record CodexModelSelection(string Model, string? ReasoningEffort, string? ServiceTier, string DisplayName, DateTimeOffset? UpdatedAt);

public sealed record TokenUsageSnapshot(
    TokenUsageBreakdown? Total,
    TokenUsageBreakdown? Last,
    int? ModelContextWindow,
    DateTimeOffset? UpdatedAt)
{
    public bool HasUsage => Total is not null || Last is not null;
}

public sealed record TokenUsageBreakdown(
    long InputTokens,
    long CachedInputTokens,
    long OutputTokens,
    long ReasoningOutputTokens,
    long TotalTokens);

public sealed record CodexSessionUsageSnapshot(
    string SessionId,
    string? SessionName,
    string? ProjectPath,
    TokenUsageSnapshot TokenUsage,
    DateTimeOffset UpdatedAt);

public sealed record CreditEvent(Guid Id, DateTimeOffset Date, string Service, double CreditsUsed);

public sealed record CreditsSnapshot(double Remaining, IReadOnlyList<CreditEvent> Events, DateTimeOffset UpdatedAt)
{
    public static CreditsSnapshot Empty(DateTimeOffset updatedAt) => new(0, Array.Empty<CreditEvent>(), updatedAt);
}

public sealed record RateLimitResetCredit(
    string Id,
    DateTimeOffset GrantedAt,
    DateTimeOffset? ExpiresAt,
    string ResetType,
    string Status,
    string? Title,
    string? Description);

public sealed record RateLimitResetCreditsSnapshot(
    long AvailableCount,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<RateLimitResetCredit>? Credits = null)
{
    public IReadOnlyList<RateLimitResetCredit> Credits { get; init; } =
        NormalizeCredits(AvailableCount, Credits);

    public long UnavailableExpirationCount =>
        Math.Max(0, AvailableCount - Credits.LongCount(credit => credit.ExpiresAt is not null));

    public DateTimeOffset? NextExpiresAt => Credits
        .Select(credit => credit.ExpiresAt)
        .Where(expiresAt => expiresAt is not null)
        .Min();

    private static IReadOnlyList<RateLimitResetCredit> NormalizeCredits(
        long availableCount,
        IReadOnlyList<RateLimitResetCredit>? credits)
    {
        var safeCount = availableCount <= 0
            ? 0
            : availableCount > int.MaxValue
                ? int.MaxValue
                : (int)availableCount;
        return (credits ?? Array.Empty<RateLimitResetCredit>())
            .OrderBy(credit => credit.ExpiresAt is null)
            .ThenBy(credit => credit.ExpiresAt)
            .ThenBy(credit => credit.GrantedAt)
            .ThenBy(credit => credit.Id, StringComparer.Ordinal)
            .Take(safeCount)
            .ToArray();
    }
}
