using WindexBar.Core.Config;
using WindexBar.Core.Models;
using WindexBar.Core.Formatting;
using WindexBar.Core.Providers;
using WindexBar.Core.Providers.Codex;
using WindexBar.Core.Refresh;
using WindexBar.Core.Windowing;
using System.Globalization;
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
                rateLimitResetCredits = new
                {
                    availableCount = 2,
                    credits = new[]
                    {
                        new
                        {
                            id = "reset-1",
                            grantedAt = 1_751_234_567L,
                            expiresAt = 1_753_826_567L,
                            resetType = "codexRateLimits",
                            status = "available",
                            title = "Referral reset",
                            description = "Banked reset"
                        }
                    }
                }
            }),
            OnRequest(3, new { account = new { type = "chatgpt", email = "me@example.com", planType = "team" } }),
            OnRequest(4, new
            {
                data = new[]
                {
                    new
                    {
                        id = "session-1",
                        name = "Implement session usage",
                        preview = "Session usage preview",
                        cwd = "D:\\Codes\\WindexBar",
                        createdAt = 1_800_000_000L,
                        updatedAt = 1_800_100_000L
                    }
                },
                nextCursor = (string?)null
            }));

        await using var client = new CodexRpcClient(transport, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        await client.InitializeAsync(CancellationToken.None);
        var limits = await client.FetchRateLimitsAsync(CancellationToken.None);
        var account = await client.FetchAccountAsync(CancellationToken.None);
        var threads = await client.FetchThreadsAsync(CancellationToken.None);
        var usage = CodexUsageMapper.MapUsage(limits, account, DateTimeOffset.UnixEpoch)!;
        var credits = CodexUsageMapper.MapCredits(limits.RateLimits.Credits, DateTimeOffset.UnixEpoch)!;

        Assert.Equal(25, usage.Primary!.UsedPercent);
        Assert.Equal(75, usage.Primary.RemainingPercent);
        Assert.Equal(300, usage.Primary.WindowMinutes);
        Assert.Equal("me@example.com", usage.Identity!.AccountEmail);
        Assert.Equal("team", usage.Identity.LoginMethod);
        Assert.Equal(2, usage.RateLimitResetCredits!.AvailableCount);
        var resetCredit = Assert.Single(limits.RateLimitResetCredits!.Credits!);
        Assert.Equal("reset-1", resetCredit.Id);
        Assert.Equal(1_751_234_567L, resetCredit.GrantedAt);
        Assert.Equal(1_753_826_567L, resetCredit.ExpiresAt);
        Assert.Equal("codexRateLimits", resetCredit.ResetType);
        Assert.Equal("available", resetCredit.Status);
        Assert.Equal("Referral reset", resetCredit.Title);
        Assert.Equal("Banked reset", resetCredit.Description);
        Assert.Equal(123.5, credits.Remaining, 3);
        var thread = Assert.Single(threads.Data);
        Assert.Equal("session-1", thread.Id);
        Assert.Equal("Implement session usage", thread.Name);
        Assert.Equal("Session usage preview", thread.Preview);
        Assert.Equal("D:\\Codes\\WindexBar", thread.Cwd);
        Assert.Contains("initialized", transport.Writes[1], StringComparison.Ordinal);
        Assert.Contains("\"useStateDbOnly\":true", transport.Writes[4], StringComparison.Ordinal);
        Assert.Contains("\"sortKey\":\"updated_at\"", transport.Writes[4], StringComparison.Ordinal);
        Assert.DoesNotContain("sourceKinds", transport.Writes[4], StringComparison.Ordinal);
    }

    [Fact]
    public async Task FetchesEveryThreadListPage()
    {
        var transport = new FakeCodexRpcTransport(
            OnRequest(1, new { ok = true }),
            OnRequest(2, new
            {
                data = new[] { new { id = "session-1" } },
                nextCursor = "next-page"
            }),
            OnRequest(3, new
            {
                data = new[] { new { id = "session-2" } },
                nextCursor = (string?)null
            }));

        await using var client = new CodexRpcClient(transport, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        await client.InitializeAsync(CancellationToken.None);

        var threads = await client.FetchThreadsAsync(CancellationToken.None);

        Assert.Equal(["session-1", "session-2"], threads.Data.Select(thread => thread.Id));
        Assert.Contains("\"cursor\":\"next-page\"", transport.Writes[3], StringComparison.Ordinal);
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
    public void MapsExactRateLimitResetCreditDetails()
    {
        var grantedAt = 1_751_234_567L;
        var expiresAt = 1_753_826_567L;
        var response = JsonSerializer.Deserialize<RpcRateLimitsResponse>(JsonSerializer.Serialize(new
        {
            rateLimits = new { primary = new { usedPercent = 12.0 } },
            rateLimitResetCredits = new
            {
                availableCount = 2L,
                credits = new object[]
                {
                    new
                    {
                        id = "reset-1",
                        grantedAt,
                        expiresAt,
                        resetType = "codexRateLimits",
                        status = "available",
                        title = "Referral reset",
                        description = "Banked reset"
                    },
                    new
                    {
                        id = "reset-2",
                        grantedAt,
                        expiresAt = (long?)null,
                        resetType = "codexRateLimits",
                        status = "available",
                        title = (string?)null,
                        description = (string?)null
                    }
                }
            }
        }))!;

        var snapshot = CodexUsageMapper.MapUsage(response, null, DateTimeOffset.UnixEpoch)!.RateLimitResetCredits!;

        Assert.Equal(2, snapshot.AvailableCount);
        Assert.Equal(2, snapshot.Credits.Count);
        var exact = snapshot.Credits[0];
        Assert.Equal("reset-1", exact.Id);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(grantedAt), exact.GrantedAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(expiresAt), exact.ExpiresAt);
        Assert.Equal("codexRateLimits", exact.ResetType);
        Assert.Equal("available", exact.Status);
        Assert.Equal(1, snapshot.UnavailableExpirationCount);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(expiresAt), snapshot.NextExpiresAt);
    }

    [Fact]
    public void MissingOrInvalidResetCreditDetailsRemainUnavailable()
    {
        var response = new RpcRateLimitResetCreditsSummary
        {
            AvailableCount = 2,
            Credits =
            [
                new RpcRateLimitResetCredit
                {
                    Id = "invalid",
                    GrantedAt = long.MaxValue,
                    ExpiresAt = 1_753_826_567L,
                    ResetType = "codexRateLimits",
                    Status = "available"
                }
            ]
        };

        var snapshot = CodexUsageMapper.MapRateLimitResetCredits(response, DateTimeOffset.UnixEpoch)!;

        Assert.Empty(snapshot.Credits);
        Assert.Equal(2, snapshot.UnavailableExpirationCount);
        Assert.Null(snapshot.NextExpiresAt);
    }

    [Fact]
    public void CapsResetCreditDetailsToAvailableCountUsingEarliestExpiration()
    {
        var grantedAt = 1_751_234_567L;
        var earlierExpiry = 1_753_826_567L;
        var laterExpiry = 1_753_900_000L;
        var response = new RpcRateLimitResetCreditsSummary
        {
            AvailableCount = 1,
            Credits =
            [
                new RpcRateLimitResetCredit
                {
                    Id = "later",
                    GrantedAt = grantedAt,
                    ExpiresAt = laterExpiry,
                    ResetType = "codexRateLimits",
                    Status = "available"
                },
                new RpcRateLimitResetCredit
                {
                    Id = "earlier",
                    GrantedAt = grantedAt,
                    ExpiresAt = earlierExpiry,
                    ResetType = "codexRateLimits",
                    Status = "available"
                }
            ]
        };

        var snapshot = CodexUsageMapper.MapRateLimitResetCredits(response, DateTimeOffset.UnixEpoch)!;

        var credit = Assert.Single(snapshot.Credits);
        Assert.Equal("earlier", credit.Id);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(earlierExpiry), snapshot.NextExpiresAt);
        Assert.Equal(0, snapshot.UnavailableExpirationCount);
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
    public void MapsWeeklyOnlyPrimaryWindowByDuration()
    {
        var response = JsonSerializer.Deserialize<RpcRateLimitsResponse>(JsonSerializer.Serialize(new
        {
            rateLimits = new
            {
                limitId = "codex",
                primary = new { usedPercent = 0.0, windowDurationMins = 10080, resetsAt = 1_800_100_000L },
                secondary = (object?)null,
                planType = "pro"
            },
            rateLimitsByLimitId = new Dictionary<string, object?>
            {
                ["codex"] = new
                {
                    limitId = "codex",
                    primary = new { usedPercent = 0.0, windowDurationMins = 10080, resetsAt = 1_800_100_000L },
                    secondary = (object?)null,
                    planType = "pro"
                }
            }
        }))!;

        var usage = CodexUsageMapper.MapUsage(response, null, DateTimeOffset.UnixEpoch)!;

        Assert.Null(usage.Primary);
        Assert.Equal(0, usage.Secondary!.UsedPercent);
        Assert.Equal(10080, usage.Secondary.WindowMinutes);
        var model = Assert.Single(usage.Models!);
        Assert.Null(model.Current);
        Assert.Equal(0, model.Weekly!.UsedPercent);
        Assert.Equal(10080, model.Weekly.WindowMinutes);
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
    public void GroupsGpt56MaxAndUltraReasoningBucketsByModelVersion()
    {
        var response = JsonSerializer.Deserialize<RpcRateLimitsResponse>(JsonSerializer.Serialize(new
        {
            rateLimits = new { },
            rateLimitsByLimitId = new Dictionary<string, object?>
            {
                ["gpt-5.6-sol-ultra"] = new
                {
                    limitId = "gpt-5.6-sol-ultra",
                    limitName = "GPT-5.6-Sol Ultra Reasoning",
                    primary = new { usedPercent = 10.0, windowDurationMins = 300, resetsAt = 1_800_000_000L }
                },
                ["gpt-5.6-sol-low"] = new
                {
                    limitId = "gpt-5.6-sol-low",
                    limitName = "GPT-5.6-Sol Low Reasoning",
                    secondary = new { usedPercent = 20.0, windowDurationMins = 10080, resetsAt = 1_800_100_000L }
                },
                ["gpt-5.6-terra-max"] = new
                {
                    limitId = "gpt-5.6-terra-max",
                    limitName = "GPT-5.6-Terra Max Reasoning",
                    primary = new { usedPercent = 30.0, windowDurationMins = 300, resetsAt = 1_800_200_000L }
                },
                ["gpt-5.6-terra-low"] = new
                {
                    limitId = "gpt-5.6-terra-low",
                    limitName = "GPT-5.6-Terra Low Reasoning",
                    secondary = new { usedPercent = 40.0, windowDurationMins = 10080, resetsAt = 1_800_300_000L }
                }
            }
        }))!;

        var usage = CodexUsageMapper.MapUsage(response, null, DateTimeOffset.UnixEpoch)!;

        Assert.Equal(2, usage.Models!.Count);
        Assert.Equal("GPT-5.6 Sol Ultra", usage.Models[0].ModelName);
        Assert.Equal(10, usage.Models[0].Current!.UsedPercent);
        Assert.Equal(20, usage.Models[0].Weekly!.UsedPercent);
        Assert.Equal("GPT-5.6 Terra Max", usage.Models[1].ModelName);
        Assert.Equal(30, usage.Models[1].Current!.UsedPercent);
        Assert.Equal(40, usage.Models[1].Weekly!.UsedPercent);
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
        Assert.Equal(WindexBarConfig.DefaultLanguage, reloaded.Language);
        Assert.Equal(WindexBarConfig.DefaultToggleWindowHotkey, reloaded.Hotkeys.ToggleWindow);
        Assert.Equal(WindexBarConfig.DefaultToggleSidebarHotkey, reloaded.Hotkeys.ToggleSidebar);
        Assert.True(reloaded.StartWithWindows);
        Assert.False(reloaded.AutoShowWithCodex);
        Assert.Equal(StyleConfig.DefaultGaugeThickness, reloaded.Style.GaugeThickness);
        Assert.Equal(StyleConfig.DefaultGaugeColor, reloaded.Style.GaugeColor);
        Assert.Equal(StyleConfig.DefaultGaugeAnimation, reloaded.Style.GaugeAnimation);
    }

    [Fact]
    public void PreservesAndNormalizesGaugeStylePreferences()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "config.json");
        var store = new WindexBarConfigStore(path);
        var config = store.LoadOrCreateDefault();
        config.Style.GaugeThickness = "THICK";
        config.Style.GaugeColor = "#4f9dff";
        config.Style.GaugeAnimation = "off";
        store.Save(config);

        var reloaded = store.LoadOrCreateDefault();

        Assert.Equal("thick", reloaded.Style.GaugeThickness);
        Assert.Equal("#4F9DFF", reloaded.Style.GaugeColor);
        Assert.Equal("off", reloaded.Style.GaugeAnimation);
    }

    [Fact]
    public void InvalidGaugeStylePreferencesFallBackToDefaults()
    {
        var config = new StyleConfig
        {
            GaugeThickness = "huge",
            GaugeColor = "not-a-color",
            GaugeAnimation = "bounce"
        };

        config.Normalized();

        Assert.Equal(StyleConfig.DefaultGaugeThickness, config.GaugeThickness);
        Assert.Equal(StyleConfig.DefaultGaugeColor, config.GaugeColor);
        Assert.Equal(StyleConfig.DefaultGaugeAnimation, config.GaugeAnimation);
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
    public void PreservesSavedAutoShowWithCodex()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "config.json");
        var store = new WindexBarConfigStore(path);
        var config = store.LoadOrCreateDefault();
        config.AutoShowWithCodex = true;
        store.Save(config);

        var reloaded = store.LoadOrCreateDefault();

        Assert.True(reloaded.AutoShowWithCodex);
    }

    [Fact]
    public void PreservesCodexUpdatePreferencesAndCache()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "config.json");
        var store = new WindexBarConfigStore(path);
        var config = store.LoadOrCreateDefault();
        var checkedAt = new DateTimeOffset(2026, 7, 17, 1, 2, 3, TimeSpan.Zero);
        config.CodexUpdates.InstallMethod = CodexInstallMethodNames.Bun;
        config.CodexUpdates.AutomaticallyUpdate = true;
        config.CodexUpdates.CustomCommand = "custom-update {latestVersion}";
        config.CodexUpdates.LatestVersion = "0.144.5";
        config.CodexUpdates.LastCheckedAt = checkedAt;
        store.Save(config);

        var reloaded = store.LoadOrCreateDefault();

        Assert.Equal(CodexInstallMethodNames.Bun, reloaded.CodexUpdates.InstallMethod);
        Assert.True(reloaded.CodexUpdates.AutomaticallyUpdate);
        Assert.Equal("custom-update {latestVersion}", reloaded.CodexUpdates.CustomCommand);
        Assert.Equal("0.144.5", reloaded.CodexUpdates.LatestVersion);
        Assert.Equal(checkedAt, reloaded.CodexUpdates.LastCheckedAt);
    }

    [Fact]
    public void CodexAutomaticUpdatesAreAlwaysEnabled()
    {
        var config = new CodexUpdateConfig { AutomaticallyUpdate = false };

        config.Normalized();

        Assert.True(config.AutomaticallyUpdate);
        Assert.True(new CodexUpdateConfig().AutomaticallyUpdate);
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

public sealed class CodexActivityWindowMatcherTests
{
    [Theory]
    [InlineData("ChatGPT")]
    [InlineData("ChatGPT.exe")]
    public void MatchesChatGptDesktopProcess(string processName)
    {
        var window = new CodexActivityWindowSnapshot(processName, "ChatGPT", []);

        Assert.True(CodexActivityWindowMatcher.IsCodexActivity(window));
    }

    [Fact]
    public void MatchesCodexDesktopProcess()
    {
        var window = new CodexActivityWindowSnapshot("Codex", "Codex", []);

        Assert.True(CodexActivityWindowMatcher.IsCodexActivity(window));
    }

    [Fact]
    public void MatchesTerminalWithCodexDescendant()
    {
        var window = new CodexActivityWindowSnapshot("WindowsTerminal", "PowerShell", ["pwsh", "codex"]);

        Assert.True(CodexActivityWindowMatcher.IsCodexActivity(window));
    }

    [Fact]
    public void MatchesTerminalWhenCodexRunsOutsideWindowProcessTree()
    {
        var window = new CodexActivityWindowSnapshot("WindowsTerminal", "PowerShell", [], HasTerminalCodexProcess: true);

        Assert.True(CodexActivityWindowMatcher.IsCodexActivity(window));
    }

    [Fact]
    public void FindsCodexCliWithTerminalShellAncestor()
    {
        CodexActivityProcessSnapshot[] processes =
        [
            new(10, 1, "cmd.exe"),
            new(11, 10, "node.exe"),
            new(12, 11, "codex.exe")
        ];

        Assert.True(CodexActivityWindowMatcher.HasTerminalCodexProcess(processes));
    }

    [Fact]
    public void IgnoresCodexProcessOwnedByDesktopApp()
    {
        CodexActivityProcessSnapshot[] processes =
        [
            new(20, 1, "ChatGPT.exe"),
            new(21, 20, "codex.exe")
        ];

        Assert.False(CodexActivityWindowMatcher.HasTerminalCodexProcess(processes));
    }

    [Fact]
    public void MatchesTerminalTitleFallback()
    {
        var window = new CodexActivityWindowSnapshot("pwsh", "codex app-server", []);

        Assert.True(CodexActivityWindowMatcher.IsCodexActivity(window));
    }

    [Fact]
    public void DoesNotMatchBrowserTitleFallback()
    {
        var window = new CodexActivityWindowSnapshot("chrome", "Codex docs", []);

        Assert.False(CodexActivityWindowMatcher.IsCodexActivity(window));
    }

    [Fact]
    public void DoesNotMatchChatGptBrowserTitleFallback()
    {
        var window = new CodexActivityWindowSnapshot("chrome", "ChatGPT", []);

        Assert.False(CodexActivityWindowMatcher.IsCodexActivity(window));
    }

    [Theory]
    [InlineData("WindexBar.Windows")]
    [InlineData("WindexBar")]
    public void IdentifiesOwnWindexBarWindow(string processName)
    {
        var window = new CodexActivityWindowSnapshot(processName, "WindexBar", []);

        Assert.True(CodexActivityWindowMatcher.IsWindexBarWindow(window));
        Assert.False(CodexActivityWindowMatcher.IsCodexActivity(window));
    }
}

public sealed class AutoVisibilityPolicyTests
{
    [Theory]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, false, true)]
    public void OnlyShowsForEnabledCodexActivityWhenUserDidNotHide(bool enabled, bool codexActivity, bool userHidden, bool expected)
    {
        Assert.Equal(expected, AutoVisibilityPolicy.ShouldShow(enabled, codexActivity, userHidden));
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    public void PreservesOwnWindowFocusOnlyWhilePreviousCodexWindowRemainsAvailable(
        bool hasPreviousCodexWindow,
        bool previousCodexWindowVisible,
        bool previousCodexWindowMinimized,
        bool expected)
    {
        Assert.Equal(
            expected,
            AutoVisibilityPolicy.ShouldPreserveWhileOwnWindowFocused(
                hasPreviousCodexWindow,
                previousCodexWindowVisible,
                previousCodexWindowMinimized));
    }

    [Fact]
    public void KeepsWindowVisibleThroughOneTransientInactiveSample()
    {
        var filter = new AutoVisibilityStabilityFilter(inactiveSamplesBeforeHide: 2);

        Assert.True(filter.ShouldTreatAsActive(true));
        Assert.True(filter.ShouldTreatAsActive(false));
        Assert.False(filter.ShouldTreatAsActive(false));
    }

    [Fact]
    public void DoesNotTreatInitialInactiveStateAsActive()
    {
        var filter = new AutoVisibilityStabilityFilter(inactiveSamplesBeforeHide: 2);

        Assert.False(filter.ShouldTreatAsActive(false));
    }
}

public sealed class RateLimitResetCreditFormatterTests
{
    [Fact]
    public void DirectSnapshotCapsDetailsToAuthoritativeAvailableCount()
    {
        var now = DateTimeOffset.Parse("2026-07-10T12:00:00+09:00");
        var earlierExpiry = DateTimeOffset.Parse("2026-08-01T05:05:10+09:00");
        var laterExpiry = DateTimeOffset.Parse("2026-08-03T06:06:00+09:00");
        var snapshot = new RateLimitResetCreditsSnapshot(
            1,
            now,
            [
                new RateLimitResetCredit("later", now.AddDays(-7), laterExpiry, "codexRateLimits", "available", null, null),
                new RateLimitResetCredit("earlier", now.AddDays(-8), earlierExpiry, "codexRateLimits", "available", null, null)
            ]);

        var credit = Assert.Single(snapshot.Credits);
        Assert.Equal("earlier", credit.Id);
        Assert.Equal(earlierExpiry, snapshot.NextExpiresAt);
        var localExpiry = earlierExpiry.ToLocalTime().ToString("yy.M.dd H:mm", CultureInfo.InvariantCulture);
        Assert.Equal($"Expires {localExpiry}: 1", RateLimitResetCreditFormatter.FormatDetail(snapshot, "en", now));
    }

    [Fact]
    public void FormatsExactResetCreditExpirationsAndUnavailableCount()
    {
        var now = DateTimeOffset.Parse("2026-07-10T12:00:00+09:00");
        var earliestExpiry = DateTimeOffset.Parse("2026-08-01T05:05:10+09:00");
        var sameMinuteExpiry = DateTimeOffset.Parse("2026-08-01T05:05:47+09:00");
        var laterExpiry = DateTimeOffset.Parse("2026-08-03T06:06:00+09:00");
        var snapshot = new RateLimitResetCreditsSnapshot(
            5,
            now,
            [
                new RateLimitResetCredit("later", now.AddDays(-8), laterExpiry, "codexRateLimits", "available", null, null),
                new RateLimitResetCredit("a", now.AddDays(-8), earliestExpiry, "codexRateLimits", "available", null, null),
                new RateLimitResetCredit("missing", now.AddDays(-8), null, "codexRateLimits", "available", null, null),
                new RateLimitResetCredit("b", now.AddDays(-8), sameMinuteExpiry, "codexRateLimits", "available", null, null)
            ]);

        Assert.Equal("5\uAC1C \uBCF4\uC720" + Environment.NewLine + "\uCCAB \uB9CC\uB8CC D-22", RateLimitResetCreditFormatter.FormatSummary(snapshot, "ko", now));
        Assert.Equal("5 held" + Environment.NewLine + "First expiry D-22", RateLimitResetCreditFormatter.FormatSummary(snapshot, "en", now));
        var earliestLocalExpiry = earliestExpiry.ToLocalTime().ToString("yy.M.dd H:mm", CultureInfo.InvariantCulture);
        var laterLocalExpiry = laterExpiry.ToLocalTime().ToString("yy.M.dd H:mm", CultureInfo.InvariantCulture);

        Assert.Equal(
            $"{earliestLocalExpiry} \uB9CC\uB8CC: 2\uAC1C" + Environment.NewLine
            + $"{laterLocalExpiry} \uB9CC\uB8CC: 1\uAC1C" + Environment.NewLine
            + "\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500" + Environment.NewLine
            + "\uB9CC\uB8CC \uC815\uBCF4 \uBBF8\uC81C\uACF5: 2\uAC1C",
            RateLimitResetCreditFormatter.FormatDetail(snapshot, "ko", now));
        Assert.Equal(
            $"Expires {earliestLocalExpiry}: 2" + Environment.NewLine
            + $"Expires {laterLocalExpiry}: 1" + Environment.NewLine
            + "\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500" + Environment.NewLine
            + "Expiration unavailable: 2",
            RateLimitResetCreditFormatter.FormatDetail(snapshot, "en", now));
    }

    [Fact]
    public void FormatsUnavailableWhenAllExactRowsOmitExpiration()
    {
        var now = DateTimeOffset.Parse("2026-07-10T12:00:00+09:00");
        var snapshot = new RateLimitResetCreditsSnapshot(
            2,
            now,
            [
                new RateLimitResetCredit("a", now, null, "codexRateLimits", "available", null, null),
                new RateLimitResetCredit("b", now, null, "codexRateLimits", "available", null, null)
            ]);

        Assert.Equal("2 held" + Environment.NewLine + "Expiration unavailable", RateLimitResetCreditFormatter.FormatSummary(snapshot, "en", now));
        Assert.Equal("Expiration unavailable: 2", RateLimitResetCreditFormatter.FormatDetail(snapshot, "en", now));
    }

    [Fact]
    public void FormatsUnavailableWhenAppServerReturnsCountOnly()
    {
        var snapshot = new RateLimitResetCreditsSnapshot(1, DateTimeOffset.UnixEpoch);

        Assert.Equal("1\uAC1C \uBCF4\uC720" + Environment.NewLine + "\uB9CC\uB8CC \uC815\uBCF4 \uBBF8\uC81C\uACF5", RateLimitResetCreditFormatter.FormatSummary(snapshot, "ko", DateTimeOffset.UnixEpoch));
        Assert.Equal("\uB9CC\uB8CC \uC815\uBCF4 \uBBF8\uC81C\uACF5: 1\uAC1C", RateLimitResetCreditFormatter.FormatDetail(snapshot, "ko", DateTimeOffset.UnixEpoch));
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

    [Theory]
    [InlineData("max", "Max")]
    [InlineData("ultra", "Ultra")]
    public void ReadsGpt56ExtendedReasoningEfforts(string effort, string displayEffort)
    {
        var codexHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sessionDir = Path.Combine(codexHome, "sessions", "2026", "07", "10");
        Directory.CreateDirectory(sessionDir);
        File.WriteAllLines(
            Path.Combine(sessionDir, "rollout-user.jsonl"),
            [
                JsonSerializer.Serialize(new
                {
                    timestamp = "2026-07-10T00:59:00Z",
                    type = "session_meta",
                    payload = new { id = "user", thread_source = "user", source = "desktop" }
                }),
                JsonSerializer.Serialize(new
                {
                    timestamp = "2026-07-10T00:59:01Z",
                    type = "turn_context",
                    payload = new { model = "gpt-5.6-sol", effort }
                })
            ]);

        var selection = CodexSessionStateReader.ReadLatest(TestEnvironment(codexHome));

        Assert.NotNull(selection);
        Assert.Equal("gpt-5.6-sol", selection!.Model);
        Assert.Equal(effort, selection.ReasoningEffort);
        Assert.Equal($"GPT-5.6 Sol {displayEffort}", selection.DisplayName);
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
        {"timestamp":"2026-06-18T00:59:01Z","type":"event_msg","payload":{"type":"user_message","message":"Immediate session title"}}
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
        Assert.Equal("Immediate session title", Assert.Single(state.Sessions!).SessionName);
    }

    [Fact]
    public void ReadsTokenUsageForEachUserSessionAcrossProjects()
    {
        var codexHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sessionDir = Path.Combine(codexHome, "sessions", "2026", "07", "13");
        Directory.CreateDirectory(sessionDir);

        var projectAPath = Path.Combine(sessionDir, "rollout-project-a.jsonl");
        File.WriteAllText(projectAPath, """
        {"timestamp":"2026-07-13T10:00:00Z","type":"session_meta","payload":{"id":"session-a","thread_source":"user","cwd":"D:\\Codes\\ProjectA","source":"desktop"}}
        {"timestamp":"2026-07-13T10:05:00Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":9000,"output_tokens":1000,"total_tokens":10000},"last_token_usage":{"input_tokens":3000,"output_tokens":1000,"total_tokens":4000},"model_context_window":128000}}}
        """);

        var projectBPath = Path.Combine(sessionDir, "rollout-project-b.jsonl");
        File.WriteAllText(projectBPath, """
        {"timestamp":"2026-07-13T11:00:00Z","type":"session_meta","payload":{"session_id":"session-b","thread_source":"user","cwd":"D:\\Codes\\ProjectB","source":"desktop"}}
        {"timestamp":"2026-07-13T11:05:00Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":18000,"output_tokens":2000,"total_tokens":20000},"last_token_usage":{"input_tokens":4000,"output_tokens":1000,"total_tokens":5000},"model_context_window":256000}}}
        """);
        File.SetLastWriteTimeUtc(projectBPath, DateTime.UtcNow.AddMinutes(1));

        var subagentPath = Path.Combine(sessionDir, "rollout-subagent.jsonl");
        File.WriteAllText(subagentPath, """
        {"timestamp":"2026-07-13T12:00:00Z","type":"session_meta","payload":{"id":"subagent","thread_source":"subagent","cwd":"D:\\Codes\\ProjectB","source":{"subagent":{}}}}
        {"timestamp":"2026-07-13T12:05:00Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"total_tokens":99999},"last_token_usage":{"total_tokens":99999},"model_context_window":128000}}}
        """);
        File.SetLastWriteTimeUtc(subagentPath, DateTime.UtcNow.AddMinutes(2));
        File.WriteAllText(Path.Combine(codexHome, "session_index.jsonl"), """
        {"id":"session-a","thread_name":"Project A session","updated_at":"2026-07-13T10:00:00Z"}
        {"id":"session-b","thread_name":"Old session name","updated_at":"2026-07-13T10:30:00Z"}
        {"id":"session-b","thread_name":"\uBA85\uC2DC\uC801 \uC138\uC158\uBA85","updated_at":"2026-07-13T11:00:00Z"}
        """);

        var state = CodexSessionStateReader.ReadLatestState(TestEnvironment(codexHome));

        Assert.NotNull(state);
        Assert.Equal(2, state!.Sessions!.Count);
        Assert.Equal("session-b", state.Sessions[0].SessionId);
        Assert.Equal("\uBA85\uC2DC\uC801 \uC138\uC158\uBA85", state.Sessions[0].SessionName);
        Assert.Equal("D:\\Codes\\ProjectB", state.Sessions[0].ProjectPath);
        Assert.Equal(5000, state.Sessions[0].TokenUsage.Last!.TotalTokens);
        Assert.Equal(20000, state.Sessions[0].TokenUsage.Total!.TotalTokens);
        Assert.Equal("session-a", state.Sessions[1].SessionId);
        Assert.Equal("Project A session", state.Sessions[1].SessionName);
        Assert.Equal(4000, state.Sessions[1].TokenUsage.Last!.TotalTokens);
        Assert.DoesNotContain(state.Sessions, session => session.SessionId == "subagent");
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

    [Fact]
    public async Task CodexCliFetchEnrichesSessionUsageWithThreadNames()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var binDir = Path.Combine(testRoot, "bin");
        var codexHome = Path.Combine(testRoot, "codex-home");
        var sessionDir = Path.Combine(codexHome, "sessions", "2026", "07", "13");
        Directory.CreateDirectory(binDir);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(binDir, "codex.cmd"), "@echo off\r\n");
        File.WriteAllText(Path.Combine(sessionDir, "rollout-user.jsonl"), """
        {"timestamp":"2026-07-13T10:00:00Z","type":"session_meta","payload":{"id":"session-1","thread_source":"user","cwd":"D:\\Codes\\OldName","source":"desktop"}}
        {"timestamp":"2026-07-13T10:05:00Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"total_tokens":20000},"last_token_usage":{"total_tokens":5000},"model_context_window":256000}}}
        """);

        static string Reply(int id, object result) => JsonSerializer.Serialize(new { id, result });
        var transportFactory = new QueueCodexRpcTransportFactory(
        [
            [
                Reply(1, new { ok = true }),
                Reply(2, new
                {
                    rateLimits = new
                    {
                        primary = new { usedPercent = 0.0, windowDurationMins = 10080, resetsAt = 1_800_100_000L },
                        secondary = (object?)null,
                        planType = "pro"
                    }
                }),
                Reply(3, new { account = new { type = "chatgpt", planType = "pro" } }),
                Reply(4, new
                {
                    data = new[]
                    {
                        new { id = "session-1", name = (string?)null, preview = "\uC138\uC158 \uC0AC\uC6A9\uB7C9 \uAE30\uB2A5", cwd = codexHome }
                    },
                    nextCursor = (string?)null
                })
            ]
        ]);
        var strategy = new CodexCliFetchStrategy(transportFactory);
        var context = new ProviderFetchContext(
            UsageProvider.Codex,
            new Dictionary<string, string>
            {
                ["PATH"] = binDir,
                ["PATHEXT"] = ".CMD",
                ["CODEX_HOME"] = codexHome
            },
            IncludeCredits: true,
            InitializeTimeout: TimeSpan.FromSeconds(1),
            RequestTimeout: TimeSpan.FromSeconds(1));

        var result = await strategy.FetchAsync(context, CancellationToken.None);

        var session = Assert.Single(result.Usage.Sessions!);
        Assert.Equal("\uC138\uC158 \uC0AC\uC6A9\uB7C9 \uAE30\uB2A5", session.SessionName);
        Assert.Equal(codexHome, session.ProjectPath);
        Assert.Equal(5000, session.TokenUsage.Last!.TotalTokens);
        Assert.Equal(20000, session.TokenUsage.Total!.TotalTokens);
    }

    [Fact]
    public async Task CodexCliFetchExcludesDeletedAndUnavailableProjectSessions()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var binDir = Path.Combine(testRoot, "bin");
        var codexHome = Path.Combine(testRoot, "codex-home");
        var sessionDir = Path.Combine(codexHome, "sessions", "2026", "07", "16");
        var activeProject = Path.Combine(testRoot, "active-project");
        var staleProject = Path.Combine(testRoot, "stale-project");
        var missingProject = Path.Combine(testRoot, "missing-project");
        Directory.CreateDirectory(binDir);
        Directory.CreateDirectory(sessionDir);
        Directory.CreateDirectory(activeProject);
        Directory.CreateDirectory(staleProject);
        File.WriteAllText(Path.Combine(binDir, "codex.cmd"), "@echo off\r\n");

        static void WriteSession(string path, string sessionId, string projectPath, long totalTokens)
        {
            var metadata = JsonSerializer.Serialize(new
            {
                timestamp = "2026-07-16T10:00:00Z",
                type = "session_meta",
                payload = new { id = sessionId, thread_source = "user", cwd = projectPath, source = "desktop" }
            });
            var usage = JsonSerializer.Serialize(new
            {
                timestamp = "2026-07-16T10:05:00Z",
                type = "event_msg",
                payload = new
                {
                    type = "token_count",
                    info = new
                    {
                        total_token_usage = new { total_tokens = totalTokens },
                        last_token_usage = new { total_tokens = totalTokens / 2 },
                        model_context_window = 256000
                    }
                }
            });
            File.WriteAllLines(path, [metadata, usage]);
        }

        WriteSession(Path.Combine(sessionDir, "rollout-active.jsonl"), "active-session", activeProject, 30_000);
        WriteSession(Path.Combine(sessionDir, "rollout-stale.jsonl"), "stale-session", staleProject, 20_000);
        WriteSession(Path.Combine(sessionDir, "rollout-missing.jsonl"), "missing-session", missingProject, 10_000);

        static string Reply(int id, object result) => JsonSerializer.Serialize(new { id, result });
        var transportFactory = new QueueCodexRpcTransportFactory(
        [
            [
                Reply(1, new { ok = true }),
                Reply(2, new
                {
                    rateLimits = new
                    {
                        primary = new { usedPercent = 0.0, windowDurationMins = 10080, resetsAt = 1_800_100_000L },
                        secondary = (object?)null,
                        planType = "pro"
                    }
                }),
                Reply(3, new { account = new { type = "chatgpt", planType = "pro" } }),
                Reply(4, new
                {
                    data = new[]
                    {
                        new { id = "active-session", name = "Active", cwd = activeProject },
                        new { id = "missing-session", name = "Missing", cwd = missingProject }
                    },
                    nextCursor = (string?)null
                })
            ]
        ]);
        var strategy = new CodexCliFetchStrategy(transportFactory);
        var context = new ProviderFetchContext(
            UsageProvider.Codex,
            new Dictionary<string, string>
            {
                ["PATH"] = binDir,
                ["PATHEXT"] = ".CMD",
                ["CODEX_HOME"] = codexHome
            },
            IncludeCredits: true,
            InitializeTimeout: TimeSpan.FromSeconds(1),
            RequestTimeout: TimeSpan.FromSeconds(1));

        var result = await strategy.FetchAsync(context, CancellationToken.None);

        var session = Assert.Single(result.Usage.Sessions!);
        Assert.Equal("active-session", session.SessionId);
        Assert.Equal("Active", session.SessionName);
        Assert.Equal(activeProject, session.ProjectPath);
    }

    [Fact]
    public async Task CodexCliFetchKeepsAvailableSessionsWhenThreadListIsEmpty()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var binDir = Path.Combine(testRoot, "bin");
        var codexHome = Path.Combine(testRoot, "codex-home");
        var sessionDir = Path.Combine(codexHome, "sessions", "2026", "07", "16");
        var projectPath = Path.Combine(testRoot, "active-project");
        Directory.CreateDirectory(binDir);
        Directory.CreateDirectory(sessionDir);
        Directory.CreateDirectory(projectPath);
        File.WriteAllText(Path.Combine(binDir, "codex.cmd"), "@echo off\r\n");
        File.WriteAllLines(
            Path.Combine(sessionDir, "rollout-active.jsonl"),
            [
                JsonSerializer.Serialize(new
                {
                    timestamp = "2026-07-16T10:00:00Z",
                    type = "session_meta",
                    payload = new { id = "active-session", thread_source = "user", cwd = projectPath, source = "desktop" }
                }),
                JsonSerializer.Serialize(new
                {
                    timestamp = "2026-07-16T10:05:00Z",
                    type = "event_msg",
                    payload = new
                    {
                        type = "token_count",
                        info = new
                        {
                            total_token_usage = new { total_tokens = 30_000 },
                            last_token_usage = new { total_tokens = 15_000 },
                            model_context_window = 256000
                        }
                    }
                })
            ]);

        static string Reply(int id, object result) => JsonSerializer.Serialize(new { id, result });
        var transportFactory = new QueueCodexRpcTransportFactory(
        [
            [
                Reply(1, new { ok = true }),
                Reply(2, new
                {
                    rateLimits = new
                    {
                        primary = new { usedPercent = 0.0, windowDurationMins = 10080, resetsAt = 1_800_100_000L },
                        secondary = (object?)null,
                        planType = "pro"
                    }
                }),
                Reply(3, new { account = new { type = "chatgpt", planType = "pro" } }),
                Reply(4, new { data = Array.Empty<object>(), nextCursor = (string?)null })
            ]
        ]);
        var strategy = new CodexCliFetchStrategy(transportFactory);
        var context = new ProviderFetchContext(
            UsageProvider.Codex,
            new Dictionary<string, string>
            {
                ["PATH"] = binDir,
                ["PATHEXT"] = ".CMD",
                ["CODEX_HOME"] = codexHome
            },
            IncludeCredits: true,
            InitializeTimeout: TimeSpan.FromSeconds(1),
            RequestTimeout: TimeSpan.FromSeconds(1));

        var result = await strategy.FetchAsync(context, CancellationToken.None);

        var session = Assert.Single(result.Usage.Sessions!);
        Assert.Equal("active-session", session.SessionId);
        Assert.Equal(projectPath, session.ProjectPath);
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

    private static ProviderFetchResult FetchResultWithResetCredits(
        RateLimitResetCreditsSnapshot resetCredits,
        DateTimeOffset now)
    {
        var usage = new UsageSnapshot(
            new RateWindow(10, 300, DateTimeOffset.FromUnixTimeSeconds(1_800_000_000), "in 1h"),
            null,
            null,
            now,
            new ProviderIdentitySnapshot(UsageProvider.Codex, "me@example.com", null, "plus"),
            RateLimitResetCredits: resetCredits);
        return new ProviderFetchResult(
            usage,
            new CreditsSnapshot(55, Array.Empty<CreditEvent>(), now),
            "test",
            "test",
            ProviderFetchKind.LocalProbe);
    }
}

public sealed class InstallerBuildScriptTests
{
    [Fact]
    public void SolutionDoesNotIncludeStandaloneCliProject()
    {
        var solution = File.ReadAllText(FindRepositoryFile("WindexBar.slnx"));

        Assert.DoesNotContain("WindexBar.Cli", solution, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(FindRepositoryPath(Path.Combine("src", "WindexBar.Cli"))));
    }

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
    public void RunCommandSupportsWatchRestartMode()
    {
        var runScript = File.ReadAllText(FindRepositoryFile("run.cmd"));
        var watchScript = File.ReadAllText(FindRepositoryFile(Path.Combine("scripts", "run-watch.ps1")));

        Assert.Contains("--watch", runScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scripts\\run-watch.ps1", runScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FileSystemWatcher", watchScript, StringComparison.Ordinal);
        Assert.Contains("Restart-WindexBar", watchScript, StringComparison.Ordinal);
        Assert.Contains("Stop-WindexBar", watchScript, StringComparison.Ordinal);
        Assert.Contains("Test-F5Pressed", watchScript, StringComparison.Ordinal);
        Assert.Contains("Press F5 to restart", watchScript, StringComparison.Ordinal);
        Assert.Contains("ConsoleKey]::F5", watchScript, StringComparison.Ordinal);
        Assert.DoesNotContain("Ctrl+C", watchScript, StringComparison.OrdinalIgnoreCase);
        var changeDetectedStart = watchScript.IndexOf("if ($null -ne $event", StringComparison.Ordinal);
        var pendingRestartGateStart = watchScript.IndexOf("if (-not $pendingRestart)", StringComparison.Ordinal);
        Assert.True(changeDetectedStart >= 0);
        Assert.True(pendingRestartGateStart > changeDetectedStart);
        var changeDetectedBlock = watchScript[changeDetectedStart..pendingRestartGateStart];
        Assert.DoesNotContain("Restart-WindexBar", changeDetectedBlock, StringComparison.Ordinal);
        Assert.Matches(
            new Regex(@"if \(-not \(Test-F5Pressed\)\)[\s\S]+Restart-WindexBar", RegexOptions.CultureInvariant),
            watchScript);
        Assert.Contains("dotnet", watchScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("publish", watchScript, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReleaseWorkflowRemovesGitHubGeneratedAttributionFromNotes()
    {
        var workflow = File.ReadAllText(FindRepositoryFile(Path.Combine(".github", "workflows", "release.yml")));

        Assert.Contains("Remove-ReleaseNoteAttribution", workflow, StringComparison.Ordinal);
        Assert.Contains("by\\s+@[^\\s]+\\s+in\\s+#\\d+", workflow, StringComparison.Ordinal);
        Assert.Contains("$item = Remove-ReleaseNoteAttribution $Matches.item.Trim()", workflow, StringComparison.Ordinal);
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

    [Fact]
    public void WindowsAppWiresAutoShowWithCodexSettingAndActivityService()
    {
        var settingsController = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "WindexBar.Windows", "Controllers", "SettingsController.cs")));
        var trayService = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "WindexBar.Windows", "TrayIconService.cs")));
        var activityService = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "WindexBar.Windows", "ForegroundCodexActivityService.cs")));

        Assert.Contains("AutoShowWithCodexCheckBox", settingsController, StringComparison.Ordinal);
        Assert.Contains("config.AutoShowWithCodex = _view.AutoShowWithCodexCheckBox.IsChecked == true", settingsController, StringComparison.Ordinal);
        Assert.Contains("ForegroundCodexActivityService", trayService, StringComparison.Ordinal);
        Assert.Contains("ActivitySampled", trayService, StringComparison.Ordinal);
        Assert.Contains("AutoVisibilityStabilityFilter", trayService, StringComparison.Ordinal);
        Assert.Contains("ShouldTreatAsActive(isCodexActivity)", trayService, StringComparison.Ordinal);
        Assert.Contains("AutoVisibilityPolicy.ShouldShow", trayService, StringComparison.Ordinal);
        Assert.Contains("ActivitySampled?.Invoke", activityService, StringComparison.Ordinal);
        Assert.Contains("CodexActivityWindowMatcher.IsCodexActivity", activityService, StringComparison.Ordinal);
        Assert.Contains("CodexActivityWindowMatcher.IsWindexBarWindow", activityService, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoShowModeDisablesWindowToggleShortcut()
    {
        var settingsController = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "WindexBar.Windows", "Controllers", "SettingsController.cs")));
        var trayService = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "WindexBar.Windows", "TrayIconService.cs")));

        Assert.Contains("ApplyAutoShowShortcutState();", settingsController, StringComparison.Ordinal);
        Assert.Contains("_view.ToggleHotkeyTextBox.IsEnabled = !enabled", settingsController, StringComparison.Ordinal);
        Assert.Contains("_view.ToggleHotkeyTextBox.Opacity = enabled ? 0.45 : 1", settingsController, StringComparison.Ordinal);
        Assert.Contains("if (!_settingsStore.Config.AutoShowWithCodex)", trayService, StringComparison.Ordinal);
        Assert.Contains("RegisterHotkey(ToggleWindowHotkeyId", trayService, StringComparison.Ordinal);
        Assert.Contains("_hotkeyService.Unregister(ToggleWindowHotkeyId)", trayService, StringComparison.Ordinal);
        Assert.Contains("false);", trayService, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoVisibilityPreservesTheSelectedSection()
    {
        var trayService = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "WindexBar.Windows", "TrayIconService.cs")));
        var applyAutoVisibility = ExtractMethodBody(trayService, "private void ApplyAutoVisibility(bool isCodexActivity)");

        Assert.Contains("WindowCloseBehavior.ShowPassive(window)", applyAutoVisibility, StringComparison.Ordinal);
        Assert.DoesNotContain("window.ShowHudView()", applyAutoVisibility, StringComparison.Ordinal);
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

    private static string FindRepositoryPath(string relativePath, [CallerFilePath] string sourceFilePath = "")
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
                if (Directory.Exists(path) || File.Exists(path))
                {
                    return path;
                }

                directory = directory.Parent;
            }
        }

        return Path.GetFullPath(relativePath);
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

        throw new InvalidOperationException($"Could not parse method body for {signature}.");
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
