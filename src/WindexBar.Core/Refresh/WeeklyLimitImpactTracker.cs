using System.Text.Json;
using WindexBar.Core.Models;
using WindexBar.Core.Presentation;

namespace WindexBar.Core.Refresh;

public sealed class WeeklyLimitImpactState
{
    public string? WindowId { get; set; }
    public double? LastUsedPercent { get; set; }
    public Dictionary<string, long> LastSessionTokens { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, long> PendingSessionTokens { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> SessionImpacts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public double UnattributedImpact { get; set; }
}

public interface IWeeklyLimitImpactStateStore
{
    WeeklyLimitImpactState Load();
    void Save(WeeklyLimitImpactState state);
}

public sealed class WeeklyLimitImpactStateStore(string? filePath = null) : IWeeklyLimitImpactStateStore
{
    public string FilePath { get; } = filePath ?? DefaultPath();

    public WeeklyLimitImpactState Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new WeeklyLimitImpactState();
            }

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize(json, WindexBarJsonContext.Default.WeeklyLimitImpactState)
                ?? new WeeklyLimitImpactState();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return new WeeklyLimitImpactState();
        }
    }

    public void Save(WeeklyLimitImpactState state)
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);
            var temporaryPath = FilePath + ".tmp";
            var json = JsonSerializer.Serialize(state, WindexBarJsonContext.Default.WeeklyLimitImpactState);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, FilePath, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static string DefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "WindexBar", "weekly-limit-impact.json");
    }
}

public sealed class WeeklyLimitImpactTracker
{
    private const double PercentTolerance = 0.0001;
    private readonly IWeeklyLimitImpactStateStore _store;
    private WeeklyLimitImpactState _state;

    public WeeklyLimitImpactTracker(IWeeklyLimitImpactStateStore store)
    {
        _store = store;
        _state = Normalize(store.Load());
    }

    public UsageSnapshot Apply(UsageSnapshot snapshot)
    {
        var sessions = snapshot.Sessions ?? [];
        var selectedModel = HudDisplayModelFactory.FindCurrentSessionModel(snapshot.Models, snapshot.ActiveModel);
        var weekly = selectedModel?.Weekly ?? snapshot.Secondary;
        if (weekly is null)
        {
            return snapshot;
        }

        var sourceKey = selectedModel is null ? "account" : $"model:{selectedModel.ModelName.Trim()}";
        var windowId = CreateWindowId(sourceKey, weekly);
        var currentTokens = sessions
            .GroupBy(session => session.SessionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => SessionTotalTokens(group.OrderByDescending(session => session.UpdatedAt).First()),
                StringComparer.OrdinalIgnoreCase);
        if (!string.Equals(_state.WindowId, windowId, StringComparison.Ordinal)
            || _state.LastUsedPercent is null
            || weekly.UsedPercent + PercentTolerance < _state.LastUsedPercent.Value)
        {
            _state = new WeeklyLimitImpactState
            {
                WindowId = windowId,
                LastUsedPercent = weekly.UsedPercent,
                LastSessionTokens = currentTokens
            };
            _store.Save(_state);
            return WithImpacts(snapshot, sessions);
        }

        var stateChanged = false;
        foreach (var (sessionId, totalTokens) in currentTokens)
        {
            if (_state.LastSessionTokens.TryGetValue(sessionId, out var previousTokens)
                && totalTokens > previousTokens)
            {
                Add(_state.PendingSessionTokens, sessionId, totalTokens - previousTokens);
                stateChanged = true;
            }

            if (!_state.LastSessionTokens.TryGetValue(sessionId, out previousTokens)
                || previousTokens != totalTokens)
            {
                _state.LastSessionTokens[sessionId] = totalTokens;
                stateChanged = true;
            }
        }

        var usedPercentDelta = weekly.UsedPercent - _state.LastUsedPercent.Value;
        if (usedPercentDelta > PercentTolerance)
        {
            AttributeImpact(usedPercentDelta);
            _state.PendingSessionTokens.Clear();
            stateChanged = true;
        }

        _state.WindowId = windowId;
        if (Math.Abs(usedPercentDelta) > PercentTolerance)
        {
            _state.LastUsedPercent = weekly.UsedPercent;
            stateChanged = true;
        }

        if (stateChanged)
        {
            _store.Save(_state);
        }
        return WithImpacts(snapshot, sessions);
    }

    private void AttributeImpact(double usedPercentDelta)
    {
        var changedSessions = _state.PendingSessionTokens
            .Where(item => item.Value > 0)
            .ToArray();
        var totalChangedTokens = changedSessions.Sum(item => item.Value);
        if (totalChangedTokens > 0)
        {
            foreach (var (sessionId, tokenDelta) in changedSessions)
            {
                Add(
                    _state.SessionImpacts,
                    sessionId,
                    usedPercentDelta * tokenDelta / totalChangedTokens);
            }
            return;
        }

        _state.UnattributedImpact += usedPercentDelta;
    }

    private UsageSnapshot WithImpacts(
        UsageSnapshot snapshot,
        IReadOnlyList<CodexSessionUsageSnapshot> sessions)
    {
        var enriched = sessions
            .Select(session => session with
            {
                WeeklyLimitImpactPercent = _state.SessionImpacts.GetValueOrDefault(session.SessionId)
            })
            .ToArray();
        return snapshot with { Sessions = enriched };
    }

    private static string CreateWindowId(string sourceKey, RateWindow weekly)
    {
        var reset = weekly.ResetsAt?.ToUniversalTime().ToString("O") ?? "unknown-reset";
        return $"{sourceKey}|{weekly.WindowMinutes?.ToString() ?? "unknown-duration"}|{reset}";
    }

    private static long SessionTotalTokens(CodexSessionUsageSnapshot session) =>
        session.TokenUsage.Total?.TotalTokens
        ?? session.TokenUsage.Last?.TotalTokens
        ?? 0;

    private static void Add(Dictionary<string, long> values, string key, long delta) =>
        values[key] = values.GetValueOrDefault(key) + delta;

    private static void Add(Dictionary<string, double> values, string key, double delta) =>
        values[key] = values.GetValueOrDefault(key) + delta;

    private static WeeklyLimitImpactState Normalize(WeeklyLimitImpactState state)
    {
        state.LastSessionTokens = Copy(state.LastSessionTokens);
        state.PendingSessionTokens = Copy(state.PendingSessionTokens);
        state.SessionImpacts = Copy(state.SessionImpacts);
        return state;
    }

    private static Dictionary<string, TValue> Copy<TValue>(Dictionary<string, TValue>? values) =>
        new(values ?? [], StringComparer.OrdinalIgnoreCase);
}
