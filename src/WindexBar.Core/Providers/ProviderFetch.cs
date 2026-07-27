using WindexBar.Core.Models;

namespace WindexBar.Core.Providers;

public sealed record ProviderFetchContext(
    UsageProvider Provider,
    IReadOnlyDictionary<string, string> Environment,
    bool IncludeCredits,
    TimeSpan InitializeTimeout,
    TimeSpan RequestTimeout);

public sealed record ProviderFetchResult(
    UsageSnapshot Usage,
    CreditsSnapshot? Credits);

public sealed record ProviderFetchOutcome(ProviderFetchResult? Result, string? ErrorDescription);

public interface IProviderFetchStrategy
{
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
        Exception? lastAvailableError = null;

        foreach (var strategy in _strategies)
        {
            var available = await strategy.IsAvailableAsync(context, cancellationToken).ConfigureAwait(false);
            if (!available)
            {
                continue;
            }

            try
            {
                var result = await strategy.FetchAsync(context, cancellationToken).ConfigureAwait(false);
                return new ProviderFetchOutcome(result, null);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                lastAvailableError = error;
                if (strategy.ShouldFallback(error, context))
                {
                    continue;
                }

                return new ProviderFetchOutcome(null, error.Message);
            }
        }

        return new ProviderFetchOutcome(
            null,
            lastAvailableError?.Message ?? $"No available fetch strategy for {context.Provider}.");
    }
}

public sealed record ProviderDescriptor(ProviderFetchPipeline FetchPipeline);

