using WindexBar.Core.Models;

namespace WindexBar.Core.Providers;

public enum ProviderFetchKind
{
    Cli,
    OAuth,
    Web,
    ApiToken,
    LocalProbe,
    WebDashboard
}

public sealed record ProviderFetchContext(
    UsageProvider Provider,
    IReadOnlyDictionary<string, string> Environment,
    bool IncludeCredits,
    TimeSpan InitializeTimeout,
    TimeSpan RequestTimeout);

public sealed record ProviderFetchResult(
    UsageSnapshot Usage,
    CreditsSnapshot? Credits,
    string SourceLabel,
    string StrategyId,
    ProviderFetchKind StrategyKind);

public sealed record ProviderFetchAttempt(string StrategyId, ProviderFetchKind Kind, bool WasAvailable, string? ErrorDescription);

public sealed record ProviderFetchOutcome(ProviderFetchResult? Result, IReadOnlyList<ProviderFetchAttempt> Attempts, string? ErrorDescription)
{
    public bool IsSuccess => Result is not null;
}

public interface IProviderFetchStrategy
{
    string Id { get; }
    ProviderFetchKind Kind { get; }
    Task<bool> IsAvailableAsync(ProviderFetchContext context, CancellationToken cancellationToken);
    Task<ProviderFetchResult> FetchAsync(ProviderFetchContext context, CancellationToken cancellationToken);
    bool ShouldFallback(Exception error, ProviderFetchContext context);
}

public sealed class ProviderFetchPipeline
{
    private readonly IReadOnlyList<IProviderFetchStrategy> _strategies;

    public ProviderFetchPipeline(IEnumerable<IProviderFetchStrategy> strategies)
    {
        _strategies = strategies.ToArray();
    }

    public async Task<ProviderFetchOutcome> FetchAsync(ProviderFetchContext context, CancellationToken cancellationToken)
    {
        var attempts = new List<ProviderFetchAttempt>();
        Exception? lastAvailableError = null;

        foreach (var strategy in _strategies)
        {
            var available = await strategy.IsAvailableAsync(context, cancellationToken).ConfigureAwait(false);
            if (!available)
            {
                attempts.Add(new ProviderFetchAttempt(strategy.Id, strategy.Kind, false, null));
                continue;
            }

            try
            {
                var result = await strategy.FetchAsync(context, cancellationToken).ConfigureAwait(false);
                attempts.Add(new ProviderFetchAttempt(strategy.Id, strategy.Kind, true, null));
                return new ProviderFetchOutcome(result, attempts, null);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                lastAvailableError = error;
                attempts.Add(new ProviderFetchAttempt(strategy.Id, strategy.Kind, true, error.Message));
                if (strategy.ShouldFallback(error, context))
                {
                    continue;
                }

                return new ProviderFetchOutcome(null, attempts, error.Message);
            }
        }

        return new ProviderFetchOutcome(
            null,
            attempts,
            lastAvailableError?.Message ?? $"No available fetch strategy for {context.Provider}.");
    }
}

public sealed record ProviderDescriptor(
    UsageProvider Id,
    string DisplayName,
    string SessionLabel,
    string WeeklyLabel,
    string CliName,
    bool DefaultEnabled,
    ProviderFetchPipeline FetchPipeline);

