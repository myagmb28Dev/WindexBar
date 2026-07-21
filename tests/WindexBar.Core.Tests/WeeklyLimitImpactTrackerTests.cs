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

    [Fact]
    public void PreservesAccumulatedImpactWhenTheReportedUsageDropsTemporarily()
    {
        var store = new MemoryStateStore();
        var tracker = new WeeklyLimitImpactTracker(store);
        tracker.Apply(Snapshot(10, Session("a", 100_000, @"D:\Codes\ProjectA")));
        tracker.Apply(Snapshot(13, Session("a", 221_000, @"D:\Codes\ProjectA")));

        var corrected = tracker.Apply(Snapshot(11, Session("a", 230_000, @"D:\Codes\ProjectA")));
        var recovered = tracker.Apply(Snapshot(14, Session("a", 240_000, @"D:\Codes\ProjectA")));

        Assert.Equal(3, Assert.Single(corrected.Sessions!).WeeklyLimitImpactPercent);
        Assert.Equal(4, Assert.Single(recovered.Sessions!).WeeklyLimitImpactPercent);
    }

    [Fact]
    public void PreservesAccumulatedImpactWhenTheReportedResetTimeMoves()
    {
        var tracker = new WeeklyLimitImpactTracker(new MemoryStateStore());
        tracker.Apply(Snapshot(10, Session("a", 100_000, @"D:\Codes\ProjectA")));
        tracker.Apply(Snapshot(13, Session("a", 221_000, @"D:\Codes\ProjectA")));

        var result = tracker.Apply(Snapshot(
            13,
            ResetAt.AddMinutes(10),
            Session("a", 221_000, @"D:\Codes\ProjectA")));

        Assert.Equal(3, Assert.Single(result.Sessions!).WeeklyLimitImpactPercent);
    }

    [Fact]
    public void PrefersTheStableAccountWindowOverChangingModelWindows()
    {
        var store = new MemoryStateStore();
        var tracker = new WeeklyLimitImpactTracker(store);
        tracker.Apply(SnapshotWithModelWindow(10, "gpt-a", Session("a", 100_000, @"D:\Codes\ProjectA")));
        tracker.Apply(SnapshotWithModelWindow(13, "gpt-a", Session("a", 221_000, @"D:\Codes\ProjectA")));

        var result = tracker.Apply(SnapshotWithModelWindow(
            13,
            "gpt-b",
            Session("a", 221_000, @"D:\Codes\ProjectA")));

        Assert.Equal(3, Assert.Single(result.Sessions!).WeeklyLimitImpactPercent);
        Assert.Equal("account|10080", store.State.WindowId);
    }

    [Fact]
    public void PreservesImpactWhenAStartupModelWindowBecomesTheAccountWindow()
    {
        var tracker = new WeeklyLimitImpactTracker(new MemoryStateStore());
        tracker.Apply(SnapshotWithOnlyModelWindow(10, Session("a", 100_000, @"D:\Codes\ProjectA")));
        tracker.Apply(SnapshotWithOnlyModelWindow(13, Session("a", 221_000, @"D:\Codes\ProjectA")));

        var result = tracker.Apply(Snapshot(13, Session("a", 221_000, @"D:\Codes\ProjectA")));

        Assert.Equal(3, Assert.Single(result.Sessions!).WeeklyLimitImpactPercent);
    }

    [Fact]
    public void MigratesTheLegacyResetTimestampWindowIdWithoutLosingImpact()
    {
        var store = new MemoryStateStore(new WeeklyLimitImpactState
        {
            WindowId = $"model:Codex|10080|{ResetAt:O}",
            LastUsedPercent = 13,
            LastSessionTokens = new Dictionary<string, long> { ["a"] = 221_000 },
            SessionImpacts = new Dictionary<string, double> { ["a"] = 3 }
        });

        var result = new WeeklyLimitImpactTracker(store).Apply(
            Snapshot(13, Session("a", 221_000, @"D:\Codes\ProjectA")));

        Assert.Equal(3, Assert.Single(result.Sessions!).WeeklyLimitImpactPercent);
        Assert.Equal("account|10080", store.State.WindowId);
        Assert.Equal(ResetAt, store.State.WindowResetsAt);
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

    private static UsageSnapshot SnapshotWithModelWindow(
        double weeklyUsedPercent,
        string activeModel,
        params CodexSessionUsageSnapshot[] sessions)
    {
        var accountWeekly = new RateWindow(weeklyUsedPercent, 10_080, ResetAt, null);
        var modelWeekly = new RateWindow(weeklyUsedPercent, 10_080, ResetAt.AddMinutes(5), null);
        return new UsageSnapshot(
            new RateWindow(5, 300, ResetAt.AddDays(-6), null),
            accountWeekly,
            null,
            ResetAt.AddDays(-1),
            null,
            Models: [new ModelUsageSnapshot(activeModel, null, modelWeekly)],
            ActiveModel: new CodexModelSelection(activeModel, null, null, activeModel, ResetAt.AddDays(-1)),
            Sessions: sessions);
    }

    private static UsageSnapshot SnapshotWithOnlyModelWindow(
        double weeklyUsedPercent,
        params CodexSessionUsageSnapshot[] sessions) =>
        new(
            new RateWindow(5, 300, ResetAt.AddDays(-6), null),
            null,
            null,
            ResetAt.AddDays(-1),
            null,
            Models: [new ModelUsageSnapshot(
                "Codex",
                null,
                new RateWindow(weeklyUsedPercent, 10_080, ResetAt, null))],
            ActiveModel: new CodexModelSelection("Codex", null, null, "Codex", ResetAt.AddDays(-1)),
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

    private sealed class MemoryStateStore(WeeklyLimitImpactState? state = null) : IWeeklyLimitImpactStateStore
    {
        public WeeklyLimitImpactState State { get; private set; } = state ?? new();

        public WeeklyLimitImpactState Load() => State;

        public void Save(WeeklyLimitImpactState state) => State = state;
    }
}
