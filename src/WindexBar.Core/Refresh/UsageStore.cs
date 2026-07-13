using WindexBar.Core.Config;
using WindexBar.Core.Models;
using WindexBar.Core.Providers;
using WindexBar.Core.Providers.Codex;

namespace WindexBar.Core.Refresh;

public sealed class UsageStore : IDisposable
{
    private readonly SettingsStore _settings;
    private readonly ProviderDescriptor _codexDescriptor;
    private readonly object _sessionIndexWatcherLock = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _loopCts;
    private CancellationTokenSource? _sessionIndexDebounceCts;
    private FileSystemWatcher? _sessionIndexWatcher;

    public UsageStore(
        SettingsStore settings,
        ProviderDescriptor? codexDescriptor = null)
    {
        _settings = settings;
        _codexDescriptor = codexDescriptor ?? CodexProviderDescriptor.Create();
    }

    public UsageSnapshot? Snapshot { get; private set; }
    public CreditsSnapshot? Credits { get; private set; }
    public string? LastError { get; private set; }
    public string? LastSourceLabel { get; private set; }
    public bool IsRefreshing { get; private set; }

    public event EventHandler? Changed;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        var providerConfig = _settings.Codex;
        if (!providerConfig.Enabled)
        {
            Snapshot = null;
            Credits = null;
            LastError = null;
            LastSourceLabel = null;
            OnChanged();
            return;
        }

        IsRefreshing = true;
        OnChanged();
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
                return;
            }

            Snapshot = outcome.Result.Usage;
            Credits = outcome.Result.Credits;
            LastSourceLabel = outcome.Result.SourceLabel;
            LastError = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            LastError = error.Message;
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
        _loopCts = new CancellationTokenSource();
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(_settings.Codex.RefreshIntervalSeconds));
        StartSessionIndexWatcher();
        _ = Task.Run(async () =>
        {
            await RefreshAsync(_loopCts.Token).ConfigureAwait(false);
            while (_timer is not null && await _timer.WaitForNextTickAsync(_loopCts.Token).ConfigureAwait(false))
            {
                await RefreshAsync(_loopCts.Token).ConfigureAwait(false);
            }
        }, _loopCts.Token);
    }

    public void StopBackgroundRefresh()
    {
        StopSessionIndexWatcher();
        _loopCts?.Cancel();
        _loopCts?.Dispose();
        _loopCts = null;
        _timer?.Dispose();
        _timer = null;
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

