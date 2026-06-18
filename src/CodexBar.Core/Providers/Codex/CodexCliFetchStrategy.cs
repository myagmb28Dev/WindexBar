using CodexBar.Core.Models;
using CodexBar.Core.Providers;

namespace CodexBar.Core.Providers.Codex;

public sealed class CodexCliFetchStrategy : IProviderFetchStrategy
{
    private static readonly string[] Arguments = ["-s", "read-only", "-a", "untrusted", "app-server"];
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

        await using var transport = _transportFactory.Start(executable, Arguments, context.Environment);
        await using var client = new CodexRpcClient(transport, context.InitializeTimeout, context.RequestTimeout);
        await client.InitializeAsync(cancellationToken).ConfigureAwait(false);

        var now = DateTimeOffset.Now;
        var limits = (await client.FetchRateLimitsAsync(cancellationToken).ConfigureAwait(false)).RateLimits;
        RpcAccountResponse? account = null;
        try
        {
            account = await client.FetchAccountAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
        }

        var usage = CodexUsageMapper.MapUsage(limits, account, now);
        var credits = context.IncludeCredits ? CodexUsageMapper.MapCredits(limits.Credits, now) : null;
        if (usage is null && credits is null)
        {
            throw new InvalidOperationException("Codex returned no rate limits or credits.");
        }

        usage ??= new UsageSnapshot(null, null, null, now, null);
        return new ProviderFetchResult(usage, credits, "codex-cli", Id, Kind);
    }

    public bool ShouldFallback(Exception error, ProviderFetchContext context) => false;
}

