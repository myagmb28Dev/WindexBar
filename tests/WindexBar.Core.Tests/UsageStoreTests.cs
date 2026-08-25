using WindexBar.Core.Config;
using WindexBar.Core.Models;
using WindexBar.Core.Providers;
using WindexBar.Core.Providers.Codex;
using WindexBar.Core.Refresh;

namespace WindexBar.Core.Tests;

public sealed class UsageStoreTests
{
    [Fact]
    public void BackgroundRefreshBackoffReturnsToThirtySecondsAfterSuccess()
    {
        var policy = new BackgroundRefreshDelayPolicy();

        Assert.Equal(TimeSpan.FromSeconds(30), policy.NextDelay);
        policy.RecordResult(succeeded: false);
        Assert.Equal(TimeSpan.FromSeconds(60), policy.NextDelay);
        policy.RecordResult(succeeded: false);
        Assert.Equal(TimeSpan.FromSeconds(120), policy.NextDelay);
        policy.RecordResult(succeeded: false);
        Assert.Equal(TimeSpan.FromSeconds(120), policy.NextDelay);
        policy.RecordResult(succeeded: true);
        Assert.Equal(TimeSpan.FromSeconds(30), policy.NextDelay);
    }

    [Fact]
    public async Task ManualRefreshUpdatesSnapshot()
    {
        var settings = TestSettings();
        var descriptor = TestDescriptor(FetchResult(10, 55));
        var store = new UsageStore(settings, descriptor);
        var changedCount = 0;
        store.Changed += (_, _) => changedCount++;

        await store.RefreshAsync(CancellationToken.None);

        Assert.Null(store.LastError);
        Assert.Equal(90, store.Snapshot!.Primary!.RemainingPercent);
        Assert.Equal(55, store.Credits!.Remaining);
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public async Task FailurePreservesStaleSnapshot()
    {
        var settings = TestSettings();
        var descriptor = TestDescriptor(FetchResult(10, 0), new InvalidOperationException("boom"));
        var store = new UsageStore(settings, descriptor);
        await store.RefreshAsync(CancellationToken.None);
        var stale = store.Snapshot;

        await store.RefreshAsync(CancellationToken.None);

        Assert.Same(stale, store.Snapshot);
        Assert.Equal("boom", store.LastError);
    }

    [Fact]
    public async Task DisabledProviderClearsState()
    {
        var settings = TestSettings();
        settings.Update(config => config.GetProviderConfig(UsageProvider.Codex).Enabled = false);
        var store = new UsageStore(settings, CodexProviderDescriptor.Create(new QueueCodexRpcTransportFactory(Array.Empty<string[]>())));

        await store.RefreshAsync(CancellationToken.None);

        Assert.Null(store.Snapshot);
        Assert.Null(store.LastError);
    }

    [Fact]
    public async Task RefreshPreservesProviderResetCreditSnapshot()
    {
        var now = DateTimeOffset.Parse("2026-07-10T12:00:00+09:00");
        var resetCredits = new RateLimitResetCreditsSnapshot(
            1,
            now,
            [new RateLimitResetCredit("reset-1", now.AddDays(-8), now.AddDays(22), "codexRateLimits", "available", null, null)]);
        var result = FetchResultWithResetCredits(resetCredits, now);
        var expectedUsage = result.Usage;
        var store = new UsageStore(TestSettings(), TestDescriptor(result));

        await store.RefreshAsync(CancellationToken.None);

        Assert.Same(expectedUsage, store.Snapshot);
        Assert.Same(resetCredits, store.Snapshot!.RateLimitResetCredits);
    }

    [Fact]
    public async Task ArchivingSessionFileTriggersPromptRefresh()
    {
        var codexHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sessionDirectory = Path.Combine(codexHome, "sessions", "2026", "08", "25");
        var archivedDirectory = Path.Combine(codexHome, "archived_sessions");
        Directory.CreateDirectory(sessionDirectory);
        Directory.CreateDirectory(archivedDirectory);
        var activePath = Path.Combine(sessionDirectory, "rollout-test.jsonl");
        var archivedPath = Path.Combine(archivedDirectory, "rollout-test.jsonl");
        await File.WriteAllTextAsync(activePath, "{}");

        var strategy = new CountingProviderFetchStrategy(
            FetchResult(10, 55),
            FetchResult(20, 45));
        using var store = new UsageStore(
            TestSettings(),
            new ProviderDescriptor(new ProviderFetchPipeline([strategy])),
            weeklyLimitImpactTracker: null,
            rateLimitAlertTracker: null,
            codexHomeOverride: codexHome);

        try
        {
            store.StartBackgroundRefresh();
            await WaitUntilAsync(() => store.Snapshot?.Primary?.RemainingPercent == 90);

            File.Move(activePath, archivedPath);

            await WaitUntilAsync(() => store.Snapshot?.Primary?.RemainingPercent == 80);
            Assert.True(strategy.FetchCount >= 2);
            Assert.Equal(80, store.Snapshot!.Primary!.RemainingPercent);
        }
        finally
        {
            store.StopBackgroundRefresh();
            Directory.Delete(codexHome, recursive: true);
        }
    }

    private static SettingsStore TestSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "config.json");
        return new SettingsStore(new WindexBarConfigStore(path));
    }

    private static ProviderDescriptor TestDescriptor(params object[] outcomes) => new(
        new ProviderFetchPipeline([new QueueProviderFetchStrategy(outcomes)]));

    private static ProviderFetchResult FetchResult(double usedPercent, double creditsRemaining)
    {
        var now = DateTimeOffset.UnixEpoch;
        var usage = new UsageSnapshot(
            new RateWindow(usedPercent, 300, DateTimeOffset.FromUnixTimeSeconds(1_800_000_000)),
            null,
            now,
            new ProviderIdentitySnapshot("me@example.com", "plus"));
        var credits = new CreditsSnapshot(creditsRemaining, now);
        return new ProviderFetchResult(usage, credits);
    }

    private static ProviderFetchResult FetchResultWithResetCredits(
        RateLimitResetCreditsSnapshot resetCredits,
        DateTimeOffset now)
    {
        var usage = new UsageSnapshot(
            new RateWindow(10, 300, DateTimeOffset.FromUnixTimeSeconds(1_800_000_000)),
            null,
            now,
            new ProviderIdentitySnapshot("me@example.com", "plus"),
            RateLimitResetCredits: resetCredits);
        return new ProviderFetchResult(
            usage,
            new CreditsSnapshot(55, now));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(25, timeout.Token);
        }
    }

    private sealed class CountingProviderFetchStrategy(
        ProviderFetchResult initial,
        ProviderFetchResult subsequent) : IProviderFetchStrategy
    {
        private int _fetchCount;

        public int FetchCount => Volatile.Read(ref _fetchCount);

        public Task<bool> IsAvailableAsync(ProviderFetchContext context, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<ProviderFetchResult> FetchAsync(ProviderFetchContext context, CancellationToken cancellationToken)
        {
            var count = Interlocked.Increment(ref _fetchCount);
            return Task.FromResult(count == 1 ? initial : subsequent);
        }

        public bool ShouldFallback(Exception error, ProviderFetchContext context) => false;
    }
}
