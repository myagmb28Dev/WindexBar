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
    TokenUsageSnapshot? TokenUsage = null)
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

public sealed record CodexModelSelection(string Model, string? ReasoningEffort, string DisplayName, DateTimeOffset? UpdatedAt);

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

public sealed record CreditEvent(Guid Id, DateTimeOffset Date, string Service, double CreditsUsed);

public sealed record CreditsSnapshot(double Remaining, IReadOnlyList<CreditEvent> Events, DateTimeOffset UpdatedAt)
{
    public static CreditsSnapshot Empty(DateTimeOffset updatedAt) => new(0, Array.Empty<CreditEvent>(), updatedAt);
}
