using WindexBar.Core.Config;
using WindexBar.Core.Models;
using WindexBar.Core.Providers;
using WindexBar.Core.Providers.Codex;
using WindexBar.Core.Refresh;
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
    public void DoesNotPromoteSparkCurrentWindowIntoWeeklyOnlyCodexUsage()
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
                ["codex_bengalfox"] = new
                {
                    limitId = "codex_bengalfox",
                    limitName = "GPT-5.3-Codex-Spark",
                    primary = new { usedPercent = 0.0, windowDurationMins = 300, resetsAt = 1_800_000_000L },
                    secondary = new { usedPercent = 0.0, windowDurationMins = 10080, resetsAt = 1_800_100_000L }
                },
                ["codex"] = new
                {
                    limitId = "codex",
                    primary = new { usedPercent = 0.0, windowDurationMins = 10080, resetsAt = 1_800_100_000L },
                    secondary = (object?)null
                }
            }
        }))!;

        var usage = CodexUsageMapper.MapUsage(response, null, DateTimeOffset.UnixEpoch)!;

        Assert.Null(usage.Primary);
        Assert.Equal(10080, usage.Secondary!.WindowMinutes);
        var generic = Assert.Single(usage.Models!, model => model.ModelName == "Codex");
        Assert.Null(generic.Current);
        Assert.Equal(10080, generic.Weekly!.WindowMinutes);
        var spark = Assert.Single(usage.Models!, model => model.ModelName == "GPT-5.3 Codex Spark");
        Assert.Equal(300, spark.Current!.WindowMinutes);
        Assert.Equal(10080, spark.Weekly!.WindowMinutes);
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
    public void CorruptConfigFallsBackToDefaultsWithoutOverwritingTheOriginal()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ definitely not json");

        var config = new WindexBarConfigStore(path).LoadOrCreateDefault();

        Assert.Equal(WindexBarConfig.DefaultLanguage, config.Language);
        Assert.Equal("{ definitely not json", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("{\"providers\":null}")]
    [InlineData("{\"providers\":[null]}")]
    public void NullProviderCollectionsAndEntriesFallBackToCodexDefaults(string json)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "config.json");
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, json);

        var config = new WindexBarConfigStore(path).LoadOrCreateDefault();

        var provider = Assert.Single(config.Providers);
        Assert.Equal("codex", provider.Id);
        Assert.True(provider.Enabled);
    }

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
        Assert.Equal(WindexBarConfig.DefaultLanguage, reloaded.Language);
        Assert.Equal(WindexBarConfig.DefaultToggleWindowHotkey, reloaded.Hotkeys.ToggleWindow);
        Assert.Equal(WindexBarConfig.DefaultToggleSidebarHotkey, reloaded.Hotkeys.ToggleSidebar);
        Assert.True(reloaded.StartWithWindows);
        Assert.False(reloaded.AutoShowWithCodex);
        Assert.False(reloaded.Sidebar.ShowOnHover);
        Assert.Null(reloaded.Window.ClientWidth);
        Assert.Null(reloaded.Window.ClientHeight);
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
    public void MigratesLegacyRefreshIntervalWithoutPersistingItAgain()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "config.json");
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, """
        {
          "version": 10,
          "providers": [
            { "id": "codex", "enabled": false, "refreshIntervalSeconds": 5 }
          ]
        }
        """);
        var store = new WindexBarConfigStore(path);
        var config = store.LoadOrCreateDefault();
        store.Save(config);

        var saved = File.ReadAllText(path);

        Assert.False(config.GetProviderConfig(UsageProvider.Codex).Enabled);
        Assert.DoesNotContain("refreshIntervalSeconds", saved, StringComparison.Ordinal);
        Assert.Contains($"\"version\": {WindexBarConfig.CurrentVersion}", saved, StringComparison.Ordinal);
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
    public void PreservesRateLimitAlertPreference()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "config.json");
        var store = new WindexBarConfigStore(path);
        var config = store.LoadOrCreateDefault();
        config.RateLimitAlerts.Enabled = false;
        store.Save(config);

        var reloaded = store.LoadOrCreateDefault();

        Assert.False(reloaded.RateLimitAlerts.Enabled);
    }

    [Fact]
    public void PreservesSidebarHoverRevealPreference()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "config.json");
        var store = new WindexBarConfigStore(path);
        var config = store.LoadOrCreateDefault();
        config.Sidebar.ShowOnHover = true;
        store.Save(config);

        var reloaded = store.LoadOrCreateDefault();

        Assert.True(reloaded.Sidebar.ShowOnHover);
    }

    [Fact]
    public void PreservesSavedWindowClientSize()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "config.json");
        var store = new WindexBarConfigStore(path);
        var config = store.LoadOrCreateDefault();
        config.Window.ClientWidth = 287.5;
        config.Window.ClientHeight = 221.25;
        store.Save(config);

        var reloaded = store.LoadOrCreateDefault();

        Assert.Equal(287.5, reloaded.Window.ClientWidth);
        Assert.Equal(221.25, reloaded.Window.ClientHeight);
    }

    [Fact]
    public void InvalidSavedWindowClientSizeIsDiscarded()
    {
        var config = WindexBarConfig.Default();
        config.Window.ClientWidth = double.NaN;
        config.Window.ClientHeight = 20;

        config.Normalized();

        Assert.Null(config.Window.ClientWidth);
        Assert.Null(config.Window.ClientHeight);
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
    public void PreservesAppUpdateState()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "config.json");
        var store = new WindexBarConfigStore(path);
        var config = store.LoadOrCreateDefault();
        var checkedAt = new DateTimeOffset(2026, 7, 21, 1, 2, 3, TimeSpan.Zero);
        config.AppUpdates.LatestVersion = "1.6.0";
        config.AppUpdates.LastCheckedAt = checkedAt;
        config.AppUpdates.PendingVersion = "1.6.0";
        store.Save(config);

        var reloaded = store.LoadOrCreateDefault();

        Assert.True(reloaded.AppUpdates.AutomaticallyUpdate);
        Assert.Equal("1.6.0", reloaded.AppUpdates.LatestVersion);
        Assert.Equal(checkedAt, reloaded.AppUpdates.LastCheckedAt);
        Assert.Equal("1.6.0", reloaded.AppUpdates.PendingVersion);
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
    public void ClassifiesASevenDayPrimarySessionWindowAsWeekly()
    {
        var codexHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sessionDir = Path.Combine(codexHome, "sessions", "2026", "07", "29");
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "rollout-user.jsonl"), """
        {"timestamp":"2026-07-29T06:00:00Z","type":"session_meta","payload":{"id":"user","thread_source":"user","source":"desktop"}}
        {"timestamp":"2026-07-29T06:00:01Z","type":"turn_context","payload":{"model":"gpt-5.6-sol","effort":"high"}}
        {"timestamp":"2026-07-29T06:00:02Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"total_tokens":1200}},"rate_limits":{"limit_id":"codex","primary":{"used_percent":1.0,"window_minutes":10080,"resets_at":1785907965},"secondary":null}}}
        """);

        var state = CodexSessionStateReader.ReadLatestState(TestEnvironment(codexHome));

        var model = Assert.Single(state!.Models);
        Assert.Equal("Codex", model.ModelName);
        Assert.Null(model.Current);
        Assert.Equal(1, model.Weekly!.UsedPercent);
        Assert.Equal(10080, model.Weekly.WindowMinutes);
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
    public async Task CodexCliFetchUsesSupportedNonInteractiveApprovalPolicy()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var binDir = Path.Combine(testRoot, "bin");
        Directory.CreateDirectory(binDir);
        File.WriteAllText(Path.Combine(binDir, "codex.cmd"), "@echo off\r\n");

        static string Reply(int id, object result) => JsonSerializer.Serialize(new { id, result });
        var transportFactory = new QueueCodexRpcTransportFactory(
        [
            [
                Reply(1, new { ok = true }),
                Reply(2, new
                {
                    rateLimits = new
                    {
                        primary = new { usedPercent = 10.0, windowDurationMins = 300, resetsAt = 1_800_000_000L }
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
                ["PATHEXT"] = ".CMD"
            },
            IncludeCredits: true,
            InitializeTimeout: TimeSpan.FromSeconds(1),
            RequestTimeout: TimeSpan.FromSeconds(1));

        await strategy.FetchAsync(context, CancellationToken.None);

        Assert.Equal(["-s", "read-only", "-a", "never", "app-server"], transportFactory.Arguments);
    }

    [Fact]
    public async Task CodexCliFetchFallsBackToSessionUsageWhenRpcFails()
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
        {"timestamp":"2026-06-18T00:59:02Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":1000,"output_tokens":200,"total_tokens":1200},"last_token_usage":{"input_tokens":900,"output_tokens":100,"total_tokens":1000},"model_context_window":258400},"rate_limits":{"limit_id":"gpt-5.3-codex-spark","primary":{"used_percent":1.0,"window_minutes":10080,"resets_at":1800000000}}}}
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

        var result = await strategy.FetchAsync(context, CancellationToken.None);

        Assert.Equal(1, result.Usage.Models!.Single().Weekly!.UsedPercent);
        Assert.Equal("gpt-5.3-codex-spark", result.Usage.ActiveModel!.Model);
        Assert.Equal(1200, result.Usage.TokenUsage!.Total!.TotalTokens);
        Assert.Single(result.Usage.Sessions!);
        Assert.Null(result.Credits);
    }

    [Fact]
    public async Task CodexCliFetchPrefersLatestGenericSessionLimitsOverRpcLimits()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var binDir = Path.Combine(testRoot, "bin");
        var codexHome = Path.Combine(testRoot, "codex-home");
        var sessionDir = Path.Combine(codexHome, "sessions", "2026", "07", "29");
        Directory.CreateDirectory(binDir);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(binDir, "codex.cmd"), "@echo off\r\n");
        File.WriteAllText(Path.Combine(sessionDir, "rollout-user.jsonl"), """
        {"timestamp":"2026-07-29T06:00:00Z","type":"session_meta","payload":{"id":"session-1","thread_source":"user","source":"desktop"}}
        {"timestamp":"2026-07-29T06:00:01Z","type":"turn_context","payload":{"model":"gpt-5.6-sol","effort":"high"}}
        {"timestamp":"2026-07-29T06:00:02Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"total_tokens":1200}},"rate_limits":{"limit_id":"codex","primary":{"used_percent":1.0,"window_minutes":10080,"resets_at":1785907965},"secondary":null}}}
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
                        primary = new { usedPercent = 10.0, windowDurationMins = 300, resetsAt = 1_800_000_000L },
                        secondary = new { usedPercent = 14.0, windowDurationMins = 10080, resetsAt = 1_800_100_000L }
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

        Assert.Equal(10, result.Usage.Primary!.UsedPercent);
        Assert.Equal(1, result.Usage.Secondary!.UsedPercent);
        Assert.Equal(1, result.Usage.Models!.Single(model => model.ModelName == "Codex").Weekly!.UsedPercent);
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
    public async Task CodexCliFetchKeepsOnlyLatestRolloutForDuplicateSessionId()
    {
        var (binDir, codexHome, projectPath) = CreateDuplicateSessionFixture();
        var transportFactory = new QueueCodexRpcTransportFactory(
        [
            [
                RpcReply(1, new { ok = true }),
                RpcReply(2, new
                {
                    rateLimits = new
                    {
                        primary = new { usedPercent = 0.0, windowDurationMins = 10080, resetsAt = 1_800_100_000L },
                        secondary = (object?)null,
                        planType = "pro"
                    }
                }),
                RpcReply(3, new { account = new { type = "chatgpt", planType = "pro" } }),
                RpcReply(4, new
                {
                    data = new[] { new { id = "shared-session", name = "Gemini tracker app", cwd = projectPath } },
                    nextCursor = (string?)null
                })
            ]
        ]);
        var strategy = new CodexCliFetchStrategy(transportFactory);
        var context = CreateDuplicateSessionContext(binDir, codexHome, TimeSpan.FromSeconds(1));

        var result = await strategy.FetchAsync(context, CancellationToken.None);

        var session = Assert.Single(result.Usage.Sessions!);
        Assert.Equal("shared-session", session.SessionId);
        Assert.Equal("Gemini tracker app", session.SessionName);
        Assert.Equal(30_000, session.TokenUsage.Total!.TotalTokens);
    }

    [Fact]
    public async Task CodexCliFetchKeepsOnlyLatestRolloutWhenThreadListFails()
    {
        var (binDir, codexHome, _) = CreateDuplicateSessionFixture();
        var transportFactory = new QueueCodexRpcTransportFactory(
        [
            [
                RpcReply(1, new { ok = true }),
                RpcReply(2, new
                {
                    rateLimits = new
                    {
                        primary = new { usedPercent = 0.0, windowDurationMins = 10080, resetsAt = 1_800_100_000L },
                        secondary = (object?)null,
                        planType = "pro"
                    }
                }),
                RpcReply(3, new { account = new { type = "chatgpt", planType = "pro" } })
            ]
        ]);
        var strategy = new CodexCliFetchStrategy(transportFactory);
        var context = CreateDuplicateSessionContext(binDir, codexHome, TimeSpan.FromMilliseconds(20));

        var result = await strategy.FetchAsync(context, CancellationToken.None);

        var session = Assert.Single(result.Usage.Sessions!);
        Assert.Equal("shared-session", session.SessionId);
        Assert.Equal(30_000, session.TokenUsage.Total!.TotalTokens);
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

    private static (string BinDir, string CodexHome, string ProjectPath) CreateDuplicateSessionFixture()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var binDir = Path.Combine(testRoot, "bin");
        var codexHome = Path.Combine(testRoot, "codex-home");
        var sessionDir = Path.Combine(codexHome, "sessions", "2026", "08", "26");
        var projectPath = Path.Combine(testRoot, "TwinQuota");
        Directory.CreateDirectory(binDir);
        Directory.CreateDirectory(sessionDir);
        Directory.CreateDirectory(projectPath);
        File.WriteAllText(Path.Combine(binDir, "codex.cmd"), "@echo off\r\n");

        WriteDuplicateSession(
            Path.Combine(sessionDir, "rollout-original.jsonl"),
            projectPath,
            "2026-08-26T01:05:00Z",
            10_000);
        WriteDuplicateSession(
            Path.Combine(sessionDir, "rollout-resumed.jsonl"),
            projectPath,
            "2026-08-26T02:05:00Z",
            30_000);
        return (binDir, codexHome, projectPath);
    }

    private static void WriteDuplicateSession(
        string path,
        string projectPath,
        string updatedAt,
        long totalTokens)
    {
        File.WriteAllLines(
            path,
            [
                JsonSerializer.Serialize(new
                {
                    timestamp = "2026-08-26T01:00:00Z",
                    type = "session_meta",
                    payload = new
                    {
                        id = "shared-session",
                        thread_source = "user",
                        cwd = projectPath,
                        source = "desktop"
                    }
                }),
                JsonSerializer.Serialize(new
                {
                    timestamp = updatedAt,
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
                })
            ]);
    }

    private static ProviderFetchContext CreateDuplicateSessionContext(
        string binDir,
        string codexHome,
        TimeSpan requestTimeout) =>
        new(
            UsageProvider.Codex,
            new Dictionary<string, string>
            {
                ["PATH"] = binDir,
                ["PATHEXT"] = ".CMD",
                ["CODEX_HOME"] = codexHome
            },
            IncludeCredits: true,
            InitializeTimeout: TimeSpan.FromSeconds(1),
            RequestTimeout: requestTimeout);

    private static string RpcReply(int id, object result) => JsonSerializer.Serialize(new { id, result });
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
    public void PublishUsesStableWinUiReleaseOptions()
    {
        var script = File.ReadAllText(FindRepositoryFile("build-installer.cmd"));

        Assert.Contains("-p:PublishTrimmed=false", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-p:PublishReadyToRun=false", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-p:DebugType=None", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-p:DebugSymbols=false", script, StringComparison.OrdinalIgnoreCase);
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
        Assert.DoesNotContain("-CodeSigningCert", signScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1.3.6.1.5.5.7.3.3", signScript, StringComparison.Ordinal);
    }

    [Fact]
    public void ConsoleInstallRebuildsWinUiArtifactsAndDetectsImmediateStartupFailure()
    {
        var installScript = File.ReadAllText(FindRepositoryFile(Path.Combine("scripts", "install-console.ps1")));

        Assert.Contains("build\\WindexBar.Core", installScript, StringComparison.Ordinal);
        Assert.Contains("build\\WindexBar.Windows", installScript, StringComparison.Ordinal);
        Assert.Contains("Remove-IfSafe -Path $buildDir -Parent $ArtifactsRoot", installScript, StringComparison.Ordinal);
        Assert.Contains("Start-Process -FilePath $AppExe -WorkingDirectory $InstallDir -PassThru", installScript, StringComparison.Ordinal);
        Assert.Contains("$launchedProcess.HasExited", installScript, StringComparison.Ordinal);
        Assert.Contains("WindexBar exited during startup", installScript, StringComparison.Ordinal);
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
    public void AutoShowModeLocksWindowToggleShortcutAndShowsNotice()
    {
        var settingsController = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "WindexBar.Windows", "Controllers", "SettingsController.cs")));
        var trayService = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "WindexBar.Windows", "TrayIconService.cs")));

        Assert.Contains("ApplyAutoShowShortcutState();", settingsController, StringComparison.Ordinal);
        Assert.Contains("_view.ToggleHotkeyButton.IsEnabled = !enabled", settingsController, StringComparison.Ordinal);
        Assert.Contains("_view.ToggleHotkeyButton.Opacity = enabled ? 0.45 : 1", settingsController, StringComparison.Ordinal);
        Assert.Contains("RegisterHotkey(ToggleWindowHotkeyId", trayService, StringComparison.Ordinal);
        Assert.Contains("if (_settingsStore.Config.AutoShowWithCodex)", trayService, StringComparison.Ordinal);
        Assert.Contains("ShowModeLockedNotice(", trayService, StringComparison.Ordinal);
        Assert.DoesNotContain("_hotkeyService.Unregister(ToggleWindowHotkeyId)", trayService, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoVisibilityPreservesTheSelectedSection()
    {
        var trayService = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "WindexBar.Windows", "TrayIconService.cs")));
        var applyAutoVisibility = ExtractMethodBody(trayService, "private void ApplyAutoVisibility(bool isCodexActivity)");

        Assert.Contains("if (_statusWindow is not null && WindowCloseBehavior.IsVisible(_statusWindow))", applyAutoVisibility, StringComparison.Ordinal);
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
        Assert.Contains("_settingsStore.Config.Sidebar.ShowOnHover", toggleSidebarBody, StringComparison.Ordinal);
        Assert.Contains("ShowModeLockedNotice(", toggleSidebarBody, StringComparison.Ordinal);
        Assert.DoesNotContain("_hotkeyService.Unregister(ToggleSidebarHotkeyId)", service, StringComparison.Ordinal);
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
    public void ReleaseWorkflowPublishesRsaSignedUpdateManifest()
    {
        var workflow = File.ReadAllText(FindRepositoryFile(Path.Combine(".github", "workflows", "release.yml")));

        Assert.Contains("WINDEXBAR_UPDATE_SIGNING_KEY", workflow, StringComparison.Ordinal);
        Assert.Contains("WINDEXBAR_UPDATE_SIGNING_KEY_RECOVERY", workflow, StringComparison.Ordinal);
        Assert.Contains("RSASignaturePadding]::Pkcs1", workflow, StringComparison.Ordinal);
        Assert.Contains("update.json", workflow, StringComparison.Ordinal);
        Assert.Contains("update.sig", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("signature.Status -ne 'Valid'", workflow, StringComparison.Ordinal);
        Assert.Matches(
            new Regex("gh release upload.*gh release edit.*--draft=false", RegexOptions.Singleline),
            workflow);
    }

    [Fact]
    public void ReleaseWorkflowDefersWingetSubmissionWhilePullRequestIsOpen()
    {
        var workflow = File.ReadAllText(FindRepositoryFile(Path.Combine(".github", "workflows", "release.yml")));

        Assert.Contains("Get-OpenWindexBarWingetPullRequest", workflow, StringComparison.Ordinal);
        Assert.Contains("repo:microsoft/winget-pkgs is:pr is:open", workflow, StringComparison.Ordinal);
        Assert.Contains("WinGet submission deferred", workflow, StringComparison.Ordinal);
        Assert.Contains("exit 0", workflow, StringComparison.Ordinal);
        Assert.Contains("exit $submitExitCode", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseWorkflowPinsWingetInstallerArchitecture()
    {
        var workflow = File.ReadAllText(FindRepositoryFile(Path.Combine(".github", "workflows", "release.yml")));

        Assert.Contains("'--urls', \"${installerUrl}|x64\"", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("'--urls', $installerUrl", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void AppContainsGenerated3072BitUpdatePublicKey()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine(
            "src",
            "WindexBar.Windows",
            "UpdateSigningPublicKey.cs")));
        var modulus = Regex.Match(source, "ModulusBase64 = \"([^\"]+)\"");
        var exponent = Regex.Match(source, "ExponentBase64 = \"([^\"]+)\"");

        Assert.True(modulus.Success);
        Assert.True(exponent.Success);
        Assert.Equal(384, Convert.FromBase64String(modulus.Groups[1].Value).Length);
        Assert.Equal("AQAB", exponent.Groups[1].Value);
        Assert.DoesNotContain("__WINDEXBAR", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallerPreservesUpgradeChoicesAndOnlyAutoRestartsForAutoUpdate()
    {
        var installer = File.ReadAllText(FindRepositoryFile(Path.Combine("installer", "WindexBar.iss")));

        Assert.DoesNotContain("UsePreviousAppDir=no", installer, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Type: filesandordirs; Name: \"{userstartup}\\WindexBar.lnk\"",
            installer,
            StringComparison.Ordinal);
        Assert.Contains("{param:autoupdate|0}", installer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Check: IsAutoUpdate", installer, StringComparison.Ordinal);
        Assert.Contains("Tasks: startup; Check: not IsAutoUpdate", installer, StringComparison.Ordinal);
        Assert.Contains("Tasks: desktopicon; Check: not IsAutoUpdate", installer, StringComparison.Ordinal);
    }

    [Fact]
    public void AppUpdateRequiresConfirmationAndForceClosesOnlyAfterApproval()
    {
        var app = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "WindexBar.Windows", "App.xaml.cs")));
        var mainWindow = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "WindexBar.Windows", "MainWindow.xaml.cs")));
        var promptPopup = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "WindexBar.Windows", "Dialogs", "AppUpdatePromptPopup.cs")));

        var promptIndex = app.IndexOf("PromptForAppUpdateAsync", StringComparison.Ordinal);
        var installerIndex = app.IndexOf("Process.Start(new ProcessStartInfo", StringComparison.Ordinal);
        Assert.True(promptIndex >= 0);
        Assert.True(installerIndex > promptIndex);
        Assert.Contains("if (!installNow", app, StringComparison.Ordinal);
        Assert.Contains("_deferredAppUpdateVersion", app, StringComparison.Ordinal);
        Assert.Contains("/FORCECLOSEAPPLICATIONS", app, StringComparison.Ordinal);
        Assert.Contains("Update now", promptPopup, StringComparison.Ordinal);
        Assert.Contains("Later", promptPopup, StringComparison.Ordinal);
        Assert.Contains("HasOpenAppUpdatePrompt", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_appUpdatePromptWindow?.Close();", mainWindow, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(_appUpdatePromptWindow, popup)", mainWindow, StringComparison.Ordinal);
        Assert.Contains("onWindowClosed(popup);", promptPopup, StringComparison.Ordinal);

        var trayService = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "WindexBar.Windows", "TrayIconService.cs")));
        Assert.Contains("_statusWindow?.HasOpenAppUpdatePrompt == true", trayService, StringComparison.Ordinal);
    }

    [Fact]
    public void SidebarUsesAnUnconstrainedPopupWithoutResizingTheMainWindow()
    {
        var mainWindow = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "WindexBar.Windows", "MainWindow.xaml.cs")));
        var sidebarController = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "WindexBar.Windows", "Controllers", "SidebarController.cs")));

        Assert.Contains("_sideBarPanel", sidebarController, StringComparison.Ordinal);
        Assert.Contains("HorizontalOffset = -SideBarVisualWidth", sidebarController, StringComparison.Ordinal);
        Assert.Contains("VerticalOffset = TitleBarClientHeight", sidebarController, StringComparison.Ordinal);
        Assert.Contains("ShouldConstrainToRootBounds = false", sidebarController, StringComparison.Ordinal);
        Assert.Contains("NavigateFromSideBar(", mainWindow, StringComparison.Ordinal);
        Assert.Contains("WindowCloseBehavior.ActivateForInput(this);", mainWindow, StringComparison.Ordinal);
        Assert.Contains("var scrollViewer = VisibleScrollViewer();", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_sideBarHoverPollTimer = new DispatcherTimer", sidebarController, StringComparison.Ordinal);
        Assert.Contains("PollSideBarHoverRegion", sidebarController, StringComparison.Ordinal);
        Assert.Contains("GetCursorPos(out var cursor)", sidebarController, StringComparison.Ordinal);
        Assert.Contains("isOverExternalRegion || isOverInternalRegion", sidebarController, StringComparison.Ordinal);
        Assert.Contains("RootLayout.SizeChanged += RootLayout_SizeChanged;", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_settingsStore.Config.Sidebar.ShowOnHover", sidebarController, StringComparison.Ordinal);
        Assert.Contains("_sideBarPopup.XamlRoot = _rootLayout.XamlRoot", sidebarController, StringComparison.Ordinal);
        Assert.Contains("_sideBarPopup.IsOpen != shouldOpenSideBar", sidebarController, StringComparison.Ordinal);
        Assert.Contains("_rootLayout.ActualHeight - TitleBarClientHeight - SideBarBottomMargin", sidebarController, StringComparison.Ordinal);
        Assert.Contains("_sideBarPanel.Height = sideBarHeight", sidebarController, StringComparison.Ordinal);
        Assert.DoesNotContain("OnSideBarScrollNavigationKeyDown", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("SideBarHoverTarget", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("SideBarPanel.PointerEntered", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("SideBarColumn", mainWindow, StringComparison.Ordinal);

        var toggleStart = sidebarController.IndexOf("public void ToggleSideBar()", StringComparison.Ordinal);
        var toggleEnd = sidebarController.IndexOf("public void ApplySideBarHoverPreference()", toggleStart, StringComparison.Ordinal);
        Assert.True(toggleStart >= 0 && toggleEnd > toggleStart);
        Assert.Contains("if (IsSideBarHoverRevealEnabled)", sidebarController[toggleStart..toggleEnd], StringComparison.Ordinal);
        Assert.DoesNotContain("ResizeForCurrentView();", sidebarController[toggleStart..toggleEnd], StringComparison.Ordinal);
    }

    [Fact]
    public void WindowClientSizeIsRestoredAcrossAppRestarts()
    {
        var app = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "WindexBar.Windows", "App.xaml.cs")));
        var mainWindow = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "WindexBar.Windows", "MainWindow.xaml.cs")));

        Assert.Contains("if (!TryRestoreWindowSize())", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_settingsStore.Config.Window.ClientWidth", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_settingsStore.Config.Window.ClientHeight", mainWindow, StringComparison.Ordinal);
        Assert.Contains("SettingsStore.Persist();", app, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowStartupWiresAutoVisibilityMonitoringAfterWindowCreation()
    {
        var app = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "WindexBar.Windows", "App.xaml.cs")));
        var trayService = File.ReadAllText(FindRepositoryFile(Path.Combine(
            "src",
            "WindexBar.Windows",
            "TrayIconService.cs")));
        var mainWindow = File.ReadAllText(FindRepositoryFile(Path.Combine(
            "src",
            "WindexBar.Windows",
            "MainWindow.xaml.cs")));

        Assert.Contains("TrayIconService.Start();", app, StringComparison.Ordinal);
        Assert.DoesNotContain("TrayIconService.ShowStatusWindow();", app, StringComparison.Ordinal);
        Assert.Contains("ApplyInitialWindowSize();", mainWindow, StringComparison.Ordinal);
        Assert.Contains("RootLayout.Loaded += OnRootLayoutLoaded;", mainWindow, StringComparison.Ordinal);
        Assert.Contains("await _codexUpdateController.CheckAsync", mainWindow, StringComparison.Ordinal);
        Assert.Contains("StartupCompleted?.Invoke(this, EventArgs.Empty);", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_statusWindow.StartupCompleted += OnStatusWindowStartupCompleted;", trayService, StringComparison.Ordinal);
        Assert.Contains("if (_started)", trayService, StringComparison.Ordinal);

        var constructorStart = trayService.IndexOf("public TrayIconService(", StringComparison.Ordinal);
        var startMethod = trayService.IndexOf("public void Start()", constructorStart, StringComparison.Ordinal);
        Assert.True(constructorStart >= 0 && startMethod > constructorStart);
        Assert.DoesNotContain(
            "ApplyAutoVisibilityMonitoring();",
            trayService[constructorStart..startMethod],
            StringComparison.Ordinal);
    }

    [Fact]
    public void AutoShowStartsMonitoringWithoutShowingTheInitialWindow()
    {
        var trayService = File.ReadAllText(FindRepositoryFile(Path.Combine(
            "src",
            "WindexBar.Windows",
            "TrayIconService.cs")));
        var startIndex = trayService.IndexOf("public void Start()", StringComparison.Ordinal);
        var showIndex = trayService.IndexOf("public void ShowStatusWindow()", startIndex, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && showIndex > startIndex);
        var startMethod = trayService[startIndex..showIndex];

        Assert.Contains("if (_settingsStore.Config.AutoShowWithCodex)", startMethod, StringComparison.Ordinal);
        Assert.Contains("StartAutoVisibilityMonitoring();", startMethod, StringComparison.Ordinal);
        Assert.Contains("return;", startMethod, StringComparison.Ordinal);
        Assert.Contains("ShowStatusWindow();", startMethod, StringComparison.Ordinal);
        Assert.True(
            startMethod.IndexOf("StartAutoVisibilityMonitoring();", StringComparison.Ordinal)
                < startMethod.IndexOf("ShowStatusWindow();", StringComparison.Ordinal));
    }

    [Fact]
    public void CodexCliDetectionRepeatsWithoutAutomaticUpdatePrompt()
    {
        var controller = File.ReadAllText(FindRepositoryFile(Path.Combine(
            "src",
            "WindexBar.Windows",
            "Controllers",
            "CodexUpdateController.cs")));
        var mainWindow = File.ReadAllText(FindRepositoryFile(Path.Combine(
            "src",
            "WindexBar.Windows",
            "MainWindow.xaml.cs")));

        Assert.Contains("TimeSpan.FromMinutes(1)", controller, StringComparison.Ordinal);
        Assert.Contains("_codexCliDetectionTimer.Tick += OnCodexCliDetectionTimerTick", controller, StringComparison.Ordinal);
        Assert.Contains("allowAutomaticUpdate: false", controller, StringComparison.Ordinal);
        Assert.Contains("_codexCliDetectionTimer.Start()", controller, StringComparison.Ordinal);
        Assert.Contains("_codexUpdateController.Dispose();", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void WinUiReleasePublishDisablesUnsafeTrimming()
    {
        var project = File.ReadAllText(FindRepositoryFile(Path.Combine(
            "src",
            "WindexBar.Windows",
            "WindexBar.Windows.csproj")));
        var installerBuild = File.ReadAllText(FindRepositoryFile("build-installer.cmd"));

        Assert.Contains("<PublishTrimmed>False</PublishTrimmed>", project, StringComparison.Ordinal);
        Assert.DoesNotContain("<PublishTrimmed Condition=", project, StringComparison.Ordinal);
        Assert.Contains("-p:PublishTrimmed=false", installerBuild, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("-p:PublishTrimmed=true", installerBuild, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SettingsAndStyleOptionsProvideLocalizedHoverHelp()
    {
        var settingsController = File.ReadAllText(FindRepositoryFile(Path.Combine(
            "src",
            "WindexBar.Windows",
            "Controllers",
            "SettingsController.cs")));
        var sidebarController = File.ReadAllText(FindRepositoryFile(Path.Combine(
            "src",
            "WindexBar.Windows",
            "Controllers",
            "SidebarController.cs")));
        var mainWindow = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "WindexBar.Windows", "MainWindow.xaml.cs")));
        var scrollBarManager = File.ReadAllText(FindRepositoryFile(Path.Combine(
            "src",
            "WindexBar.Windows",
            "UI",
            "TransientScrollBarManager.cs")));
        var featureViewHelpers = File.ReadAllText(FindRepositoryFile(Path.Combine(
            "src",
            "WindexBar.Windows",
            "Views",
            "FeatureViewHelpers.cs")));

        Assert.Contains("ApplyOptionTooltips(text);", settingsController, StringComparison.Ordinal);
        Assert.Contains("This locks the window toggle.", settingsController, StringComparison.Ordinal);
        Assert.Contains("This locks the sidebar toggle.", settingsController, StringComparison.Ordinal);
        Assert.Contains("ApplyStyleTooltips();", mainWindow, StringComparison.Ordinal);
        Assert.Contains("SaveStyleButton.Content = Text(\"Save\", \"\\uC800\\uC7A5\");", mainWindow, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.SetToolTip", featureViewHelpers, StringComparison.Ordinal);
        Assert.Contains("if (!TransientScrollBarManager.IsArrowKeyInputControl(args.OriginalSource as DependencyObject))", mainWindow, StringComparison.Ordinal);
        Assert.Contains("or ButtonBase", scrollBarManager, StringComparison.Ordinal);
        Assert.Contains("_modeLockNoticePopup = new Popup", sidebarController, StringComparison.Ordinal);
        Assert.Contains("Width = ModeLockNoticeWidth", sidebarController, StringComparison.Ordinal);
        Assert.Contains("IsHitTestVisible = false", sidebarController, StringComparison.Ordinal);
        Assert.Contains("IsLightDismissEnabled = false", sidebarController, StringComparison.Ordinal);
        Assert.Contains("_modeLockNoticePopup.IsOpen = true", sidebarController, StringComparison.Ordinal);
        Assert.Contains("ModeLockNoticeDurationMilliseconds", sidebarController, StringComparison.Ordinal);
        Assert.DoesNotContain("_modeLockNoticeWindow", sidebarController, StringComparison.Ordinal);
    }

    [Fact]
    public void GaugeColorAndBrightnessChangesRefreshTheStylePreviewImmediately()
    {
        var mainWindow = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "WindexBar.Windows", "MainWindow.xaml.cs")));
        var colorPicker = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "WindexBar.Windows", "Dialogs", "GaugeColorPickerPopup.cs")));
        var colorWindowStart = colorPicker.IndexOf("public void Show(", StringComparison.Ordinal);
        var colorWindowEnd = colorPicker.IndexOf("public static global::Windows.UI.Color NormalizeGaugeColorBrightness", colorWindowStart, StringComparison.Ordinal);
        var closeWindowStart = mainWindow.IndexOf("private void CloseGaugeColorWindow", StringComparison.Ordinal);
        var closeWindowEnd = mainWindow.IndexOf("private static string ReadStyleOption", closeWindowStart, StringComparison.Ordinal);

        Assert.True(colorWindowStart >= 0 && colorWindowEnd > colorWindowStart);
        var colorWindow = colorPicker[colorWindowStart..colorWindowEnd];
        Assert.Contains("void PreviewCandidateColor()", colorWindow, StringComparison.Ordinal);
        Assert.Contains("candidateColor = currentColor;", colorWindow, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(colorWindow, "PreviewCandidateColor\\(\\);").Count);
        Assert.Contains("CloseCandidateColor(keepCandidate: true)", colorWindow, StringComparison.Ordinal);
        Assert.Contains("CloseCandidateColor(keepCandidate: false)", colorWindow, StringComparison.Ordinal);
        Assert.Contains("onColorChanged(initialPreviewColor);", colorWindow, StringComparison.Ordinal);
        Assert.Contains("closedWithoutAction", colorWindow, StringComparison.Ordinal);
        Assert.True(closeWindowStart >= 0 && closeWindowEnd > closeWindowStart);
        var closeWindow = mainWindow[closeWindowStart..closeWindowEnd];
        Assert.Contains("if (discardPendingColor)", closeWindow, StringComparison.Ordinal);
        Assert.Contains("_previewGaugeColor = _selectedGaugeColor;", closeWindow, StringComparison.Ordinal);
        Assert.Contains("CloseGaugeColorWindow(discardPendingColor: false);", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void PopupTrackingRegistersBeforeActivationAndIgnoresStaleClosedEvents()
    {
        var mainWindow = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "WindexBar.Windows", "MainWindow.xaml.cs")));
        var sessionPopup = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "WindexBar.Windows", "Dialogs", "SessionDetailsPopup.cs")));
        var hotkeyPopup = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "WindexBar.Windows", "Dialogs", "HotkeyCapturePopup.cs")));

        Assert.Contains("ReferenceEquals(_sessionDetailsWindow, popup)", mainWindow, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(_shortcutWindow, popup)", mainWindow, StringComparison.Ordinal);

        var sessionCreated = sessionPopup.IndexOf("onWindowCreated(popup);", StringComparison.Ordinal);
        var sessionActivated = sessionPopup.IndexOf("popup.Activate();", StringComparison.Ordinal);
        Assert.True(sessionCreated >= 0 && sessionActivated > sessionCreated);

        var hotkeyCreated = hotkeyPopup.IndexOf("onWindowCreated(popup);", StringComparison.Ordinal);
        var hotkeyActivated = hotkeyPopup.IndexOf("popup.Activate();", StringComparison.Ordinal);
        Assert.True(hotkeyCreated >= 0 && hotkeyActivated > hotkeyCreated);
    }

    [Fact]
    public void HomeSectionRendersTheSharedDividerBelowItsTitle()
    {
        var hudView = File.ReadAllText(FindRepositoryFile(Path.Combine(
            "src",
            "WindexBar.Windows",
            "Views",
            "HudViewControl.cs")));

        Assert.Contains("var titleDivider = FeatureViewHelpers.CreateDivider();", hudView, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRow(titleDivider, 1);", hudView, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRow(MetaText, 2);", hudView, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRow(ModelContentPanel, 3);", hudView, StringComparison.Ordinal);
        Assert.Contains("AddLabelValueRow(content, 4, \"Account\"", hudView, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRow(ErrorText, 5);", hudView, StringComparison.Ordinal);
    }

    [Fact]
    public void FastTierHudPulsesOnlyTheLightningIndicators()
    {
        var hudView = File.ReadAllText(FindRepositoryFile(Path.Combine(
            "src",
            "WindexBar.Windows",
            "Views",
            "HudViewControl.cs")));
        var gaugeAnimator = File.ReadAllText(FindRepositoryFile(Path.Combine(
            "src",
            "WindexBar.Windows",
            "UI",
            "GaugeAnimator.cs")));

        Assert.Equal(2, Regex.Matches(hudView, "Text = \"\\\\u26A1\"").Count);
        Assert.Contains("AutoReverse = true", hudView, StringComparison.Ordinal);
        Assert.Contains("RepeatBehavior = RepeatBehavior.Forever", hudView, StringComparison.Ordinal);
        Assert.Contains("indicator.Visibility = isFastTier ? Visibility.Visible : Visibility.Collapsed", hudView, StringComparison.Ordinal);
        Assert.DoesNotContain("labelColor = isFastTier", hudView, StringComparison.Ordinal);
        Assert.DoesNotContain("TrackGlow", gaugeAnimator, StringComparison.Ordinal);
        Assert.DoesNotContain("FillGlow", gaugeAnimator, StringComparison.Ordinal);
    }

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

    public IReadOnlyList<string> Arguments { get; private set; } = [];

    public ICodexRpcTransport Start(string executablePath, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment)
    {
        Arguments = arguments.ToArray();
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
