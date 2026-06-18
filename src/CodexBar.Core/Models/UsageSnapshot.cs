namespace CodexBar.Core.Models;

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
    ProviderIdentitySnapshot? Identity)
{
    public bool HasRateLimitWindows => Primary is not null || Secondary is not null || Tertiary is not null;
}

public sealed record CreditEvent(Guid Id, DateTimeOffset Date, string Service, double CreditsUsed);

public sealed record CreditsSnapshot(double Remaining, IReadOnlyList<CreditEvent> Events, DateTimeOffset UpdatedAt)
{
    public static CreditsSnapshot Empty(DateTimeOffset updatedAt) => new(0, Array.Empty<CreditEvent>(), updatedAt);
}
