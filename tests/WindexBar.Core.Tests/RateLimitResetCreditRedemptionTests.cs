using System.Text.Json;
using WindexBar.Core.Presentation;
using WindexBar.Core.Providers.Codex;

namespace WindexBar.Core.Tests;

public sealed class RateLimitResetCreditRedemptionTests
{
    [Theory]
    [InlineData("reset", RateLimitResetCreditRedemptionOutcome.Reset)]
    [InlineData("nothingToReset", RateLimitResetCreditRedemptionOutcome.NothingToReset)]
    [InlineData("noCredit", RateLimitResetCreditRedemptionOutcome.NoCredit)]
    [InlineData("alreadyRedeemed", RateLimitResetCreditRedemptionOutcome.AlreadyRedeemed)]
    public async Task RpcClientConsumesResetCreditWithIdempotencyKey(
        string rpcOutcome,
        RateLimitResetCreditRedemptionOutcome expected)
    {
        var transport = new FakeCodexRpcTransport(
            Reply(1, new { ok = true }),
            Reply(2, new { outcome = rpcOutcome }));
        await using var client = new CodexRpcClient(
            transport,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
        await client.InitializeAsync(CancellationToken.None);

        var outcome = await client.ConsumeRateLimitResetCreditAsync(
            "attempt-123",
            "credit-456",
            CancellationToken.None);

        Assert.Equal(expected, outcome);
        Assert.Contains("\"method\":\"account/rateLimitResetCredit/consume\"", transport.Writes[2], StringComparison.Ordinal);
        Assert.Contains("\"idempotencyKey\":\"attempt-123\"", transport.Writes[2], StringComparison.Ordinal);
        Assert.Contains("\"creditId\":\"credit-456\"", transport.Writes[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RpcClientLetsBackendSelectCreditWhenIdIsOmitted()
    {
        var transport = new FakeCodexRpcTransport(
            Reply(1, new { ok = true }),
            Reply(2, new { outcome = "reset" }));
        await using var client = new CodexRpcClient(
            transport,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
        await client.InitializeAsync(CancellationToken.None);

        await client.ConsumeRateLimitResetCreditAsync("attempt-123", null, CancellationToken.None);

        Assert.DoesNotContain("creditId", transport.Writes[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConsumerUsesSupportedReadOnlyAppServerArguments()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var binDir = Path.Combine(testRoot, "bin");
        Directory.CreateDirectory(binDir);
        File.WriteAllText(Path.Combine(binDir, "codex.cmd"), "@echo off\r\n");
        try
        {
            var transportFactory = new QueueCodexRpcTransportFactory(
            [
                [
                    Reply(1, new { ok = true }),
                    Reply(2, new { outcome = "reset" })
                ]
            ]);
            var consumer = new CodexRateLimitResetCreditConsumer(
                transportFactory,
                () => new Dictionary<string, string>
                {
                    ["PATH"] = binDir,
                    ["PATHEXT"] = ".CMD"
                },
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1));

            var outcome = await consumer.ConsumeAsync("attempt-123", null, CancellationToken.None);

            Assert.Equal(RateLimitResetCreditRedemptionOutcome.Reset, outcome);
            Assert.Equal(["-s", "read-only", "-a", "never", "app-server"], transportFactory.Arguments);
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CoordinatorReusesIdempotencyKeyAfterAmbiguousFailure()
    {
        var keys = new List<string>();
        var creditIds = new List<string?>();
        var callCount = 0;
        var consumer = new DelegateResetCreditConsumer((key, creditId, _) =>
        {
            keys.Add(key);
            creditIds.Add(creditId);
            callCount++;
            return callCount == 1
                ? Task.FromException<RateLimitResetCreditRedemptionOutcome>(new TimeoutException("timed out"))
                : Task.FromResult(RateLimitResetCreditRedemptionOutcome.AlreadyRedeemed);
        });
        var coordinator = new RateLimitResetCreditRedemptionCoordinator(consumer, () => "logical-attempt");

        var failed = await coordinator.RedeemAsync("credit-a", CancellationToken.None);
        Assert.Equal("credit-a", coordinator.PendingCreditId);
        var retried = await coordinator.RedeemAsync("credit-b", CancellationToken.None);

        Assert.Equal("timed out", failed.ErrorMessage);
        Assert.True(retried.IsCompleted);
        Assert.Equal(RateLimitResetCreditRedemptionOutcome.AlreadyRedeemed, retried.Outcome);
        Assert.Equal(["logical-attempt", "logical-attempt"], keys);
        Assert.Equal(["credit-a", "credit-a"], creditIds);
        Assert.False(coordinator.HasPendingAttempt);
        Assert.Null(coordinator.PendingCreditId);
    }

    [Fact]
    public async Task CoordinatorRejectsConcurrentDuplicateAttempt()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumer = new DelegateResetCreditConsumer(async (_, _, _) =>
        {
            started.SetResult();
            await release.Task;
            return RateLimitResetCreditRedemptionOutcome.Reset;
        });
        var coordinator = new RateLimitResetCreditRedemptionCoordinator(consumer, () => "logical-attempt");

        var first = coordinator.RedeemAsync(null, CancellationToken.None);
        await started.Task;
        var duplicate = await coordinator.RedeemAsync(null, CancellationToken.None);
        release.SetResult();

        Assert.True(duplicate.IsInProgress);
        Assert.Equal(RateLimitResetCreditRedemptionOutcome.Reset, (await first).Outcome);
        Assert.Equal(1, consumer.CallCount);
    }

    [Theory]
    [InlineData(RateLimitResetCreditRedemptionOutcome.Reset, true, "Reset complete")]
    [InlineData(RateLimitResetCreditRedemptionOutcome.AlreadyRedeemed, true, "Reset already completed")]
    [InlineData(RateLimitResetCreditRedemptionOutcome.NothingToReset, false, "Nothing to reset")]
    [InlineData(RateLimitResetCreditRedemptionOutcome.NoCredit, false, "No reset credit available")]
    public void FormatsEveryServerOutcome(
        RateLimitResetCreditRedemptionOutcome outcome,
        bool expectedSuccess,
        string expectedTitle)
    {
        var display = RateLimitResetCreditRedemptionDisplayModelFactory.Create(
            RateLimitResetCreditRedemptionAttempt.Completed(outcome),
            "en");

        Assert.Equal(expectedTitle, display.Title);
        Assert.Equal(expectedSuccess, display.IsSuccess);
        Assert.NotEmpty(display.Message);
    }

    [Fact]
    public void AmbiguousFailureExplainsSafeRetry()
    {
        var display = RateLimitResetCreditRedemptionDisplayModelFactory.Create(
            RateLimitResetCreditRedemptionAttempt.Failed("network timeout"),
            "ko");

        Assert.Contains("중복 소비되지 않아", display.Message, StringComparison.Ordinal);
        Assert.Contains("network timeout", display.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResetCreditPopupRequiresConfirmationAndRefreshesAuthoritativeState()
    {
        var resetCreditBank = File.ReadAllText(FindRepositoryFile(
            Path.Combine("src", "WindexBar.Windows", "Dialogs", "ResetCreditBankDialog.cs")));
        var confirmationIndex = resetCreditBank.IndexOf("await ShowConfirmationAsync", StringComparison.Ordinal);
        var redemptionIndex = resetCreditBank.IndexOf("_redemptionCoordinator.RedeemAsync", StringComparison.Ordinal);

        Assert.Contains("AddResetCreditRedemptionRow", resetCreditBank, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(useButton, 1)", resetCreditBank, StringComparison.Ordinal);
        Assert.Contains("ConfirmAndRedeemResetCreditAsync(credit)", resetCreditBank, StringComparison.Ordinal);
        Assert.Contains("FormatRedemptionTarget", resetCreditBank, StringComparison.Ordinal);
        Assert.DoesNotContain("Credit ID:", resetCreditBank, StringComparison.Ordinal);
        Assert.Contains("OwnedPopupWindow.Create", resetCreditBank, StringComparison.Ordinal);
        Assert.Contains("CreateTargetCard", resetCreditBank, StringComparison.Ordinal);
        Assert.Contains("TaskCompletionSource<bool>", resetCreditBank, StringComparison.Ordinal);
        Assert.DoesNotContain("var confirmation = new ContentDialog", resetCreditBank, StringComparison.Ordinal);
        Assert.True(confirmationIndex >= 0 && redemptionIndex > confirmationIndex);
        Assert.Contains("_isRedemptionInProgress", resetCreditBank, StringComparison.Ordinal);
        Assert.Contains("creditId: targetCredit?.Id", resetCreditBank, StringComparison.Ordinal);
        Assert.Contains("await _usageStore.RefreshAsync", resetCreditBank, StringComparison.Ordinal);
        Assert.Contains("RateLimitResetCreditRedemptionDisplayModelFactory.Create", resetCreditBank, StringComparison.Ordinal);
        Assert.Contains("_closeButton.IsEnabled = !_isRedemptionInProgress", resetCreditBank, StringComparison.Ordinal);
    }

    private static string Reply(int id, object result) =>
        JsonSerializer.Serialize(new { id, result });

    private static string FindRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WindexBar.slnx")))
            {
                return Path.Combine(directory.FullName, relativePath);
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the WindexBar repository root.");
    }

    private sealed class DelegateResetCreditConsumer(
        Func<string, string?, CancellationToken, Task<RateLimitResetCreditRedemptionOutcome>> consume)
        : IRateLimitResetCreditConsumer
    {
        public int CallCount { get; private set; }

        public Task<RateLimitResetCreditRedemptionOutcome> ConsumeAsync(
            string idempotencyKey,
            string? creditId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return consume(idempotencyKey, creditId, cancellationToken);
        }
    }
}
