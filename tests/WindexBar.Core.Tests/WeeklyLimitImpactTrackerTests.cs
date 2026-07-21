using WindexBar.Core.Models;
using WindexBar.Core.Presentation;
using WindexBar.Core.Refresh;

namespace WindexBar.Core.Tests;

public sealed class WeeklyLimitImpactTrackerTests
{
    private static readonly DateTimeOffset ResetAt = new(2026, 7, 27, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AttributesObservedWeeklyDecreaseToTheOnlyChangedSession()
    {
        var tracker = new WeeklyLimitImpactTracker(new MemoryStateStore());
        tracker.Apply(Snapshot(10, Session("a", 100_000, @"D:\Codes\ProjectA")));

        var result = tracker.Apply(Snapshot(13, Session("a", 221_000, @"D:\Codes\ProjectA")));

        var session = Assert.Single(result.Sessions!);
        Assert.Equal(3, session.WeeklyLimitImpactPercent);

        var view = SessionListViewModelFactory.Create(result.Sessions, true, "ko");
        var card = view.Projects[0].Sessions[0];
        Assert.Contains("\uC8FC\uAC04 \uD55C\uB3C4 \uC601\uD5A5: -3%", card.TokenDetails);
        Assert.DoesNotContain("\uC138\uC158 \uD569\uACC4", card.TokenDetails);
        Assert.Contains("\uC138\uC158 \uD569\uACC4", card.DetailedTokenDetails);
    }

    [Fact]
    public void KeepsTokenChangesPendingUntilTheServerReportsAWeeklyChange()
    {
        var tracker = new WeeklyLimitImpactTracker(new MemoryStateStore());
        tracker.Apply(Snapshot(10, Session("a", 100_000, @"D:\Codes\ProjectA")));
        tracker.Apply(Snapshot(10, Session("a", 221_000, @"D:\Codes\ProjectA")));

        var result = tracker.Apply(Snapshot(13, Session("a", 221_000, @"D:\Codes\ProjectA")));

        Assert.Equal(3, Assert.Single(result.Sessions!).WeeklyLimitImpactPercent);
    }

    [Fact]
    public void DistributesObservedImpactByTokenGrowthWhenSeveralSessionsChangeTogether()
    {
        var tracker = new WeeklyLimitImpactTracker(new MemoryStateStore());
        tracker.Apply(Snapshot(
            10,
            Session("a", 100_000, @"D:\Codes\ProjectA"),
            Session("b", 50_000, @"D:\Codes\ProjectA")));

        var result = tracker.Apply(Snapshot(
            13,
            Session("a", 180_000, @"D:\Codes\ProjectA"),
            Session("b", 90_000, @"D:\Codes\ProjectA")));

        Assert.Equal(2, result.Sessions!.Single(session => session.SessionId == "a").WeeklyLimitImpactPercent);
        Assert.Equal(1, result.Sessions!.Single(session => session.SessionId == "b").WeeklyLimitImpactPercent);
    }

    [Fact]
    public void DistributesObservedImpactAcrossProjectsWithoutShowingACombinedValue()
    {
        var store = new MemoryStateStore();
        var tracker = new WeeklyLimitImpactTracker(store);
        tracker.Apply(Snapshot(
            10,
            Session("a", 100_000, @"D:\Codes\ProjectA"),
            Session("b", 50_000, @"D:\Codes\ProjectB")));

        var result = tracker.Apply(Snapshot(
            13,
            Session("a", 180_000, @"D:\Codes\ProjectA"),
            Session("b", 90_000, @"D:\Codes\ProjectB")));

        Assert.Equal(2, result.Sessions!.Single(session => session.SessionId == "a").WeeklyLimitImpactPercent);
        Assert.Equal(1, result.Sessions!.Single(session => session.SessionId == "b").WeeklyLimitImpactPercent);
        Assert.Equal(0, store.State.UnattributedImpact);
    }

    [Fact]
    public void StartsAZeroedImpactWindowWhenTheWeeklyLimitResets()
    {
        var store = new MemoryStateStore();
        var tracker = new WeeklyLimitImpactTracker(store);
        tracker.Apply(Snapshot(10, Session("a", 100_000, @"D:\Codes\ProjectA")));
        tracker.Apply(Snapshot(13, Session("a", 221_000, @"D:\Codes\ProjectA")));

        var nextReset = ResetAt.AddDays(7);
        var result = tracker.Apply(Snapshot(
            1,
            nextReset,
            Session("a", 230_000, @"D:\Codes\ProjectA")));

        var session = Assert.Single(result.Sessions!);
        Assert.Equal(0, session.WeeklyLimitImpactPercent);
        Assert.Equal(0, store.State.UnattributedImpact);
    }

    [Fact]
    public void RestoresAccumulatedImpactAfterTrackerRestart()
    {
        var store = new MemoryStateStore();
        var tracker = new WeeklyLimitImpactTracker(store);
        tracker.Apply(Snapshot(10, Session("a", 100_000, @"D:\Codes\ProjectA")));
        tracker.Apply(Snapshot(13, Session("a", 221_000, @"D:\Codes\ProjectA")));

        var restarted = new WeeklyLimitImpactTracker(store);
        var result = restarted.Apply(Snapshot(13, Session("a", 221_000, @"D:\Codes\ProjectA")));

        Assert.Equal(3, Assert.Single(result.Sessions!).WeeklyLimitImpactPercent);
    }

    private static UsageSnapshot Snapshot(double weeklyUsedPercent, params CodexSessionUsageSnapshot[] sessions) =>
        Snapshot(weeklyUsedPercent, ResetAt, sessions);

    private static UsageSnapshot Snapshot(
        double weeklyUsedPercent,
        DateTimeOffset resetAt,
        params CodexSessionUsageSnapshot[] sessions) =>
        new(
            new RateWindow(5, 300, resetAt.AddDays(-6), null),
            new RateWindow(weeklyUsedPercent, 10_080, resetAt, null),
            null,
            resetAt.AddDays(-1),
            null,
            Sessions: sessions);

    private static CodexSessionUsageSnapshot Session(string id, long tokens, string projectPath) =>
        new(
            id,
            $"Session {id}",
            projectPath,
            new TokenUsageSnapshot(
                new TokenUsageBreakdown(tokens, 0, 0, 0, tokens),
                null,
                200_000,
                ResetAt.AddDays(-1)),
            ResetAt.AddDays(-1));

    private sealed class MemoryStateStore : IWeeklyLimitImpactStateStore
    {
        public WeeklyLimitImpactState State { get; private set; } = new();

        public WeeklyLimitImpactState Load() => State;

        public void Save(WeeklyLimitImpactState state) => State = state;
    }
}
