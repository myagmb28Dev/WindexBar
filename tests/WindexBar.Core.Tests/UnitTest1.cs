using WindexBar.Core.Config;
using WindexBar.Core.Models;
using WindexBar.Core.Formatting;
using WindexBar.Core.Providers;
using WindexBar.Core.Providers.Codex;
using WindexBar.Core.Refresh;
using WindexBar.Core.Windowing;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WindexBar.Core.Tests;

public sealed class CodexRpcClientTests
{
    [Fact]
    public async Task FetchesRateLimitsAndAccountFromLineBasedJsonRpc()
    {
        var transport = new FakeCodexRpcTransport(
            OnRequest(1, new { ok = true }),
            OnRequest(2, new
            {
                rateLimits = new
                {
                    primary = new { usedPercent = 25.0, windowDurationMins = 300, resetsAt = 1_800_000_000 },
                    secondary = new { usedPercent = 40.0, windowDurationMins = 10080, resetsAt = 1_800_100_000 },
                    credits = new { hasCredits = true, unlimited = false, balance = "123.5" },
                    planType = "plus"
                },
                rateLimitResetCredits = new { availableCount = 2 }
            }),
            OnRequest(3, new { account = new { type = "chatgpt", email = "me@example.com", planType = "team" } }));

        await using var client = new CodexRpcClient(transport, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        await client.InitializeAsync(CancellationToken.None);
        var limits = await client.FetchRateLimitsAsync(CancellationToken.None);
        var account = await client.FetchAccountAsync(CancellationToken.None);
        var usage = CodexUsageMapper.MapUsage(limits, account, DateTimeOffset.UnixEpoch)!;
        var credits = CodexUsageMapper.MapCredits(limits.RateLimits.Credits, DateTimeOffset.UnixEpoch)!;

        Assert.Equal(25, usage.Primary!.UsedPercent);
        Assert.Equal(75, usage.Primary.RemainingPercent);
        Assert.Equal(300, usage.Primary.WindowMinutes);
        Assert.Equal("me@example.com", usage.Identity!.AccountEmail);
        Assert.Equal("team", usage.Identity.LoginMethod);
        Assert.Equal(2, usage.RateLimitResetCredits!.AvailableCount);
        Assert.Equal(123.5, credits.Remaining, 3);
        Assert.Contains("initialized", transport.Writes[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThrowsTimeoutForMissingReply()
    {
        var transport = new FakeCodexRpcTransport();
        await using var client = new CodexRpcClient(transport, TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAsync<CodexRpcTimeoutException>(() => client.InitializeAsync(CancellationToken.None));
        Assert.True(transport.Killed);
    }

    [Fact]
    public async Task ThrowsForMalformedJson()
    {
        var transport = new FakeCodexRpcTransport("not json");
        await using var client = new CodexRpcClient(transport, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        var error = await Assert.ThrowsAsync<CodexRpcException>(() => client.InitializeAsync(CancellationToken.None));
        Assert.Contains("malformed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string OnRequest(int id, object result) =>
        System.Text.Json.JsonSerializer.Serialize(new { id, result });
}

public sealed class MappingTests
{
    [Fact]
    public void MapsRpcWindowAndCredits()
    {
        var window = CodexUsageMapper.MapWindow(new RpcRateLimitWindow
        {
            UsedPercent = 12.5,
            WindowDurationMins = 300,
            ResetsAt = 1_800_000_000
        });
        var credits = CodexUsageMapper.MapCredits(new RpcCreditsSnapshot { Balance = "42" }, DateTimeOffset.UnixEpoch);

        Assert.Equal(87.5, window!.RemainingPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000), window.ResetsAt);
        Assert.Equal(42, credits!.Remaining);
    }

    [Fact]
    public void MapsRateLimitResetCredits()
    {
        var response = JsonSerializer.Deserialize<RpcRateLimitsResponse>(JsonSerializer.Serialize(new
        {
            rateLimits = new
            {
                primary = new { usedPercent = 12.0, windowDurationMins = 300, resetsAt = 1_800_000_000L }
            },
            rateLimitResetCredits = new { availableCount = 3L }
        }))!;

        var usage = CodexUsageMapper.MapUsage(response, null, DateTimeOffset.UnixEpoch)!;

        Assert.Equal(3, usage.RateLimitResetCredits!.AvailableCount);
        Assert.Equal(DateTimeOffset.UnixEpoch, usage.RateLimitResetCredits.UpdatedAt);
    }

    [Fact]
    public void MapsUnknownRateLimitWindowForSparkWindow()
    {
        var response = JsonSerializer.Deserialize<RpcRateLimitsResponse>(JsonSerializer.Serialize(new
        {
            rateLimits = new Dictionary<string, object?>
            {
                ["gpt-5.3-codex-spark"] = new
                {
                    usedPercent = 9.0,
                    windowDurationMins = 120,
                    resetsAt = 1_800_002_000L
                },
                ["planType"] = "plus",
                ["credits"] = new { hasCredits = true, unlimited = false, balance = "1" }
            }
        }))!;

        var usage = CodexUsageMapper.MapUsage(response.RateLimits, null, DateTimeOffset.UnixEpoch)!;

        Assert.NotNull(usage.Identity);
        Assert.Equal("plus", usage.Identity.LoginMethod);
        Assert.NotNull(usage.Primary);
        Assert.Equal(9, usage.Primary.UsedPercent);
        Assert.Equal(120, usage.Primary.WindowMinutes);
        var model = Assert.Single(usage.Models!);
        Assert.Equal("GPT-5.3 Codex Spark", model.ModelName);
        Assert.Equal(9, model.Current!.UsedPercent);
        Assert.Null(model.Weekly);
    }

    [Fact]
    public void MapsDefaultAndSparkModelsWithCurrentAndWeeklyWindows()
    {
        var response = JsonSerializer.Deserialize<RpcRateLimitsResponse>(JsonSerializer.Serialize(new
        {
            rateLimits = new Dictionary<string, object?>
            {
                ["primary"] = new { usedPercent = 10.0, windowDurationMins = 300, resetsAt = 1_800_000_000L },
                ["secondary"] = new { usedPercent = 20.0, windowDurationMins = 10080, resetsAt = 1_800_100_000L },
                ["gpt-5.3-codex-spark"] = new
                {
                    primary = new { usedPercent = 30.0, windowDurationMins = 300, resetsAt = 1_800_200_000L },
                    secondary = new { usedPercent = 40.0, windowDurationMins = 10080, resetsAt = 1_800_300_000L }
                },
                ["planType"] = "plus"
            }
        }))!;

        var usage = CodexUsageMapper.MapUsage(response.RateLimits, null, DateTimeOffset.UnixEpoch)!;

        Assert.Equal(2, usage.Models!.Count);
        Assert.Equal("Codex", usage.Models[0].ModelName);
        Assert.Equal(10, usage.Models[0].Current!.UsedPercent);
        Assert.Equal(20, usage.Models[0].Weekly!.UsedPercent);
        Assert.Equal("GPT-5.3 Codex Spark", usage.Models[1].ModelName);
        Assert.Equal(30, usage.Models[1].Current!.UsedPercent);
        Assert.Equal(40, usage.Models[1].Weekly!.UsedPercent);
    }

    [Fact]
    public void MapsNestedModelLimitContainer()
    {
        var response = JsonSerializer.Deserialize<RpcRateLimitsResponse>(JsonSerializer.Serialize(new
        {
            rateLimits = new Dictionary<string, object?>
            {
                ["primary"] = new { usedPercent = 11.0, windowDurationMins = 300, resetsAt = 1_800_000_000L },
                ["secondary"] = new { usedPercent = 4.0, windowDurationMins = 10080, resetsAt = 1_800_100_000L },
                ["models"] = new Dictionary<string, object?>
                {
                    ["gpt-5.3-codex-spark"] = new
                    {
                        primary = new { usedPercent = 16.0, windowDurationMins = 300, resetsAt = 1_800_200_000L },
                        secondary = new { usedPercent = 5.0, windowDurationMins = 10080, resetsAt = 1_800_300_000L }
                    }
                }
            }
        }))!;

        var usage = CodexUsageMapper.MapUsage(response.RateLimits, null, DateTimeOffset.UnixEpoch)!;

        Assert.Equal(2, usage.Models!.Count);
        Assert.Equal("Codex", usage.Models[0].ModelName);
        Assert.Equal("GPT-5.3 Codex Spark", usage.Models[1].ModelName);
        Assert.Equal(16, usage.Models[1].Current!.UsedPercent);
        Assert.Equal(5, usage.Models[1].Weekly!.UsedPercent);
    }

    [Fact]
    public void MapsRateLimitsByLimitIdAsModelPages()
    {
        var response = JsonSerializer.Deserialize<RpcRateLimitsResponse>(JsonSerializer.Serialize(new
        {
            rateLimits = new
            {
                primary = new { usedPercent = 12.0, windowDurationMins = 300, resetsAt = 1_800_000_000L },
                secondary = new { usedPercent = 4.0, windowDurationMins = 10080, resetsAt = 1_800_100_000L },
                planType = "plus"
            },
            rateLimitsByLimitId = new Dictionary<string, object?>
            {
                ["codex"] = new
                {
                    limitId = "codex",
                    limitName = "Codex",
                    primary = new { usedPercent = 12.0, windowDurationMins = 300, resetsAt = 1_800_000_000L },
                    secondary = new { usedPercent = 4.0, windowDurationMins = 10080, resetsAt = 1_800_100_000L }
                },
                ["gpt-5.3-codex-spark"] = new
                {
                    limitId = "gpt-5.3-codex-spark",
                    limitName = "GPT-5.3-Codex-Spark",
                    primary = new { usedPercent = 20.0, windowDurationMins = 300, resetsAt = 1_800_200_000L },
                    secondary = new { usedPercent = 6.0, windowDurationMins = 10080, resetsAt = 1_800_300_000L }
                }
            }
        }))!;

        var usage = CodexUsageMapper.MapUsage(response, null, DateTimeOffset.UnixEpoch)!;

        Assert.Equal(2, usage.Models!.Count);
        Assert.Equal("Codex", usage.Models[0].ModelName);
        Assert.Equal(12, usage.Models[0].Current!.UsedPercent);
        Assert.Equal(4, usage.Models[0].Weekly!.UsedPercent);
        Assert.Equal("GPT-5.3 Codex Spark", usage.Models[1].ModelName);
        Assert.Equal(20, usage.Models[1].Current!.UsedPercent);
        Assert.Equal(6, usage.Models[1].Weekly!.UsedPercent);
    }

    [Fact]
    public void GroupsReasoningBucketsByModelVersion()
    {
        var response = JsonSerializer.Deserialize<RpcRateLimitsResponse>(JsonSerializer.Serialize(new
        {
            rateLimits = new { },
            rateLimitsByLimitId = new Dictionary<string, object?>
            {
                ["gpt-5.5-xhigh"] = new
                {
                    limitId = "gpt-5.5-xhigh",
                    limitName = "GPT-5.5 Extra High Reasoning",
                    primary = new { usedPercent = 10.0, windowDurationMins = 300, resetsAt = 1_800_000_000L }
                },
                ["gpt-5.5-low"] = new
                {
                    limitId = "gpt-5.5-low",
                    limitName = "GPT-5.5 Low Reasoning",
                    secondary = new { usedPercent = 22.0, windowDurationMins = 10080, resetsAt = 1_800_100_000L }
                },
                ["gpt-5.4-high"] = new
                {
                    limitId = "gpt-5.4-high",
                    limitName = "GPT-5.4 High Reasoning",
                    primary = new { usedPercent = 30.0, windowDurationMins = 300, resetsAt = 1_800_200_000L }
                }
            }
        }))!;

        var usage = CodexUsageMapper.MapUsage(response, null, DateTimeOffset.UnixEpoch)!;

        Assert.Equal(2, usage.Models!.Count);
        Assert.Equal("GPT-5.5 XHigh", usage.Models[0].ModelName);
        Assert.Equal(10, usage.Models[0].Current!.UsedPercent);
        Assert.Equal(22, usage.Models[0].Weekly!.UsedPercent);
        Assert.Equal("GPT-5.4 High", usage.Models[1].ModelName);
        Assert.Equal(30, usage.Models[1].Current!.UsedPercent);
    }

    [Fact]
    public void MapsAllLimitSourcesThroughCommonCodexBuckets()
    {
        var response = JsonSerializer.Deserialize<RpcRateLimitsResponse>(JsonSerializer.Serialize(new
        {
            rateLimits = new Dictionary<string, object?>
            {
                ["primary"] = new { usedPercent = 12.0, windowDurationMins = 300, resetsAt = 1_800_000_000L },
                ["gpt-5.4-high"] = new { usedPercent = 30.0, windowDurationMins = 300, resetsAt = 1_800_100_000L },
                ["gpt-5.4-low"] = new { usedPercent = 45.0, windowDurationMins = 10080, resetsAt = 1_800_200_000L }
            },
            rateLimitsByLimitId = new Dictionary<string, object?>
            {
                ["gpt-5.3-codex-spark"] = new
                {
                    limitId = "gpt-5.3-codex-spark",
                    limitName = "GPT-5.3-Codex-Spark",
                    primary = new { usedPercent = 20.0, windowDurationMins = 300, resetsAt = 1_800_300_000L },
                    secondary = new { usedPercent = 6.0, windowDurationMins = 10080, resetsAt = 1_800_400_000L }
                }
            }
        }))!;

        var usage = CodexUsageMapper.MapUsage(response, null, DateTimeOffset.UnixEpoch)!;

        Assert.Equal(3, usage.Models!.Count);
        Assert.Equal("Codex", usage.Models[0].ModelName);
        Assert.Equal(12, usage.Models[0].Current!.UsedPercent);
        Assert.Equal("GPT-5.4 High", usage.Models[1].ModelName);
        Assert.Equal(30, usage.Models[1].Current!.UsedPercent);
        Assert.Equal(45, usage.Models[1].Weekly!.UsedPercent);
        Assert.Equal("GPT-5.3 Codex Spark", usage.Models[2].ModelName);
        Assert.Equal(20, usage.Models[2].Current!.UsedPercent);
        Assert.Equal(6, usage.Models[2].Weekly!.UsedPercent);
    }

    [Fact]
    public void MissingExecutableReturnsNull()
    {
        var path = CommandLocator.ResolveExecutable("definitely-not-windexbar-test-command", new Dictionary<string, string>
        {
            ["PATH"] = "C:\\no-such-dir",
            ["PATHEXT"] = ".EXE;.CMD"
        });

        Assert.Null(path);
    }
}

public sealed class ConfigTests
{
    [Fact]
    public void CreatesAndPersistsDefaultConfig()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "config.json");
        var store = new WindexBarConfigStore(path);
        var config = store.LoadOrCreateDefault();
        config.SetProviderConfig(new ProviderConfig { Id = "codex", Enabled = false });
        store.Save(config);

        var reloaded = store.LoadOrCreateDefault();

        Assert.True(File.Exists(path));
        Assert.False(reloaded.GetProviderConfig(UsageProvider.Codex).Enabled);
        Assert.Equal(WindexBarConfig.DefaultRefreshIntervalSeconds, reloaded.GetProviderConfig(UsageProvider.Codex).RefreshIntervalSeconds);
        Assert.False(reloaded.ClickThroughHud);
        Assert.Equal(WindexBarConfig.DefaultLanguage, reloaded.Language);
        Assert.Equal(WindexBarConfig.DefaultToggleWindowHotkey, reloaded.Hotkeys.ToggleWindow);
        Assert.Equal(WindexBarConfig.DefaultToggleSidebarHotkey, reloaded.Hotkeys.ToggleSidebar);
        Assert.True(reloaded.StartWithWindows);
    }

    [Fact]
    public void PreservesSavedClickThroughHudValue()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
        {
          "version": 1,
          "clickThroughHud": true,
          "providers": [
            { "id": "codex", "enabled": true, "source": "cli" }
          ]
        }
        """);
        var store = new WindexBarConfigStore(path);

        var config = store.LoadOrCreateDefault();

        Assert.Equal(WindexBarConfig.CurrentVersion, config.Version);
        Assert.True(config.ClickThroughHud);
        Assert.Equal(WindexBarConfig.DefaultRefreshIntervalSeconds, config.GetProviderConfig(UsageProvider.Codex).RefreshIntervalSeconds);
    }

    [Fact]
    public void PreservesSavedRefreshIntervalSeconds()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "config.json");
        var store = new WindexBarConfigStore(path);
        var config = store.LoadOrCreateDefault();
        config.SetProviderConfig(new ProviderConfig { Id = "codex", RefreshIntervalSeconds = 5 });
        store.Save(config);

        var reloaded = store.LoadOrCreateDefault();

        Assert.Equal(5, reloaded.GetProviderConfig(UsageProvider.Codex).RefreshIntervalSeconds);
    }

    [Fact]
    public void PreservesSavedStartWithWindows()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "config.json");
        var store = new WindexBarConfigStore(path);
        var config = store.LoadOrCreateDefault();
        config.StartWithWindows = false;
        store.Save(config);

        var reloaded = store.LoadOrCreateDefault();

        Assert.False(reloaded.StartWithWindows);
    }

    [Fact]
    public void PreservesSavedLanguage()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "config.json");
        var store = new WindexBarConfigStore(path);
        var config = store.LoadOrCreateDefault();
        config.Language = "ko";
        store.Save(config);

        var reloaded = store.LoadOrCreateDefault();

        Assert.Equal("ko", reloaded.Language);
    }

    [Fact]
    public void NormalizesSavedLanguage()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
        {
          "version": 1,
          "language": "ko-KR",
          "providers": [
            { "id": "codex", "enabled": true, "source": "cli" }
          ]
        }
        """);
        var store = new WindexBarConfigStore(path);

        var config = store.LoadOrCreateDefault();

        Assert.Equal("ko", config.Language);
    }

    [Fact]
    public void NormalizesSavedToggleHotkey()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
        {
          "version": 1,
          "hotkeys": {
            "toggleWindow": "alt + o"
          },
          "providers": [
            { "id": "codex", "enabled": true, "source": "cli" }
          ]
        }
        """);
        var store = new WindexBarConfigStore(path);

        var config = store.LoadOrCreateDefault();

        Assert.Equal("Alt+O", config.Hotkeys.ToggleWindow);
    }

    [Fact]
    public void NormalizesSavedSidebarHotkey()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
        {
          "version": 1,
          "hotkeys": {
            "toggleSidebar": "alt + b"
          },
          "providers": [
            { "id": "codex", "enabled": true, "source": "cli" }
          ]
        }
        """);
        var store = new WindexBarConfigStore(path);

        var config = store.LoadOrCreateDefault();

        Assert.Equal("Alt+B", config.Hotkeys.ToggleSidebar);
    }

    [Fact]
    public void InvalidToggleHotkeyFallsBackToDefault()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
        {
          "version": 1,
          "hotkeys": {
            "toggleWindow": "O"
          },
          "providers": [
            { "id": "codex", "enabled": true, "source": "cli" }
          ]
        }
        """);
        var store = new WindexBarConfigStore(path);

        var config = store.LoadOrCreateDefault();

        Assert.Equal(WindexBarConfig.DefaultToggleWindowHotkey, config.Hotkeys.ToggleWindow);
    }

    [Fact]
    public void InvalidSidebarHotkeyFallsBackToDefault()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
        {
          "version": 1,
          "hotkeys": {
            "toggleSidebar": "B"
          },
          "providers": [
            { "id": "codex", "enabled": true, "source": "cli" }
          ]
        }
        """);
        var store = new WindexBarConfigStore(path);

        var config = store.LoadOrCreateDefault();

        Assert.Equal(WindexBarConfig.DefaultToggleSidebarHotkey, config.Hotkeys.ToggleSidebar);
    }
}

public sealed class HotkeyShortcutTests
{
    [Theory]
    [InlineData("alt+o", "Alt+O")]
    [InlineData("Ctrl + Shift + F12", "Ctrl+Shift+F12")]
    [InlineData("win + space", "Win+Space")]
    public void NormalizesShortcutText(string value, string expected)
    {
        var parsed = HotkeyShortcut.TryParse(value, out var shortcut);

        Assert.True(parsed);
        Assert.Equal(expected, shortcut!.DisplayText);
    }

    [Theory]
    [InlineData("")]
    [InlineData("O")]
    [InlineData("Alt")]
    [InlineData("Alt+Ctrl")]
    [InlineData("Alt+O+P")]
    public void RejectsIncompleteOrAmbiguousShortcutText(string value)
    {
        Assert.False(HotkeyShortcut.TryParse(value, out _));
    }
}

public sealed class TokenCountFormatterTests
{
    [Theory]
    [InlineData(161_000, "ko", "16\uB9CC 1\uCC9C")]
    [InlineData(258_400, "ko", "25\uB9CC 8\uCC9C")]
    [InlineData(1_610_000, "ko", "161\uB9CC")]
    [InlineData(161_000, "en", "161K")]
    public void FormatsTokenCountsForLanguage(long tokens, string language, string expected)
    {
        Assert.Equal(expected, TokenCountFormatter.Format(tokens, language));
    }
}

public sealed class RateLimitResetCreditTrackerTests
{
    [Fact]
    public void FirstObservationMarksExistingCreditsAsUnknown()
    {
        var now = DateTimeOffset.Parse("2026-06-30T12:00:00+09:00");

        var state = RateLimitResetCreditTracker.Update(new RateLimitResetCreditState(), 2, now);

        Assert.True(state.HasObserved);
        Assert.Equal(2, state.Credits.Count);
        Assert.All(state.Credits, credit => Assert.Null(credit.EstimatedExpiresAt));
    }

    [Fact]
    public void IncreaseAddsThirtyDayEstimatedExpiration()
    {
        var firstSeenAt = DateTimeOffset.Parse("2026-06-29T12:00:00+09:00");
        var now = DateTimeOffset.Parse("2026-06-30T12:00:00+09:00");
        var state = new RateLimitResetCreditState
        {
            HasObserved = true,
            Credits = new List<RateLimitResetCreditObservation>
            {
                new(firstSeenAt, null)
            }
        };

        var updated = RateLimitResetCreditTracker.Update(state, 2, now);

        var estimated = Assert.Single(updated.Credits, credit => credit.EstimatedExpiresAt is not null);
        Assert.Equal(now, estimated.FirstSeenAt);
        Assert.Equal(now.AddDays(30), estimated.EstimatedExpiresAt);
    }

    [Fact]
    public void DecreaseRemovesEarliestEstimatedExpiration()
    {
        var earlier = new RateLimitResetCreditObservation(
            DateTimeOffset.Parse("2026-06-29T12:00:00+09:00"),
            DateTimeOffset.Parse("2026-07-29T12:00:00+09:00"));
        var later = new RateLimitResetCreditObservation(
            DateTimeOffset.Parse("2026-06-30T12:00:00+09:00"),
            DateTimeOffset.Parse("2026-07-30T12:00:00+09:00"));
        var state = new RateLimitResetCreditState
        {
            HasObserved = true,
            Credits = new List<RateLimitResetCreditObservation> { later, earlier }
        };

        var updated = RateLimitResetCreditTracker.Update(state, 1, DateTimeOffset.Parse("2026-07-01T12:00:00+09:00"));

        Assert.Equal(later, Assert.Single(updated.Credits));
    }

    [Fact]
    public void DecreaseRemovesUnknownLegacyCreditBeforeEstimatedCredit()
    {
        var legacy = new RateLimitResetCreditObservation(
            DateTimeOffset.Parse("2026-06-29T12:00:00+09:00"),
            null);
        var estimated = new RateLimitResetCreditObservation(
            DateTimeOffset.Parse("2026-06-30T12:00:00+09:00"),
            DateTimeOffset.Parse("2026-07-30T12:00:00+09:00"));
        var state = new RateLimitResetCreditState
        {
            HasObserved = true,
            Credits = new List<RateLimitResetCreditObservation> { legacy, estimated }
        };

        var updated = RateLimitResetCreditTracker.Update(state, 1, DateTimeOffset.Parse("2026-07-01T12:00:00+09:00"));

        Assert.Equal(estimated, Assert.Single(updated.Credits));
    }

    [Fact]
    public void RepairsSingleUnknownCreditFromPriorRemovalBugWhenStillInsideThirtyDayWindow()
    {
        var firstSeenAt = DateTimeOffset.Parse("2026-06-30T12:00:00+09:00");
        var state = new RateLimitResetCreditState
        {
            Version = 1,
            HasObserved = true,
            Credits = new List<RateLimitResetCreditObservation>
            {
                new(firstSeenAt, null)
            }
        };

        var updated = RateLimitResetCreditTracker.Update(state, 1, DateTimeOffset.Parse("2026-07-01T12:00:00+09:00"));
        var credit = Assert.Single(updated.Credits);

        Assert.Equal(firstSeenAt.AddDays(30), credit.EstimatedExpiresAt);
    }

    [Fact]
    public void SnapshotReportsNextEstimatedExpirationAndUnknownCount()
    {
        var now = DateTimeOffset.Parse("2026-06-30T12:00:00+09:00");
        var snapshot = new RateLimitResetCreditsSnapshot(
            2,
            now,
            new List<RateLimitResetCreditObservation>
            {
                new(now.AddDays(-1), null),
                new(now, now.AddDays(30))
            });

        Assert.Equal(1, snapshot.UnknownExpirationCount);
        Assert.Equal(now.AddDays(30), snapshot.NextEstimatedExpiresAt);
    }
}

public sealed class RateLimitResetCreditFormatterTests
{
    [Fact]
    public void FormatsResetCreditSummaryAndDetail()
    {
        var now = DateTimeOffset.Parse("2026-06-30T12:00:00+09:00");
        var snapshot = new RateLimitResetCreditsSnapshot(
            8,
            now,
            new List<RateLimitResetCreditObservation>
            {
                new(now.AddDays(-1), null),
                new(now, now.AddDays(29)),
                new(now, now.AddDays(20)),
                new(now, now.AddDays(19)),
                new(now, now.AddDays(20)),
                new(now.AddDays(-2), null),
                new(now, now.AddDays(19)),
                new(now, now.AddDays(20))
            });

        var text = RateLimitResetCreditFormatter.Format(snapshot, "en", now);

        Assert.Equal("8\uAC1C \uBCF4\uC720" + Environment.NewLine + "\uCCAB \uB9CC\uB8CC D-19", RateLimitResetCreditFormatter.FormatSummary(snapshot, "ko", now));
        Assert.Equal("8 held" + Environment.NewLine + "First expiry D-19", text);

        var detail = RateLimitResetCreditFormatter.FormatDetail(snapshot, "ko", now);

        Assert.Equal(
            "19\uC77C \uD6C4 \uB9CC\uB8CC: 2\uAC1C" + Environment.NewLine +
            "20\uC77C \uD6C4 \uB9CC\uB8CC: 3\uAC1C" + Environment.NewLine +
            "29\uC77C \uD6C4 \uB9CC\uB8CC: 1\uAC1C" + Environment.NewLine +
            "\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500" + Environment.NewLine +
            "\uAE30\uC874 \uCD08\uAE30\uD654\uAD8C: 2\uAC1C",
            detail);
    }
}

public sealed class WindowPlacementControllerTests
{
    [Fact]
    public void FirstResizeUsesDefaultPositionThenPreservesCurrentPosition()
    {
        var controller = new WindowPlacementController(new WindowPosition(96, 96));

        var initialPosition = controller.PositionForResize(new WindowPosition(320, 220));
        var restoredPosition = controller.PositionForResize(new WindowPosition(320, 220));

        Assert.Equal(new WindowPosition(96, 96), initialPosition);
        Assert.Equal(new WindowPosition(320, 220), restoredPosition);
    }

    [Fact]
    public void ActivationPlanPreservesCurrentBounds()
    {
        var plan = WindowActivationPlan.PreserveCurrentBounds;

        Assert.True(plan.PreservesPosition);
        Assert.True(plan.PreservesSize);
        Assert.Equal(0, plan.X);
        Assert.Equal(0, plan.Y);
        Assert.Equal(0, plan.Width);
        Assert.Equal(0, plan.Height);
    }
}

public sealed class CodexSessionStateReaderTests
{
    [Fact]
    public void ReadsLatestUserTurnContextAndSkipsSubagents()
    {
        var codexHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sessionDir = Path.Combine(codexHome, "sessions", "2026", "06", "18");
        Directory.CreateDirectory(sessionDir);

        var subagentPath = Path.Combine(sessionDir, "rollout-subagent.jsonl");
        File.WriteAllText(subagentPath, """
        {"timestamp":"2026-06-18T01:00:00Z","type":"session_meta","payload":{"id":"sub","thread_source":"subagent","source":{"subagent":{"other":"guardian"}}}}
        {"timestamp":"2026-06-18T01:00:01Z","type":"turn_context","payload":{"model":"gpt-5.4","effort":"low"}}
        """);
        File.SetLastWriteTimeUtc(subagentPath, DateTime.UtcNow.AddMinutes(1));

        var userPath = Path.Combine(sessionDir, "rollout-user.jsonl");
        File.WriteAllText(userPath, """
        {"timestamp":"2026-06-18T00:59:00Z","type":"session_meta","payload":{"id":"user","thread_source":"user","source":"vscode"}}
        {"timestamp":"2026-06-18T00:59:01Z","type":"turn_context","payload":{"model":"gpt-5.5","effort":"xhigh"}}
        """);

        var selection = CodexSessionStateReader.ReadLatest(TestEnvironment(codexHome));

        Assert.NotNull(selection);
        Assert.Equal("gpt-5.5", selection!.Model);
        Assert.Equal("xhigh", selection.ReasoningEffort);
        Assert.Equal("GPT-5.5 XHigh", selection.DisplayName);
    }

    [Fact]
    public void ReadsLatestUserTurnContextAndSkipsAutoReviewSessions()
    {
        var codexHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sessionDir = Path.Combine(codexHome, "sessions", "2026", "06", "18");
        Directory.CreateDirectory(sessionDir);

        var userPath = Path.Combine(sessionDir, "rollout-user.jsonl");
        File.WriteAllText(userPath, """
        {"timestamp":"2026-06-18T10:00:00Z","type":"session_meta","payload":{"id":"user","thread_source":"user","source":"desktop"}}
        {"timestamp":"2026-06-18T10:00:01Z","type":"turn_context","payload":{"model":"gpt-5.5","effort":"xhigh"}}
        """);

        var autoReviewPath = Path.Combine(sessionDir, "rollout-auto-review.jsonl");
        File.WriteAllText(autoReviewPath, """
        {"timestamp":"2026-06-18T10:01:00Z","type":"session_meta","payload":{"id":"auto","source":"desktop"}}
        {"timestamp":"2026-06-18T10:01:01Z","type":"turn_context","payload":{"model":"codex-auto-review","effort":"low"}}
        {"timestamp":"2026-06-18T10:01:02Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"limit_id":"codex","primary":{"used_percent":1.0,"window_minutes":300,"resets_at":1800000000}}}}
        """);
        File.SetLastWriteTimeUtc(autoReviewPath, DateTime.UtcNow.AddMinutes(1));

        var selection = CodexSessionStateReader.ReadLatest(TestEnvironment(codexHome));

        Assert.NotNull(selection);
        Assert.Equal("gpt-5.5", selection!.Model);
        Assert.Equal("xhigh", selection.ReasoningEffort);
        Assert.Equal("GPT-5.5 XHigh", selection.DisplayName);
    }

    [Fact]
    public void ReadsSessionFileWhileCodexIsStillWritingIt()
    {
        var codexHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sessionDir = Path.Combine(codexHome, "sessions", "2026", "06", "18");
        Directory.CreateDirectory(sessionDir);

        var userPath = Path.Combine(sessionDir, "rollout-user.jsonl");
        File.WriteAllText(userPath, """
        {"timestamp":"2026-06-18T10:00:00Z","type":"session_meta","payload":{"id":"user","thread_source":"user","source":"desktop"}}
        {"timestamp":"2026-06-18T10:00:01Z","type":"turn_context","payload":{"model":"gpt-5.4-mini","effort":"high"}}
        """);

        using var writerHandle = new FileStream(userPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        var selection = CodexSessionStateReader.ReadLatest(TestEnvironment(codexHome));

        Assert.NotNull(selection);
        Assert.Equal("gpt-5.4-mini", selection!.Model);
        Assert.Equal("high", selection.ReasoningEffort);
        Assert.Equal("GPT-5.4 Mini High", selection.DisplayName);
    }

    [Fact]
    public void ReadsModelFromNestedModelObject()
    {
        var codexHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sessionDir = Path.Combine(codexHome, "sessions", "2026", "06", "18");
        Directory.CreateDirectory(sessionDir);

        var userPath = Path.Combine(sessionDir, "rollout-user.jsonl");
        File.WriteAllText(userPath, """
        {"timestamp":"2026-06-18T00:59:00Z","type":"session_meta","payload":{"id":"user","thread_source":"user","source":"vscode"}}
        {"timestamp":"2026-06-18T00:59:01Z","type":"turn_context","payload":{"model":{"name":"gpt-5.4","reasoning_effort":"low"}}}
        """);

        var selection = CodexSessionStateReader.ReadLatest(TestEnvironment(codexHome));

        Assert.NotNull(selection);
        Assert.Equal("gpt-5.4", selection!.Model);
        Assert.Equal("low", selection.ReasoningEffort);
        Assert.Equal("GPT-5.4 Low", selection.DisplayName);
    }

    [Fact]
    public void ReadsModelAndReasoningFromThreadSettingsUpdated()
    {
        var codexHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sessionDir = Path.Combine(codexHome, "sessions", "2026", "06", "18");
        Directory.CreateDirectory(sessionDir);

        var userPath = Path.Combine(sessionDir, "rollout-user.jsonl");
        File.WriteAllText(userPath, """
        {"timestamp":"2026-06-18T00:59:00Z","type":"session_meta","payload":{"id":"user","thread_source":"user","source":"desktop"}}
        {"timestamp":"2026-06-18T00:59:01Z","type":"thread_settings_updated","payload":{"threadSettings":{"model":"gpt-5.3-codex-spark","effort":"xhigh","collaborationMode":{"settings":{"model":"gpt-5.3-codex-spark","reasoning_effort":"xhigh"}}}}}
        """);

        var selection = CodexSessionStateReader.ReadLatest(TestEnvironment(codexHome));

        Assert.NotNull(selection);
        Assert.Equal("gpt-5.3-codex-spark", selection!.Model);
        Assert.Equal("xhigh", selection.ReasoningEffort);
        Assert.Equal("GPT-5.3 Codex Spark XHigh", selection.DisplayName);
    }

    [Fact]
    public void ReadsServiceTierFromThreadSettingsUpdated()
    {
        var codexHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sessionDir = Path.Combine(codexHome, "sessions", "2026", "06", "18");
        Directory.CreateDirectory(sessionDir);

        var userPath = Path.Combine(sessionDir, "rollout-user.jsonl");
        File.WriteAllText(userPath, """
        {"timestamp":"2026-06-18T00:59:00Z","type":"session_meta","payload":{"id":"user","thread_source":"user","source":"desktop"}}
        {"timestamp":"2026-06-18T00:59:01Z","type":"thread_settings_updated","payload":{"threadSettings":{"model":"gpt-5.5","effort":"high","serviceTier":"fast","collaborationMode":{"settings":{"model":"gpt-5.5","reasoning_effort":"high"}}}}}
        """);

        var selection = CodexSessionStateReader.ReadLatest(TestEnvironment(codexHome));

        Assert.NotNull(selection);
        Assert.Equal("fast", selection!.ServiceTier);
        Assert.Equal("GPT-5.5 High Fast", selection.DisplayName);
    }

    [Fact]
    public void TreatsPriorityServiceTierAsFast()
    {
        var codexHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sessionDir = Path.Combine(codexHome, "sessions", "2026", "06", "18");
        Directory.CreateDirectory(sessionDir);

        var userPath = Path.Combine(sessionDir, "rollout-user.jsonl");
        File.WriteAllText(userPath, """
        {"timestamp":"2026-06-18T00:59:00Z","type":"session_meta","payload":{"id":"user","thread_source":"user","source":"desktop"}}
        {"timestamp":"2026-06-18T00:59:01Z","type":"thread_settings_updated","payload":{"threadSettings":{"model":"gpt-5.5","effort":"high","serviceTier":"priority","collaborationMode":{"settings":{"model":"gpt-5.5","reasoning_effort":"high"}}}}}
        """);

        var selection = CodexSessionStateReader.ReadLatest(TestEnvironment(codexHome));

        Assert.NotNull(selection);
        Assert.Equal("fast", selection!.ServiceTier);
        Assert.Equal("GPT-5.5 High Fast", selection.DisplayName);
    }

    [Fact]
    public void PrefersCollaborationModeReasoningFromThreadSettings()
    {
        var codexHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sessionDir = Path.Combine(codexHome, "sessions", "2026", "06", "18");
        Directory.CreateDirectory(sessionDir);

        var userPath = Path.Combine(sessionDir, "rollout-user.jsonl");
        File.WriteAllText(userPath, """
        {"timestamp":"2026-06-18T00:59:00Z","type":"session_meta","payload":{"id":"user","thread_source":"user","source":"desktop"}}
        {"timestamp":"2026-06-18T00:59:01Z","type":"thread_settings_updated","payload":{"threadSettings":{"model":"gpt-5.5","effort":"high","collaborationMode":{"settings":{"model":"gpt-5.5","reasoning_effort":"xhigh"}}}}}
        """);

        var selection = CodexSessionStateReader.ReadLatest(TestEnvironment(codexHome));

        Assert.NotNull(selection);
        Assert.Equal("gpt-5.5", selection!.Model);
        Assert.Equal("xhigh", selection.ReasoningEffort);
        Assert.Equal("GPT-5.5 XHigh", selection.DisplayName);
    }

    [Fact]
    public void ReadsRateLimitsFromSessionTokenCountEvent()
    {
        var codexHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sessionDir = Path.Combine(codexHome, "sessions", "2026", "06", "18");
        Directory.CreateDirectory(sessionDir);

        var userPath = Path.Combine(sessionDir, "rollout-user.jsonl");
        File.WriteAllText(userPath, """
        {"timestamp":"2026-06-18T00:59:00Z","type":"session_meta","payload":{"id":"user","thread_source":"user","source":"desktop"}}
        {"timestamp":"2026-06-18T00:59:01Z","type":"turn_context","payload":{"model":"gpt-5.3-codex-spark","effort":"xhigh"}}
        {"timestamp":"2026-06-18T00:59:02Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":35223,"cached_input_tokens":26880,"output_tokens":1137,"reasoning_output_tokens":569,"total_tokens":36360},"last_token_usage":{"input_tokens":18316,"cached_input_tokens":16768,"output_tokens":555,"reasoning_output_tokens":230,"total_tokens":18871},"model_context_window":258400},"rate_limits":{"limit_id":"gpt-5.3-codex-spark","limit_name":null,"primary":{"used_percent":20.0,"window_minutes":300,"resets_at":1800000000},"secondary":{"used_percent":6.0,"window_minutes":10080,"resets_at":1800100000},"plan_type":"pro"}}}
        """);

        var state = CodexSessionStateReader.ReadLatestState(TestEnvironment(codexHome));

        Assert.NotNull(state);
        Assert.Equal("gpt-5.3-codex-spark", state!.ActiveModel!.Model);
        var model = Assert.Single(state.Models);
        Assert.Equal("GPT-5.3 Codex Spark", model.ModelName);
        Assert.Equal(20, model.Current!.UsedPercent);
        Assert.Equal(6, model.Weekly!.UsedPercent);
        Assert.NotNull(state.TokenUsage);
        Assert.Equal(36360, state.TokenUsage!.Total!.TotalTokens);
        Assert.Equal(26880, state.TokenUsage.Total.CachedInputTokens);
        Assert.Equal(18871, state.TokenUsage.Last!.TotalTokens);
        Assert.Equal(258400, state.TokenUsage.ModelContextWindow);
    }

    [Fact]
    public void FallsBackToConfigDefaultsWhenSessionIsUnavailable()
    {
        var codexHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(codexHome);
        File.WriteAllText(Path.Combine(codexHome, "config.toml"), """
        model = "gpt-5.5"
        model_reasoning_effort = "high"
        """);

        var selection = CodexSessionStateReader.ReadLatest(TestEnvironment(codexHome));

        Assert.NotNull(selection);
        Assert.Equal("GPT-5.5 High", selection!.DisplayName);
    }

    [Fact]
    public void FallsBackToConfigServiceTierWhenSessionIsUnavailable()
    {
        var codexHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(codexHome);
        File.WriteAllText(Path.Combine(codexHome, "config.toml"), """
        model = "gpt-5.5"
        model_reasoning_effort = "high"
        service_tier = "fast"
        """);

        var selection = CodexSessionStateReader.ReadLatest(TestEnvironment(codexHome));

        Assert.NotNull(selection);
        Assert.Equal("fast", selection!.ServiceTier);
        Assert.Equal("GPT-5.5 High Fast", selection.DisplayName);
    }

    [Fact]
    public void MergesPriorityConfigServiceTierIntoSessionSelectionAsFast()
    {
        var codexHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(codexHome);
        File.WriteAllText(Path.Combine(codexHome, "config.toml"), """
        model = "gpt-5.5"
        model_reasoning_effort = "high"
        service_tier = "priority"
        """);
        var sessionDir = Path.Combine(codexHome, "sessions", "2026", "06", "18");
        Directory.CreateDirectory(sessionDir);

        var userPath = Path.Combine(sessionDir, "rollout-user.jsonl");
        File.WriteAllText(userPath, """
        {"timestamp":"2026-06-18T00:59:00Z","type":"session_meta","payload":{"id":"user","thread_source":"user","source":"desktop"}}
        {"timestamp":"2026-06-18T00:59:01Z","type":"thread_settings_updated","payload":{"threadSettings":{"model":"gpt-5.5","effort":"high","collaborationMode":{"settings":{"model":"gpt-5.5","reasoning_effort":"high"}}}}}
        """);

        var selection = CodexSessionStateReader.ReadLatest(TestEnvironment(codexHome));

        Assert.NotNull(selection);
        Assert.Equal("fast", selection!.ServiceTier);
        Assert.Equal("GPT-5.5 High Fast", selection.DisplayName);
    }

    [Fact]
    public async Task CodexCliFetchDoesNotFallBackToSessionUsageWhenRpcFails()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var binDir = Path.Combine(testRoot, "bin");
        var codexHome = Path.Combine(testRoot, "codex-home");
        var sessionDir = Path.Combine(codexHome, "sessions", "2026", "06", "18");
        Directory.CreateDirectory(binDir);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(binDir, "codex.cmd"), "@echo off\r\n");
        File.WriteAllText(Path.Combine(sessionDir, "rollout-user.jsonl"), """
        {"timestamp":"2026-06-18T00:59:00Z","type":"session_meta","payload":{"id":"user","thread_source":"user","source":"desktop"}}
        {"timestamp":"2026-06-18T00:59:01Z","type":"turn_context","payload":{"model":"gpt-5.3-codex-spark","effort":"xhigh"}}
        {"timestamp":"2026-06-18T00:59:02Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":1000,"output_tokens":200,"total_tokens":1200},"last_token_usage":{"input_tokens":900,"output_tokens":100,"total_tokens":1000},"model_context_window":258400},"rate_limits":{"limit_id":"gpt-5.3-codex-spark","primary":{"used_percent":20.0,"window_minutes":300,"resets_at":1800000000}}}}
        """);

        var strategy = new CodexCliFetchStrategy(new QueueCodexRpcTransportFactory([Array.Empty<string>()]));
        var context = new ProviderFetchContext(
            UsageProvider.Codex,
            new Dictionary<string, string>
            {
                ["PATH"] = binDir,
                ["PATHEXT"] = ".CMD",
                ["CODEX_HOME"] = codexHome
            },
            IncludeCredits: true,
            InitializeTimeout: TimeSpan.FromMilliseconds(20),
            RequestTimeout: TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAsync<CodexRpcTimeoutException>(() => strategy.FetchAsync(context, CancellationToken.None));
    }

    private static IReadOnlyDictionary<string, string> TestEnvironment(string codexHome) => new Dictionary<string, string>
    {
        ["CODEX_HOME"] = codexHome
    };
}

public sealed class UsageStoreTests
{
    [Fact]
    public async Task ManualRefreshUpdatesSnapshot()
    {
        var settings = TestSettings();
        var descriptor = TestDescriptor(FetchResult(10, 55));
        var store = new UsageStore(settings, descriptor);

        await store.RefreshAsync(CancellationToken.None);

        Assert.Null(store.LastError);
        Assert.Equal(90, store.Snapshot!.Primary!.RemainingPercent);
        Assert.Equal(55, store.Credits!.Remaining);
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
        settings.UpdateCodex(c => c.Enabled = false);
        var store = new UsageStore(settings, CodexProviderDescriptor.Create(new QueueCodexRpcTransportFactory(Array.Empty<string[]>())));

        await store.RefreshAsync(CancellationToken.None);

        Assert.Null(store.Snapshot);
        Assert.Null(store.LastError);
    }

    [Fact]
    public async Task RefreshTracksResetCreditIncreaseFromLocalObservation()
    {
        var firstObservation = DateTimeOffset.Parse("2026-06-29T12:00:00+09:00");
        var secondObservation = DateTimeOffset.Parse("2026-06-30T12:00:00+09:00");
        var settings = TestSettings();
        var descriptor = TestDescriptor(
            FetchResultWithResetCredits(1, firstObservation),
            FetchResultWithResetCredits(2, secondObservation));
        var store = new UsageStore(
            settings,
            descriptor,
            new RateLimitResetCreditTracker(new InMemoryRateLimitResetCreditStateStore()));

        await store.RefreshAsync(CancellationToken.None);

        Assert.Equal(1, store.Snapshot!.RateLimitResetCredits!.UnknownExpirationCount);
        Assert.Null(store.Snapshot.RateLimitResetCredits.NextEstimatedExpiresAt);

        await store.RefreshAsync(CancellationToken.None);

        var resetCredits = store.Snapshot!.RateLimitResetCredits!;
        Assert.Equal(2, resetCredits.AvailableCount);
        Assert.Equal(1, resetCredits.UnknownExpirationCount);
        Assert.Equal(secondObservation.AddDays(30), resetCredits.NextEstimatedExpiresAt);
    }

    private static SettingsStore TestSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "config.json");
        return new SettingsStore(new WindexBarConfigStore(path));
    }

    private static ProviderDescriptor TestDescriptor(params object[] outcomes) => new(
        UsageProvider.Codex,
        "Codex",
        "Session",
        "Weekly",
        "codex",
        true,
        new ProviderFetchPipeline([new QueueProviderFetchStrategy(outcomes)]));

    private static ProviderFetchResult FetchResult(double usedPercent, double creditsRemaining)
    {
        var now = DateTimeOffset.UnixEpoch;
        var usage = new UsageSnapshot(
            new RateWindow(usedPercent, 300, DateTimeOffset.FromUnixTimeSeconds(1_800_000_000), "in 1h"),
            null,
            null,
            now,
            new ProviderIdentitySnapshot(UsageProvider.Codex, "me@example.com", null, "plus"));
        var credits = new CreditsSnapshot(creditsRemaining, Array.Empty<CreditEvent>(), now);
        return new ProviderFetchResult(usage, credits, "test", "test", ProviderFetchKind.LocalProbe);
    }

    private static ProviderFetchResult FetchResultWithResetCredits(long resetCreditCount, DateTimeOffset now)
    {
        var usage = new UsageSnapshot(
            new RateWindow(10, 300, DateTimeOffset.FromUnixTimeSeconds(1_800_000_000), "in 1h"),
            null,
            null,
            now,
            new ProviderIdentitySnapshot(UsageProvider.Codex, "me@example.com", null, "plus"),
            RateLimitResetCredits: new RateLimitResetCreditsSnapshot(resetCreditCount, now));
        var credits = new CreditsSnapshot(55, Array.Empty<CreditEvent>(), now);
        return new ProviderFetchResult(usage, credits, "test", "test", ProviderFetchKind.LocalProbe);
    }
}

public sealed class InstallerBuildScriptTests
{
    [Fact]
    public void PublishUsesSizeOptimizedReleaseOptions()
    {
        var script = File.ReadAllText(FindRepositoryFile("build-installer.cmd"));

        Assert.Contains("-p:PublishTrimmed=true", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-p:PublishReadyToRun=false", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-p:DebugType=None", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-p:DebugSymbols=false", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-p:ILLinkTreatWarningsAsErrors=false", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConsoleInstallSilencesMissingCertificateWarningDuringOptionalSigning()
    {
        var installScript = File.ReadAllText(FindRepositoryFile(Path.Combine("scripts", "install-console.ps1")));
        var signScript = File.ReadAllText(FindRepositoryFile(Path.Combine("scripts", "sign-app.ps1")));

        Assert.Contains("-QuietMissingCertificate", installScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[switch]$QuietMissingCertificate", signScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("if ($QuietMissingCertificate)", signScript, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartupShortcutCreationAvoidsReflectionActivatorForTrimmedPublish()
    {
        var service = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "WindexBar.Windows", "StartupShortcutService.cs")));

        Assert.DoesNotContain("Type.GetTypeFromCLSID", service, StringComparison.Ordinal);
        Assert.DoesNotContain("Activator.CreateInstance", service, StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(string fileName, [CallerFilePath] string sourceFilePath = "")
    {
        foreach (var start in new[] { Path.GetDirectoryName(sourceFilePath), Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            if (string.IsNullOrWhiteSpace(start))
            {
                continue;
            }

            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var path = Path.Combine(directory.FullName, fileName);
                if (File.Exists(path))
                {
                    return path;
                }

                directory = directory.Parent;
            }
        }

        throw new FileNotFoundException($"Could not find repository file `{fileName}`.");
    }
}

public sealed class TrayIconServiceTests
{
    [Fact]
    public void SidebarHotkeyDoesNotShowHiddenWindow()
    {
        var service = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "WindexBar.Windows", "TrayIconService.cs")));
        var toggleSidebarBody = ExtractMethodBody(service, "private void ToggleSidebar()");

        Assert.Contains("WindowCloseBehavior.IsVisible(window)", toggleSidebarBody, StringComparison.Ordinal);
        Assert.Matches(
            new Regex(
                "WindowCloseBehavior\\.IsVisible\\(window\\).*window\\.ToggleSideBar\\(\\).*WindowCloseBehavior\\.Show\\(window\\)",
                RegexOptions.Singleline),
            toggleSidebarBody);
    }

    private static string ExtractMethodBody(string source, string signature)
    {
        var signatureIndex = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(signatureIndex >= 0, $"Could not find method signature: {signature}");

        var bodyStart = source.IndexOf('{', signatureIndex);
        Assert.True(bodyStart >= 0, $"Could not find method body for: {signature}");

        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[bodyStart..(index + 1)];
                }
            }
        }

        throw new InvalidOperationException($"Could not parse method body for: {signature}");
    }

    private static string FindRepositoryFile(string relativePath, [CallerFilePath] string sourceFilePath = "")
    {
        foreach (var start in new[] { Path.GetDirectoryName(sourceFilePath), Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = start;
            while (!string.IsNullOrWhiteSpace(directory))
            {
                var candidate = Path.Combine(directory, relativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = Directory.GetParent(directory)?.FullName;
            }
        }

        throw new FileNotFoundException($"Could not find {relativePath} from {sourceFilePath}");
    }
}

public sealed class ReleaseWorkflowTests
{
    [Fact]
    public void ReleaseVersionPatternAllowsMinorTags()
    {
        var workflow = File.ReadAllText(FindRepositoryFile(Path.Combine(".github", "workflows", "release.yml")));
        var match = Regex.Match(workflow, "\\$version -notmatch '([^']+)'");

        Assert.True(match.Success);
        var versionPattern = match.Groups[1].Value;
        Assert.Matches(versionPattern, "1.1");
        Assert.Matches(versionPattern, "1.1.0");
        Assert.DoesNotMatch(versionPattern, "1");
    }

    [Fact]
    public void ReleaseWorkflowRemovesGeneratedFullChangelogSection()
    {
        var workflow = File.ReadAllText(FindRepositoryFile(Path.Combine(".github", "workflows", "release.yml")));

        Assert.DoesNotContain("--generate-notes", workflow, StringComparison.Ordinal);
        Assert.Contains("releases/generate-notes", workflow, StringComparison.Ordinal);
        Assert.Contains("Full Changelog", workflow, StringComparison.Ordinal);
        Assert.Contains("--notes-file", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseWorkflowGroupsGeneratedNotesByChangeType()
    {
        var workflow = File.ReadAllText(FindRepositoryFile(Path.Combine(".github", "workflows", "release.yml")));

        Assert.Contains("function Convert-ReleaseNotesSections", workflow, StringComparison.Ordinal);
        Assert.Contains("Added:", workflow, StringComparison.Ordinal);
        Assert.Contains("Hotfix:", workflow, StringComparison.Ordinal);
        Assert.Contains("Get-ReleaseNoteSection", workflow, StringComparison.Ordinal);
        Assert.Contains("$generatedNotes = @(gh api", workflow, StringComparison.Ordinal);
        Assert.Contains("$body = $generatedNotes -join [Environment]::NewLine", workflow, StringComparison.Ordinal);
        Assert.Contains("Convert-ReleaseNotesSections $body", workflow, StringComparison.Ordinal);
        Assert.Contains("\\b(hotfix|bug|crash|warning|blocked|failure|error)\\b", workflow, StringComparison.Ordinal);
        Assert.Contains("[void]$Output.Add(\"- $item\")", workflow, StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(string relativePath, [CallerFilePath] string sourceFilePath = "")
    {
        foreach (var start in new[] { Path.GetDirectoryName(sourceFilePath), Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            if (string.IsNullOrWhiteSpace(start))
            {
                continue;
            }

            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var path = Path.Combine(directory.FullName, relativePath);
                if (File.Exists(path))
                {
                    return path;
                }

                directory = directory.Parent;
            }
        }

        throw new FileNotFoundException($"Could not find repository file `{relativePath}`.");
    }
}

internal sealed class QueueProviderFetchStrategy : IProviderFetchStrategy
{
    private readonly Queue<object> _outcomes;

    public QueueProviderFetchStrategy(IEnumerable<object> outcomes)
    {
        _outcomes = new Queue<object>(outcomes);
    }

    public string Id => "test";
    public ProviderFetchKind Kind => ProviderFetchKind.LocalProbe;

    public Task<bool> IsAvailableAsync(ProviderFetchContext context, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<ProviderFetchResult> FetchAsync(ProviderFetchContext context, CancellationToken cancellationToken)
    {
        var outcome = _outcomes.Dequeue();
        if (outcome is Exception error)
        {
            throw error;
        }

        return Task.FromResult((ProviderFetchResult)outcome);
    }

    public bool ShouldFallback(Exception error, ProviderFetchContext context) => false;
}

internal sealed class InMemoryRateLimitResetCreditStateStore : IRateLimitResetCreditStateStore
{
    private RateLimitResetCreditState _state = new();

    public RateLimitResetCreditState Load() => _state;

    public void Save(RateLimitResetCreditState state)
    {
        _state = state;
    }
}

internal sealed class QueueCodexRpcTransportFactory : ICodexRpcTransportFactory
{
    private readonly Queue<string[]> _sessions;

    public QueueCodexRpcTransportFactory(IEnumerable<string[]> sessions)
    {
        _sessions = new Queue<string[]>(sessions);
    }

    public ICodexRpcTransport Start(string executablePath, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment)
    {
        return new FakeCodexRpcTransport(_sessions.Count > 0 ? _sessions.Dequeue() : Array.Empty<string>());
    }
}

internal sealed class FakeCodexRpcTransport : ICodexRpcTransport
{
    private readonly Queue<string> _replies;

    public FakeCodexRpcTransport(params string[] replies)
    {
        _replies = new Queue<string>(replies);
    }

    public List<string> Writes { get; } = [];
    public bool Killed { get; private set; }

    public Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        Writes.Add(line);
        return Task.CompletedTask;
    }

    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        if (_replies.Count > 0)
        {
            return _replies.Dequeue();
        }

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return null;
    }

    public void Kill() => Killed = true;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
