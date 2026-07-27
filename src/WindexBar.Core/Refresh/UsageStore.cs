using WindexBar.Core.Config;
using WindexBar.Core.Models;
using WindexBar.Core.Providers;
using WindexBar.Core.Providers.Codex;

namespace WindexBar.Core.Refresh;

public sealed class UsageStore : IDisposable
{
    private readonly SettingsStore _settings;
    private readonly ProviderDescriptor _codexDescriptor;
    private readonly WeeklyLimitImpactTracker? _weeklyLimitImpactTracker;
    private readonly RateLimitAlertTracker? _rateLimitAlertTracker;
    private readonly object _rateLimitAlertLock = new();
    private IReadOnlyList<RateLimitThresholdAlert> _pendingRateLimitAlerts = [];
    private readonly object _sessionIndexWatcherLock = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly BackgroundRefreshDelayPolicy _refreshDelayPolicy = new();
    private CancellationTokenSource? _loopCts;
    private CancellationTokenSource? _sessionIndexDebounceCts;
    private FileSystemWatcher? _sessionIndexWatcher;

    public UsageStore(
        SettingsStore settings,
        ProviderDescriptor? codexDescriptor = null,
        WeeklyLimitImpactTracker? weeklyLimitImpactTracker = null,
        RateLimitAlertTracker? rateLimitAlertTracker = null)
    {
        _settings = settings;
        _codexDescriptor = codexDescriptor ?? CodexProviderDescriptor.Create();
        _weeklyLimitImpactTracker = weeklyLimitImpactTracker;
        _rateLimitAlertTracker = rateLimitAlertTracker;
    }

    public UsageSnapshot? Snapshot { get; private set; }
    public CreditsSnapshot? Credits { get; private set; }
    public string? LastError { get; private set; }
    public bool IsRefreshing { get; private set; }

    public IReadOnlyList<RateLimitThresholdAlert> TakeRateLimitAlerts()
    {
        lock (_rateLimitAlertLock)
        {
            var alerts = _pendingRateLimitAlerts;
            _pendingRateLimitAlerts = [];
            return alerts;
        }
    }

    public event EventHandler? Changed;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var succeeded = await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
            _refreshDelayPolicy.RecordResult(succeeded);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<bool> RefreshCoreAsync(CancellationToken cancellationToken)
    {
        var providerConfig = _settings.Codex;
        if (!providerConfig.Enabled)
        {
            Snapshot = null;
            Credits = null;
            LastError = null;
            lock (_rateLimitAlertLock)
            {
                _pendingRateLimitAlerts = [];
            }
            OnChanged();
            return true;
        }

        IsRefreshing = true;
        try
        {
            var context = new ProviderFetchContext(
                UsageProvider.Codex,
                Environment.GetEnvironmentVariables()
                    .Cast<System.Collections.DictionaryEntry>()
                    .ToDictionary(e => (string)e.Key, e => (string)(e.Value ?? string.Empty), StringComparer.OrdinalIgnoreCase),
                IncludeCredits: true,
                InitializeTimeout: TimeSpan.FromSeconds(8),
                RequestTimeout: TimeSpan.FromSeconds(3));
            var outcome = await _codexDescriptor.FetchPipeline.FetchAsync(context, cancellationToken).ConfigureAwait(false);
            if (outcome.Result is null)
            {
                LastError = outcome.ErrorDescription;
                return false;
            }

            Snapshot = _weeklyLimitImpactTracker?.Apply(outcome.Result.Usage) ?? outcome.Result.Usage;
            lock (_rateLimitAlertLock)
            {
                _pendingRateLimitAlerts = _settings.Config.RateLimitAlerts.Enabled
                    ? _rateLimitAlertTracker?.Apply(Snapshot) ?? []
                    : [];
            }
            Credits = outcome.Result.Credits;
            LastError = null;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            LastError = error.Message;
            return false;
        }
        finally
        {
            IsRefreshing = false;
            OnChanged();
        }
    }

    public void StartBackgroundRefresh()
    {
        StopBackgroundRefresh();
        var loopCts = new CancellationTokenSource();
        var loopToken = loopCts.Token;
        _refreshDelayPolicy.Reset();
        _loopCts = loopCts;
        StartSessionIndexWatcher();
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    await RefreshAsync(loopToken).ConfigureAwait(false);
                    await Task.Delay(_refreshDelayPolicy.NextDelay, loopToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (loopToken.IsCancellationRequested)
            {
            }
        }, loopToken);
    }

    public void StopBackgroundRefresh()
    {
        StopSessionIndexWatcher();
        _loopCts?.Cancel();
        _loopCts?.Dispose();
        _loopCts = null;
    }

    private void StartSessionIndexWatcher()
    {
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrWhiteSpace(codexHome))
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            codexHome = string.IsNullOrWhiteSpace(userProfile) ? null : Path.Combine(userProfile, ".codex");
        }

        if (string.IsNullOrWhiteSpace(codexHome) || !Directory.Exists(codexHome))
        {
            return;
        }

        _sessionIndexWatcher = new FileSystemWatcher(codexHome, "session_index.jsonl")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        _sessionIndexWatcher.Changed += OnSessionIndexChanged;
        _sessionIndexWatcher.Created += OnSessionIndexChanged;
        _sessionIndexWatcher.Renamed += OnSessionIndexChanged;
        _sessionIndexWatcher.EnableRaisingEvents = true;
    }

    private void OnSessionIndexChanged(object sender, FileSystemEventArgs args)
    {
        CancellationTokenSource debounceCts;
        lock (_sessionIndexWatcherLock)
        {
            _sessionIndexDebounceCts?.Cancel();
            _sessionIndexDebounceCts?.Dispose();
            debounceCts = _loopCts is null
                ? new CancellationTokenSource()
                : CancellationTokenSource.CreateLinkedTokenSource(_loopCts.Token);
            _sessionIndexDebounceCts = debounceCts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, debounceCts.Token).ConfigureAwait(false);
                await RefreshAsync(debounceCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (debounceCts.IsCancellationRequested)
            {
            }
        }, debounceCts.Token);
    }

    private void StopSessionIndexWatcher()
    {
        lock (_sessionIndexWatcherLock)
        {
            _sessionIndexDebounceCts?.Cancel();
            _sessionIndexDebounceCts?.Dispose();
            _sessionIndexDebounceCts = null;

            if (_sessionIndexWatcher is not null)
            {
                _sessionIndexWatcher.EnableRaisingEvents = false;
                _sessionIndexWatcher.Changed -= OnSessionIndexChanged;
                _sessionIndexWatcher.Created -= OnSessionIndexChanged;
                _sessionIndexWatcher.Renamed -= OnSessionIndexChanged;
                _sessionIndexWatcher.Dispose();
                _sessionIndexWatcher = null;
            }
        }
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose() => StopBackgroundRefresh();
}

