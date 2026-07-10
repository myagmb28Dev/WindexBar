# Exact Reset Credit Tracking Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace all locally estimated banked reset-credit expiration logic with exact `credits[]` details from Codex app-server.

**Architecture:** Expand the JSON-RPC contract, map exact backend rows directly into the core snapshot, and format only returned timestamps. Remove the persistence/tracker layer so `UsageStore` passes provider data through unchanged; missing detail is represented explicitly by a derived unavailable-expiration count.

**Tech Stack:** C# 14, .NET 10, System.Text.Json source generation, xUnit, WinUI 3 consumer UI

## Global Constraints

- `availableCount` remains the authoritative held-credit total.
- Never synthesize grant or expiration timestamps.
- A missing row or `expiresAt == null` contributes to `UnavailableExpirationCount`.
- Do not read, write, migrate, or delete `%APPDATA%\WindexBar\codex-reset-credits.json`.
- Preserve compatibility with older app-server payloads where `credits` is absent.
- Display exact expiration timestamps in local time.

---

### Task 1: Expand the app-server reset-credit contract

**Files:**
- Modify: `src/WindexBar.Core/Providers/Codex/CodexRpcModels.cs:95-99`
- Test: `tests/WindexBar.Core.Tests/UnitTest1.cs:14-49`

**Interfaces:**
- Consumes: `rateLimitResetCredits` from `account/rateLimits/read`.
- Produces: `RpcRateLimitResetCreditsSummary.Credits` and `RpcRateLimitResetCredit` with the backend field names.

- [ ] **Step 1: Write the failing RPC deserialization assertions**

Change the test payload in `FetchesRateLimitsAndAccountFromLineBasedJsonRpc` to include a detail row:

```csharp
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
```

Add these assertions after the existing available-count assertion:

```csharp
var resetCredit = Assert.Single(limits.RateLimitResetCredits!.Credits!);
Assert.Equal("reset-1", resetCredit.Id);
Assert.Equal(1_751_234_567L, resetCredit.GrantedAt);
Assert.Equal(1_753_826_567L, resetCredit.ExpiresAt);
Assert.Equal("codexRateLimits", resetCredit.ResetType);
Assert.Equal("available", resetCredit.Status);
Assert.Equal("Referral reset", resetCredit.Title);
Assert.Equal("Banked reset", resetCredit.Description);
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet test .\tests\WindexBar.Core.Tests\WindexBar.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~CodexRpcClientTests.FetchesRateLimitsAndAccountFromLineBasedJsonRpc"
```

Expected: compilation fails because `RpcRateLimitResetCreditsSummary` has no `Credits` property.

- [ ] **Step 3: Add the minimal RPC models**

Replace the summary class with:

```csharp
public sealed class RpcRateLimitResetCreditsSummary
{
    [JsonPropertyName("availableCount")]
    public long AvailableCount { get; set; }

    [JsonPropertyName("credits")]
    public List<RpcRateLimitResetCredit>? Credits { get; set; }
}

public sealed class RpcRateLimitResetCredit
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("grantedAt")]
    public long? GrantedAt { get; set; }

    [JsonPropertyName("expiresAt")]
    public long? ExpiresAt { get; set; }

    [JsonPropertyName("resetType")]
    public string? ResetType { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
```

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the Step 2 command again.

Expected: one test passes with zero failures.

- [ ] **Step 5: Commit the contract change**

```powershell
git add src/WindexBar.Core/Providers/Codex/CodexRpcModels.cs tests/WindexBar.Core.Tests/UnitTest1.cs
git commit -m "feat: parse exact reset credit details"
```

---

### Task 2: Replace estimated domain tracking with exact API data

**Files:**
- Modify: `src/WindexBar.Core/Models/UsageSnapshot.cs:59-81`
- Modify: `src/WindexBar.Core/Providers/Codex/CodexUsageMapper.cs:149-156`
- Modify: `src/WindexBar.Core/Refresh/UsageStore.cs:8-24,48,114-125`
- Delete: `src/WindexBar.Core/Refresh/RateLimitResetCreditTracker.cs`
- Modify: `src/WindexBar.Core/WindexBarJsonContext.cs:1-16`
- Test: `tests/WindexBar.Core.Tests/UnitTest1.cs:93-109,570-681,1060-1170,1282-1292`

**Interfaces:**
- Consumes: nullable `IReadOnlyList<RpcRateLimitResetCredit>` from Task 1.
- Produces: `RateLimitResetCredit` and `RateLimitResetCreditsSnapshot.UnavailableExpirationCount` / `NextExpiresAt`.

- [ ] **Step 1: Write failing exact-mapping and pass-through tests**

Replace `MapsRateLimitResetCredits` with tests that assert exact values and missing detail without inference:

```csharp
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
```

Replace `RefreshTracksResetCreditIncreaseFromLocalObservation` with:

```csharp
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
```

Change `FetchResultWithResetCredits` to accept the constructed snapshot:

```csharp
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
```

Delete `RateLimitResetCreditTrackerTests` and `InMemoryRateLimitResetCreditStateStore` from the test file.

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
dotnet test .\tests\WindexBar.Core.Tests\WindexBar.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~MappingTests|FullyQualifiedName~UsageStoreTests.RefreshPreservesProviderResetCreditSnapshot"
```

Expected: compilation fails because the exact domain type and properties do not exist.

- [ ] **Step 3: Add the exact domain model**

Replace the estimated model block in `UsageSnapshot.cs` with:

```csharp
public sealed record RateLimitResetCredit(
    string Id,
    DateTimeOffset GrantedAt,
    DateTimeOffset? ExpiresAt,
    string ResetType,
    string Status,
    string? Title,
    string? Description);

public sealed record RateLimitResetCreditsSnapshot(
    long AvailableCount,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<RateLimitResetCredit>? Credits = null)
{
    public IReadOnlyList<RateLimitResetCredit> Credits { get; init; } =
        Credits ?? Array.Empty<RateLimitResetCredit>();

    public long UnavailableExpirationCount =>
        Math.Max(0, AvailableCount - Credits.LongCount(credit => credit.ExpiresAt is not null));

    public DateTimeOffset? NextExpiresAt => Credits
        .Select(credit => credit.ExpiresAt)
        .Where(expiresAt => expiresAt is not null)
        .Min();
}
```

Remove the now-unused `System.Text.Json.Serialization` import from this file.

- [ ] **Step 4: Map only valid exact rows**

Replace `MapRateLimitResetCredits` and add the helpers:

```csharp
public static RateLimitResetCreditsSnapshot? MapRateLimitResetCredits(
    RpcRateLimitResetCreditsSummary? resetCredits,
    DateTimeOffset now)
{
    if (resetCredits is null)
    {
        return null;
    }

    var credits = (resetCredits.Credits ?? [])
        .Select(MapRateLimitResetCredit)
        .Where(credit => credit is not null)
        .Cast<RateLimitResetCredit>()
        .ToArray();

    return new RateLimitResetCreditsSnapshot(
        Math.Max(0, resetCredits.AvailableCount),
        now,
        credits);
}

private static RateLimitResetCredit? MapRateLimitResetCredit(RpcRateLimitResetCredit credit)
{
    if (string.IsNullOrWhiteSpace(credit.Id)
        || string.IsNullOrWhiteSpace(credit.ResetType)
        || string.IsNullOrWhiteSpace(credit.Status)
        || !TryMapUnixTime(credit.GrantedAt, out var grantedAt))
    {
        return null;
    }

    DateTimeOffset? expiresAt = null;
    if (credit.ExpiresAt is { } expiresAtSeconds
        && TryMapUnixTime(expiresAtSeconds, out var mappedExpiresAt))
    {
        expiresAt = mappedExpiresAt;
    }

    return new RateLimitResetCredit(
        credit.Id,
        grantedAt,
        expiresAt,
        credit.ResetType,
        credit.Status,
        credit.Title,
        credit.Description);
}

private static bool TryMapUnixTime(long? seconds, out DateTimeOffset value)
{
    try
    {
        if (seconds is null)
        {
            value = default;
            return false;
        }

        value = DateTimeOffset.FromUnixTimeSeconds(seconds.Value);
        return true;
    }
    catch (ArgumentOutOfRangeException)
    {
        value = default;
        return false;
    }
}
```

- [ ] **Step 5: Remove persistence and UsageStore mutation**

In `UsageStore`, remove `_resetCreditTracker`, the third constructor argument and initialization, and `TrackResetCredits`. Assign the provider snapshot directly:

```csharp
Snapshot = outcome.Result.Usage;
```

Delete `src/WindexBar.Core/Refresh/RateLimitResetCreditTracker.cs`. Remove `using WindexBar.Core.Refresh;` and `[JsonSerializable(typeof(RateLimitResetCreditState))]` from `WindexBarJsonContext.cs`.

- [ ] **Step 6: Run the focused tests and verify GREEN**

Run the Step 2 command again.

Expected: all selected mapping and pass-through tests pass with zero failures.

- [ ] **Step 7: Commit the exact-domain replacement**

```powershell
git add src/WindexBar.Core/Models/UsageSnapshot.cs src/WindexBar.Core/Providers/Codex/CodexUsageMapper.cs src/WindexBar.Core/Refresh/UsageStore.cs src/WindexBar.Core/Refresh/RateLimitResetCreditTracker.cs src/WindexBar.Core/WindexBarJsonContext.cs tests/WindexBar.Core.Tests/UnitTest1.cs
git commit -m "feat: replace estimated reset credit tracking"
```

---

### Task 3: Format exact expiration times and missing details

**Files:**
- Modify: `src/WindexBar.Core/Formatting/RateLimitResetCreditFormatter.cs`
- Test: `tests/WindexBar.Core.Tests/UnitTest1.cs:683-719`

**Interfaces:**
- Consumes: `RateLimitResetCreditsSnapshot.NextExpiresAt`, `Credits`, and `UnavailableExpirationCount` from Task 2.
- Produces: exact Korean/English summary, detail, and compact strings used by the main window and tray tooltip.

- [ ] **Step 1: Replace the formatter test with exact expectations**

```csharp
[Fact]
public void FormatsExactResetCreditExpirationsAndUnavailableCount()
{
    var now = DateTimeOffset.Parse("2026-07-10T12:00:00+09:00");
    var expiresAt = DateTimeOffset.Parse("2026-08-01T05:05:47+09:00");
    var snapshot = new RateLimitResetCreditsSnapshot(
        4,
        now,
        [
            new RateLimitResetCredit("a", now.AddDays(-8), expiresAt, "codexRateLimits", "available", null, null),
            new RateLimitResetCredit("b", now.AddDays(-8), expiresAt, "codexRateLimits", "available", null, null),
            new RateLimitResetCredit("c", now.AddDays(-8), null, "codexRateLimits", "available", null, null)
        ]);

    Assert.Equal("4개 보유" + Environment.NewLine + "첫 만료 D-22", RateLimitResetCreditFormatter.FormatSummary(snapshot, "ko", now));
    Assert.Equal("4 held" + Environment.NewLine + "First expiry D-22", RateLimitResetCreditFormatter.FormatSummary(snapshot, "en", now));
    var localExpiry = expiresAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    Assert.Equal(
        $"{localExpiry} 만료: 2개" + Environment.NewLine
        + "──────────────" + Environment.NewLine
        + "만료 정보 미제공: 2개",
        RateLimitResetCreditFormatter.FormatDetail(snapshot, "ko", now));
    Assert.Equal(
        $"Expires {localExpiry}: 2" + Environment.NewLine
        + "──────────────" + Environment.NewLine
        + "Expiration unavailable: 2",
        RateLimitResetCreditFormatter.FormatDetail(snapshot, "en", now));
}

[Fact]
public void FormatsUnavailableWhenAppServerReturnsCountOnly()
{
    var snapshot = new RateLimitResetCreditsSnapshot(1, DateTimeOffset.UnixEpoch);

    Assert.Equal("1개 보유" + Environment.NewLine + "만료 정보 미제공", RateLimitResetCreditFormatter.FormatSummary(snapshot, "ko", DateTimeOffset.UnixEpoch));
    Assert.Equal("만료 정보 미제공: 1개", RateLimitResetCreditFormatter.FormatDetail(snapshot, "ko", DateTimeOffset.UnixEpoch));
}
```

- [ ] **Step 2: Run the formatter tests and verify RED**

Run:

```powershell
dotnet test .\tests\WindexBar.Core.Tests\WindexBar.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~RateLimitResetCreditFormatterTests"
```

Expected: tests fail because the formatter still uses estimated properties and relative-day detail labels.

- [ ] **Step 3: Implement exact formatting**

Update `FormatSummary` to read `NextExpiresAt`. Update `FormatDetail` to group `ExpiresAt` values and format the local timestamp with `yyyy-MM-dd HH:mm`. Add unavailable rows based on `UnavailableExpirationCount`. Replace `FormatExpiryBucket` with:

```csharp
private static string FormatExpiryBucket(DateTimeOffset target, string? language)
{
    var local = target.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    return IsKorean(language) ? $"{local} 만료" : $"Expires {local}";
}
```

Use these labels:

```csharp
var unavailableLabel = isKorean ? "만료 정보 미제공" : "Expiration unavailable";
```

Delete the unused `FormatRelative` and `FormatRelativeShort` methods. Keep `FormatDayCode` because it computes a relative label from an exact timestamp.

- [ ] **Step 4: Run formatter tests and verify GREEN**

Run the Step 2 command again.

Expected: both exact formatter tests pass with zero failures.

- [ ] **Step 5: Commit exact formatting**

```powershell
git add src/WindexBar.Core/Formatting/RateLimitResetCreditFormatter.cs tests/WindexBar.Core.Tests/UnitTest1.cs
git commit -m "feat: display exact reset credit expirations"
```

---

### Task 4: Remove estimation documentation and verify the whole solution

**Files:**
- Modify: `README.md:13-17`
- Verify: all files under `src`, `tests`, and the solution

**Interfaces:**
- Consumes: completed exact tracking implementation.
- Produces: user-facing documentation and proof that no estimated-reset-credit code remains.

- [ ] **Step 1: Update README wording**

Replace the feature bullet and estimate paragraph with:

```markdown
- Banked rate-limit reset credit count and exact expiration details when Codex app-server provides them.

Reset-credit expiration dates come directly from Codex app-server. If the installed Codex version or backend returns only the available count, WindexBar shows that expiration details are unavailable instead of estimating a date.
```

- [ ] **Step 2: Prove estimated source paths are gone**

Run:

```powershell
rg -n "EstimatedExpiresAt|FirstSeenAt|EstimatedLifetime|AddDays\(30\)|RateLimitResetCreditTracker|RateLimitResetCreditState|IRateLimitResetCreditStateStore|FileRateLimitResetCreditStateStore|codex-reset-credits\.json|best-effort expiration|Legacy reset credits" src tests README.md
```

Expected: no matches and exit code 1.

- [ ] **Step 3: Run all core tests**

Run:

```powershell
dotnet test .\tests\WindexBar.Core.Tests\WindexBar.Core.Tests.csproj --no-restore
```

Expected: all tests pass with zero failures.

- [ ] **Step 4: Build the solution**

Run:

```powershell
dotnet build .\WindexBar.slnx --no-restore
```

Expected: build succeeds with zero errors.

- [ ] **Step 5: Check the final diff**

Run:

```powershell
git diff --check
git status --short
```

Expected: no whitespace errors; only intended implementation, tests, README, and plan changes are listed.

- [ ] **Step 6: Commit documentation and final cleanup**

```powershell
git add README.md docs/superpowers/plans/2026-07-10-exact-reset-credit-tracking.md
git commit -m "feat: document exact reset credit tracking"
```
