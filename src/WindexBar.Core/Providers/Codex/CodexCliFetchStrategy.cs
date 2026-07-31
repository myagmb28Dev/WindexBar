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

        try
        {
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

            usage ??= new UsageSnapshot(null, null, now, null);
            usage = EnrichWithSessionState(usage, sessionState, sessions);
            return new ProviderFetchResult(usage, credits);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested && HasSessionRateLimits(sessionState))
        {
            sessions = FilterUnavailableProjectSessions(sessions);
            var usage = EnrichWithSessionState(
                new UsageSnapshot(null, null, now, null),
                sessionState,
                sessions);
            return new ProviderFetchResult(usage, null);
        }
    }

    public bool ShouldFallback(Exception error, ProviderFetchContext context) => false;

    private static bool HasSessionRateLimits(CodexSessionStateSnapshot? sessionState) =>
        sessionState?.Models.Any(model => model.HasRateLimitWindows) == true;

    private static UsageSnapshot EnrichWithSessionState(
        UsageSnapshot usage,
        CodexSessionStateSnapshot? sessionState,
        IReadOnlyList<CodexSessionUsageSnapshot> sessions)
    {
        var sessionModels = sessionState?.Models ?? [];
        var models = MergeSessionModels(usage.Models, sessionModels);
        var genericSessionModel = sessionModels.FirstOrDefault(model =>
            IsSameModelName(model.ModelName, "Codex"));

        return usage with
        {
            Primary = genericSessionModel?.Current ?? usage.Primary,
            Secondary = genericSessionModel?.Weekly ?? usage.Secondary,
            Models = models,
            ActiveModel = sessionState?.ActiveModel,
            TokenUsage = sessionState?.TokenUsage,
            Sessions = sessions
        };
    }

    private static IReadOnlyList<ModelUsageSnapshot> MergeSessionModels(
        IReadOnlyList<ModelUsageSnapshot>? rpcModels,
        IReadOnlyList<ModelUsageSnapshot> sessionModels)
    {
        var merged = (rpcModels ?? [])
            .Where(model => model.HasRateLimitWindows)
            .ToList();
        foreach (var sessionModel in sessionModels.Where(model => model.HasRateLimitWindows))
        {
            var existingIndex = merged.FindIndex(model => IsSameModelName(model.ModelName, sessionModel.ModelName));
            if (existingIndex < 0)
            {
                merged.Add(sessionModel);
                continue;
            }

            var existing = merged[existingIndex];
            merged[existingIndex] = new ModelUsageSnapshot(
                existing.ModelName,
                sessionModel.Current ?? existing.Current,
                sessionModel.Weekly ?? existing.Weekly);
        }

        return merged;
    }

    private static bool IsSameModelName(string lhs, string rhs) =>
        string.Equals(
            CodexModelNaming.NormalizeModelKey(lhs),
            CodexModelNaming.NormalizeModelKey(rhs),
            StringComparison.OrdinalIgnoreCase);

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

