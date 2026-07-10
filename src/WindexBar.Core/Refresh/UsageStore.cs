using WindexBar.Core.Config;
using WindexBar.Core.Models;
using WindexBar.Core.Providers;
using WindexBar.Core.Providers.Codex;

namespace WindexBar.Core.Refresh;

public sealed class UsageStore : IDisposable
{
    private readonly SettingsStore _settings;
    private readonly ProviderDescriptor _codexDescriptor;
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _loopCts;

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
        _loopCts?.Cancel();
        _loopCts?.Dispose();
        _loopCts = null;
        _timer?.Dispose();
        _timer = null;
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose() => StopBackgroundRefresh();
}

