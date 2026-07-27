using WindexBar.Core.Models;
using WindexBar.Core.Refresh;

namespace WindexBar.Core.Tests;

public sealed class RateLimitAlertTrackerTests
{
    private static readonly DateTimeOffset ResetAt = new(2026, 7, 27, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AlertsAtEachThresholdOnlyOncePerWindow()
    {
        var tracker = new RateLimitAlertTracker(new MemoryStateStore());

        var first = tracker.Apply(Snapshot(current: 81, weekly: 10));
        var repeated = tracker.Apply(Snapshot(current: 85, weekly: 10));
        var second = tracker.Apply(Snapshot(current: 91, weekly: 10));

        var firstAlert = Assert.Single(first);
        Assert.Equal(RateLimitAlertWindow.Current, firstAlert.Window);
        Assert.Equal(80, firstAlert.ThresholdPercent);
        Assert.Empty(repeated);
        var secondAlert = Assert.Single(second);
        Assert.Equal(90, secondAlert.ThresholdPercent);
    }

    [Fact]
    public void ChoosesTheHighestNewThresholdWhenFirstObservedAboveNinetyPercent()
    {
        var alerts = new RateLimitAlertTracker(new MemoryStateStore()).Apply(Snapshot(current: 95, weekly: 10));

        var alert = Assert.Single(alerts);
        Assert.Equal(90, alert.ThresholdPercent);
    }

    [Fact]
    public void PreservesAlertedThresholdsAcrossTrackerRestart()
    {
        var store = new MemoryStateStore();
        new RateLimitAlertTracker(store).Apply(Snapshot(current: 82, weekly: 10));

        var alerts = new RateLimitAlertTracker(store).Apply(Snapshot(current: 85, weekly: 10));

        Assert.Empty(alerts);
    }

    [Fact]
    public void RearmsAlertsWhenCodexReportsTheNextResetWindow()
    {
        var tracker = new RateLimitAlertTracker(new MemoryStateStore());
        tracker.Apply(Snapshot(current: 82, weekly: 10));
        tracker.Apply(Snapshot(current: 5, weekly: 10, resetAt: ResetAt.AddHours(5)));

        var alerts = tracker.Apply(Snapshot(current: 81, weekly: 10, resetAt: ResetAt.AddHours(5)));

        var alert = Assert.Single(alerts);
        Assert.Equal(RateLimitAlertWindow.Current, alert.Window);
        Assert.Equal(80, alert.ThresholdPercent);
    }

    [Fact]
    public void DoesNotRearmAlertsWhenUsageIsTemporarilyCorrectedDownward()
    {
        var tracker = new RateLimitAlertTracker(new MemoryStateStore());
        tracker.Apply(Snapshot(current: 82, weekly: 10));
        tracker.Apply(Snapshot(current: 70, weekly: 10));

        var alerts = tracker.Apply(Snapshot(current: 82, weekly: 10));

        Assert.Empty(alerts);
    }

    [Fact]
    public void DoesNotRearmAlertsWhenTheSameResetTimestampIsCorrectedForward()
    {
        var tracker = new RateLimitAlertTracker(new MemoryStateStore());
        tracker.Apply(Snapshot(current: 82, weekly: 10, observedAt: ResetAt.AddHours(-2)));

        var alerts = tracker.Apply(Snapshot(
            current: 82,
            weekly: 10,
            resetAt: ResetAt.AddMinutes(10),
            observedAt: ResetAt.AddHours(-1)));

        Assert.Empty(alerts);
    }

    [Fact]
    public void DoesNotRewriteUnchangedAlertState()
    {
        var store = new MemoryStateStore();
        var tracker = new RateLimitAlertTracker(store);

        tracker.Apply(Snapshot(current: 10, weekly: 10));
        tracker.Apply(Snapshot(current: 20, weekly: 20));

        Assert.Equal(1, store.SaveCount);
    }

    private static UsageSnapshot Snapshot(
        double current,
        double weekly,
        DateTimeOffset? resetAt = null,
        DateTimeOffset? observedAt = null) =>
        new(
            new RateWindow(current, 300, resetAt ?? ResetAt, null),
            new RateWindow(weekly, 10_080, (resetAt ?? ResetAt).AddDays(6), null),
            null,
            observedAt ?? ResetAt,
            null);

    private sealed class MemoryStateStore(RateLimitAlertState? state = null) : IRateLimitAlertStateStore
    {
        public RateLimitAlertState State { get; private set; } = state ?? new();

        public int SaveCount { get; private set; }

        public RateLimitAlertState Load() => State;

        public void Save(RateLimitAlertState state)
        {
            State = state;
            SaveCount++;
        }
    }
}
