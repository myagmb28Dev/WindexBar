using System.Collections;

namespace WindexBar.Core.Providers.Codex;

public enum RateLimitResetCreditRedemptionOutcome
{
    Reset,
    NothingToReset,
    NoCredit,
    AlreadyRedeemed
}

public sealed record RateLimitResetCreditRedemptionAttempt(
    RateLimitResetCreditRedemptionOutcome? Outcome,
    bool IsInProgress,
    string? ErrorMessage)
{
    public bool IsCompleted => Outcome is not null;

    public static RateLimitResetCreditRedemptionAttempt Completed(RateLimitResetCreditRedemptionOutcome outcome) =>
        new(outcome, IsInProgress: false, ErrorMessage: null);

    public static RateLimitResetCreditRedemptionAttempt InProgress() =>
        new(Outcome: null, IsInProgress: true, ErrorMessage: null);

    public static RateLimitResetCreditRedemptionAttempt Failed(string errorMessage) =>
        new(Outcome: null, IsInProgress: false, ErrorMessage: errorMessage);
}

public interface IRateLimitResetCreditConsumer
{
    Task<RateLimitResetCreditRedemptionOutcome> ConsumeAsync(
        string idempotencyKey,
        string? creditId,
        CancellationToken cancellationToken);
}

public sealed class CodexRateLimitResetCreditConsumer : IRateLimitResetCreditConsumer
{
    private static readonly string[] BaseArguments = ["-s", "read-only", "-a", "never", "app-server"];
    private readonly ICodexRpcTransportFactory _transportFactory;
    private readonly Func<IReadOnlyDictionary<string, string>> _environmentProvider;
    private readonly TimeSpan _initializeTimeout;
    private readonly TimeSpan _requestTimeout;

    public CodexRateLimitResetCreditConsumer(
        ICodexRpcTransportFactory? transportFactory = null,
        Func<IReadOnlyDictionary<string, string>>? environmentProvider = null,
        TimeSpan? initializeTimeout = null,
        TimeSpan? requestTimeout = null)
    {
        _transportFactory = transportFactory ?? new ProcessCodexRpcTransportFactory();
        _environmentProvider = environmentProvider ?? ReadEnvironment;
        _initializeTimeout = initializeTimeout ?? TimeSpan.FromSeconds(8);
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(3);
    }

    public async Task<RateLimitResetCreditRedemptionOutcome> ConsumeAsync(
        string idempotencyKey,
        string? creditId,
        CancellationToken cancellationToken)
    {
        var environment = _environmentProvider();
        var executable = CommandLocator.ResolveExecutable(null, environment)
            ?? throw new FileNotFoundException("Codex executable was not found on PATH. Install Codex or add it to PATH.");

        await using var transport = _transportFactory.Start(executable, BaseArguments, environment);
        await using var client = new CodexRpcClient(transport, _initializeTimeout, _requestTimeout);
        await client.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return await client.ConsumeRateLimitResetCreditAsync(idempotencyKey, creditId, cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyDictionary<string, string> ReadEnvironment() =>
        Environment.GetEnvironmentVariables()
            .Cast<DictionaryEntry>()
            .ToDictionary(
                entry => (string)entry.Key,
                entry => (string)(entry.Value ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);
}

public sealed class RateLimitResetCreditRedemptionCoordinator
{
    private readonly IRateLimitResetCreditConsumer _consumer;
    private readonly Func<string> _idempotencyKeyFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _pendingIdempotencyKey;
    private string? _pendingCreditId;

    public RateLimitResetCreditRedemptionCoordinator(
        IRateLimitResetCreditConsumer consumer,
        Func<string>? idempotencyKeyFactory = null)
    {
        _consumer = consumer;
        _idempotencyKeyFactory = idempotencyKeyFactory ?? (() => Guid.NewGuid().ToString());
    }

    public bool HasPendingAttempt => _pendingIdempotencyKey is not null;

    public string? PendingCreditId => _pendingCreditId;

    public async Task<RateLimitResetCreditRedemptionAttempt> RedeemAsync(
        string? creditId,
        CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return RateLimitResetCreditRedemptionAttempt.InProgress();
        }

        try
        {
            if (_pendingIdempotencyKey is null)
            {
                _pendingIdempotencyKey = CreateIdempotencyKey();
                _pendingCreditId = NormalizeCreditId(creditId);
            }

            try
            {
                var outcome = await _consumer
                    .ConsumeAsync(_pendingIdempotencyKey, _pendingCreditId, cancellationToken)
                    .ConfigureAwait(false);
                _pendingIdempotencyKey = null;
                _pendingCreditId = null;
                return RateLimitResetCreditRedemptionAttempt.Completed(outcome);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                return RateLimitResetCreditRedemptionAttempt.Failed(error.Message);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private string CreateIdempotencyKey()
    {
        var key = _idempotencyKeyFactory();
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("The reset-credit idempotency key factory returned an empty value.");
        }

        return key.Trim();
    }

    private static string? NormalizeCreditId(string? creditId) =>
        string.IsNullOrWhiteSpace(creditId) ? null : creditId.Trim();
}
