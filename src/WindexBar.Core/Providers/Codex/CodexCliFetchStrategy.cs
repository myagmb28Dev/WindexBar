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
        var sessions = sessionState?.Sessions ?? [];
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

        try
        {
            var threads = await client.FetchThreadsAsync(cancellationToken).ConfigureAwait(false);
            sessions = FilterAndEnrichSessions(sessions, threads.Data);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            sessions = FilterUnavailableProjectSessions(sessions);
        }

        var usage = CodexUsageMapper.MapUsage(limits, account, now);
        var credits = context.IncludeCredits ? CodexUsageMapper.MapCredits(limits.RateLimits.Credits, now) : null;
        if (usage is null && credits is null && activeModel is null && tokenUsage is null)
        {
            throw new InvalidOperationException("Codex returned no rate limits or credits.");
        }

        usage ??= new UsageSnapshot(null, null, null, now, null);
        usage = usage with { ActiveModel = activeModel, TokenUsage = tokenUsage, Sessions = sessions };
        return new ProviderFetchResult(usage, credits, "codex-cli", Id, Kind);
    }

    public bool ShouldFallback(Exception error, ProviderFetchContext context) => false;

    private static IReadOnlyList<CodexSessionUsageSnapshot> FilterAndEnrichSessions(
        IReadOnlyList<CodexSessionUsageSnapshot> sessions,
        IReadOnlyList<RpcThreadSummary> threads)
    {
        if (sessions.Count == 0)
        {
            return sessions;
        }

        var threadsById = threads
            .Where(thread => !string.IsNullOrWhiteSpace(thread.Id))
            .GroupBy(thread => thread.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var candidates = threadsById.Count == 0
            ? sessions
            : sessions.Where(session => threadsById.ContainsKey(session.SessionId));

        return FilterUnavailableProjectSessions(candidates
            .Select(session => threadsById.TryGetValue(session.SessionId, out var thread)
                ? session with
                {
                    SessionName = ThreadDisplayName(thread) ?? session.SessionName,
                    ProjectPath = string.IsNullOrWhiteSpace(thread.Cwd) ? session.ProjectPath : thread.Cwd.Trim()
                }
                : session)
            .ToArray());
    }

    private static IReadOnlyList<CodexSessionUsageSnapshot> FilterUnavailableProjectSessions(
        IEnumerable<CodexSessionUsageSnapshot> sessions) =>
        sessions
            .Where(session => IsAvailableProjectPath(session.ProjectPath))
            .ToArray();

    private static bool IsAvailableProjectPath(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return true;
        }

        var normalized = projectPath.Trim();
        return !Path.IsPathFullyQualified(normalized) || Directory.Exists(normalized);
    }

    private static string? ThreadDisplayName(RpcThreadSummary thread)
    {
        var displayName = string.IsNullOrWhiteSpace(thread.Name) ? thread.Preview : thread.Name;
        return string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
    }
}

