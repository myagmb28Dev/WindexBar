using WindexBar.Core.Models;
using WindexBar.Core.Providers;

namespace WindexBar.Core.Providers.Codex;

public sealed class CodexCliFetchStrategy : IProviderFetchStrategy
{
    private static readonly string[] BaseArguments = ["-s", "read-only", "-a", "untrusted", "app-server"];
    private readonly ICodexRpcTransportFactory _transportFactory;

    public CodexCliFetchStrategy(ICodexRpcTransportFactory? transportFactory = null)
    {
        _transportFactory = transportFactory ?? new ProcessCodexRpcTransportFactory();
    }

    public string Id => "codex.cli";
    public ProviderFetchKind Kind => ProviderFetchKind.Cli;

    public Task<bool> IsAvailableAsync(ProviderFetchContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(CommandLocator.ResolveExecutable(null, context.Environment) is not null);
    }

    public async Task<ProviderFetchResult> FetchAsync(ProviderFetchContext context, CancellationToken cancellationToken)
    {
        var executable = CommandLocator.ResolveExecutable(null, context.Environment)
            ?? throw new FileNotFoundException("Codex executable was not found on PATH. Install Codex or add it to PATH.");

        var sessionState = CodexSessionStateReader.ReadLatestState(context.Environment);
        var activeModel = sessionState?.ActiveModel;
        var tokenUsage = sessionState?.TokenUsage;
        var now = DateTimeOffset.Now;

        await using var transport = _transportFactory.Start(executable, BaseArguments, context.Environment);
        await using var client = new CodexRpcClient(transport, context.InitializeTimeout, context.RequestTimeout);
        await client.InitializeAsync(cancellationToken).ConfigureAwait(false);

        var limits = await client.FetchRateLimitsAsync(cancellationToken).ConfigureAwait(false);
        RpcAccountResponse? account = null;
        try
        {
            account = await client.FetchAccountAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
        }

        var usage = CodexUsageMapper.MapUsage(limits, account, now);
        var credits = context.IncludeCredits ? CodexUsageMapper.MapCredits(limits.RateLimits.Credits, now) : null;
        if (usage is null && credits is null && activeModel is null && tokenUsage is null)
        {
            throw new InvalidOperationException("Codex returned no rate limits or credits.");
        }

        usage ??= new UsageSnapshot(null, null, null, now, null);
        usage = usage with { ActiveModel = activeModel, TokenUsage = tokenUsage };
        return new ProviderFetchResult(usage, credits, "codex-cli", Id, Kind);
    }

    public bool ShouldFallback(Exception error, ProviderFetchContext context) => false;
}

