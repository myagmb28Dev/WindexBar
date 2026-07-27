using WindexBar.Core.Refresh;

namespace WindexBar.Core.Tests;

public sealed class JsonStateStoreTests
{
    [Fact]
    public void WeeklyImpactStateRoundTripsWithoutLeavingTemporaryFiles()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "weekly.json");
        var store = new WeeklyLimitImpactStateStore(path);
        var state = new WeeklyLimitImpactState
        {
            WindowId = "account|10080",
            LastUsedPercent = 6,
            UnattributedImpact = 1,
            SessionImpacts = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["session-a"] = 1
            }
        };

        store.Save(state);
        var loaded = store.Load();

        Assert.Equal(state.WindowId, loaded.WindowId);
        Assert.Equal(1, loaded.SessionImpacts["session-a"]);
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public void CorruptAlertStateFallsBackWithoutOverwritingTheOriginal()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "alerts.json");
        const string corruptJson = "{ definitely-not-json";
        File.WriteAllText(path, corruptJson);
        var store = new RateLimitAlertStateStore(path);

        var loaded = store.Load();

        Assert.Empty(loaded.Windows);
        Assert.Equal(corruptJson, File.ReadAllText(path));
    }

    [Fact]
    public void AlertStateRoundTripsThroughTheSharedAtomicStore()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "alerts.json");
        var store = new RateLimitAlertStateStore(path);
        var reset = DateTimeOffset.Parse("2026-07-27T12:00:00Z");
        var state = new RateLimitAlertState
        {
            Windows = new Dictionary<string, RateLimitAlertWindowState>(StringComparer.OrdinalIgnoreCase)
            {
                ["weekly"] = new() { ResetsAt = reset, HighestAlertedThreshold = 90 }
            }
        };

        store.Save(state);
        var loaded = store.Load();

        Assert.Equal(reset, loaded.Windows["weekly"].ResetsAt);
        Assert.Equal(90, loaded.Windows["weekly"].HighestAlertedThreshold);
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp"));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"WindexBar.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
