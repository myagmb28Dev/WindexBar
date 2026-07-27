using System.Text.Json.Serialization;
using WindexBar.Core.Models;
using WindexBar.Core.Persistence;

namespace WindexBar.Core.Refresh;

public enum RateLimitAlertWindow
{
    Current,
    Weekly
}

public sealed record RateLimitThresholdAlert(
    RateLimitAlertWindow Window,
    int ThresholdPercent,
    double UsedPercent);

public sealed class RateLimitAlertState
{
    [JsonPropertyName("windows")]
    public Dictionary<string, RateLimitAlertWindowState> Windows { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class RateLimitAlertWindowState
{
    [JsonPropertyName("resetsAt")]
    public DateTimeOffset? ResetsAt { get; set; }

    [JsonPropertyName("highestAlertedThreshold")]
    public int HighestAlertedThreshold { get; set; }
}

public interface IRateLimitAlertStateStore
{
    RateLimitAlertState Load();
    void Save(RateLimitAlertState state);
}

public sealed class RateLimitAlertStateStore(string? filePath = null) : IRateLimitAlertStateStore
{
    public string FilePath { get; } = filePath ?? DefaultPath();

    public RateLimitAlertState Load()
        => JsonFileStore.LoadOrDefault(
            FilePath,
            WindexBarJsonContext.Default.RateLimitAlertState,
            static () => new RateLimitAlertState());

    public void Save(RateLimitAlertState state)
    {
        JsonFileStore.TrySaveAtomic(
            FilePath,
            state,
            WindexBarJsonContext.Default.RateLimitAlertState);
    }

    public static string DefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "WindexBar", "rate-limit-alerts.json");
    }
}

public sealed class RateLimitAlertTracker
{
    private static readonly int[] Thresholds = [80, 90];
    private readonly IRateLimitAlertStateStore _store;
    private RateLimitAlertState _state;

    public RateLimitAlertTracker(IRateLimitAlertStateStore store)
    {
        _store = store;
        _state = Normalize(store.Load());
    }

    public IReadOnlyList<RateLimitThresholdAlert> Apply(UsageSnapshot snapshot)
    {
        var alerts = new List<RateLimitThresholdAlert>();
        var stateChanged = CheckWindow(RateLimitAlertWindow.Current, snapshot.Primary, snapshot.UpdatedAt, alerts);
        stateChanged |= CheckWindow(RateLimitAlertWindow.Weekly, snapshot.Secondary, snapshot.UpdatedAt, alerts);
        if (stateChanged)
        {
            _store.Save(_state);
        }

        return alerts;
    }

    private bool CheckWindow(
        RateLimitAlertWindow windowKind,
        RateWindow? window,
        DateTimeOffset observedAt,
        List<RateLimitThresholdAlert> alerts)
    {
        if (window is null)
        {
            return false;
        }

        var key = windowKind.ToString();
        var stateChanged = false;
        if (!_state.Windows.TryGetValue(key, out var state))
        {
            state = new RateLimitAlertWindowState { ResetsAt = window.ResetsAt };
            _state.Windows[key] = state;
            stateChanged = true;
        }
        else if (state.ResetsAt is not null
            && window.ResetsAt is not null
            && observedAt >= state.ResetsAt
            && window.ResetsAt > state.ResetsAt)
        {
            state.ResetsAt = window.ResetsAt;
            state.HighestAlertedThreshold = 0;
            stateChanged = true;
        }
        else if (state.ResetsAt is null && window.ResetsAt is not null)
        {
            state.ResetsAt = window.ResetsAt;
            stateChanged = true;
        }

        var crossedThreshold = Thresholds
            .Where(threshold => window.UsedPercent >= threshold && threshold > state.HighestAlertedThreshold)
            .DefaultIfEmpty()
            .Max();
        if (crossedThreshold == 0)
        {
            return stateChanged;
        }

        state.HighestAlertedThreshold = crossedThreshold;
        alerts.Add(new RateLimitThresholdAlert(windowKind, crossedThreshold, window.UsedPercent));
        return true;
    }

    private static RateLimitAlertState Normalize(RateLimitAlertState state)
    {
        state.Windows = new Dictionary<string, RateLimitAlertWindowState>(
            state.Windows ?? [],
            StringComparer.OrdinalIgnoreCase);
        return state;
    }
}
