using WindexBar.Core.Config;
using WindexBar.Core.Refresh;
using WindexBar.Core.Updates;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System.Diagnostics;
using WinApplication = Microsoft.UI.Xaml.Application;

namespace WindexBar.Windows;

public partial class App : WinApplication
{
    private const string AppMutexName = @"Local\WindexBar";

    private bool _initialized;
    private Mutex? _singleInstanceMutex;
    private HttpClient? _httpClient;
    private HttpClient? _appUpdateHttpClient;
    private CancellationTokenSource? _appUpdateCancellation;
    private bool _shuttingDown;

    public App()
    {
        InitializeComponent();
        LogMessage("App constructed.");
    }

    public static new App Current => (App)WinApplication.Current;

    public SettingsStore SettingsStore { get; private set; } = null!;
    public UsageStore UsageStore { get; private set; } = null!;
    public TrayIconService TrayIconService { get; private set; } = null!;
    public CodexCliUpdateService CodexCliUpdateService { get; private set; } = null!;
    public AppUpdateService AppUpdateService { get; private set; } = null!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        LogMessage("OnLaunched entered.");
        if (_initialized)
        {
            LogMessage("OnLaunched skipped because app is already initialized.");
            return;
        }

        SettingsStore = new SettingsStore(new WindexBarConfigStore());
        StartupShortcutService.RemoveIfDisabled(SettingsStore.Config.StartWithWindows);
        LogMessage("Settings loaded.");
        _singleInstanceMutex = new Mutex(initiallyOwned: true, AppMutexName, out var ownsMutex);
        if (!ownsMutex)
        {
            LogMessage("Another WindexBar instance is already running. Exiting.");
            Exit();
            return;
        }

        UsageStore = new UsageStore(
            SettingsStore,
            weeklyLimitImpactTracker: new WeeklyLimitImpactTracker(new WeeklyLimitImpactStateStore()));
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _appUpdateHttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        CodexCliUpdateService = new CodexCliUpdateService(
            new GithubCodexVersionSource(_httpClient),
            new CodexProcessRunner());
        AppUpdateService = new AppUpdateService(
            _appUpdateHttpClient,
            new GithubAppReleaseSource(_appUpdateHttpClient),
            new RsaAppUpdateManifestVerifier(
                UpdateSigningPublicKey.ModulusBase64,
                UpdateSigningPublicKey.ExponentBase64));
        TrayIconService = new TrayIconService(
            SettingsStore,
            UsageStore,
            CodexCliUpdateService,
            DispatcherQueue.GetForCurrentThread());
        LogMessage("Tray icon service created.");
        TrayIconService.ShowStatusWindow();
        LogMessage("ShowStatusWindow call completed.");
        _initialized = true;
        HandlePreviousAppUpdate();
        _appUpdateCancellation = new CancellationTokenSource();
        _ = CheckForAppUpdateAsync(_appUpdateCancellation.Token);
    }

    public void Shutdown()
    {
        if (_shuttingDown)
        {
            return;
        }

        _shuttingDown = true;
        _appUpdateCancellation?.Cancel();
        TrayIconService.Dispose();
        UsageStore.Dispose();
        _httpClient?.Dispose();
        _appUpdateHttpClient?.Dispose();
        _appUpdateCancellation?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        Exit();
    }

    private void HandlePreviousAppUpdate()
    {
        var pendingText = SettingsStore.Config.AppUpdates.PendingVersion;
        if (!AppVersion.TryParse(pendingText, out var pending)
            || !AppVersion.TryParse(AppReleaseVersion.Value, out var current))
        {
            return;
        }

        if (current >= pending)
        {
            SettingsStore.Update(config =>
            {
                config.AppUpdates.PendingVersion = null;
                config.AppUpdates.LastFailure = null;
                config.AppUpdates.RetryAfter = null;
            });
            LogMessage($"Automatic update to {current} completed.");
            return;
        }

        var message = $"WindexBar could not finish updating to {pending}. The current version is {current}.";
        AppUpdateService.RecordFailure(SettingsStore.Config.AppUpdates, message);
        SettingsStore.Save();
        LogMessage(message);
        TrayIconService.ShowError("WindexBar update failed", message);
    }

    private async Task CheckForAppUpdateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            if (!AppVersion.TryParse(AppReleaseVersion.Value, out var currentVersion))
            {
                LogMessage($"Automatic update skipped because version '{AppReleaseVersion.Value}' is invalid.");
                return;
            }

            var stagingRoot = Path.Combine(Path.GetTempPath(), "WindexBar", "Updates");
            var update = await AppUpdateService.CheckAndStageAsync(
                SettingsStore.Config.AppUpdates,
                currentVersion,
                stagingRoot,
                force: false,
                cancellationToken);
            SettingsStore.Save();
            if (update is null || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            SettingsStore.Update(config =>
            {
                config.AppUpdates.PendingVersion = update.Version.ToString();
                config.AppUpdates.LastFailure = null;
            });
            LogMessage($"Starting automatic update to {update.Version}. SHA-256: {update.Sha256}");
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = update.InstallerPath,
                Arguments = "/VERYSILENT /NORESTART /CLOSEAPPLICATIONS /AUTOUPDATE=1",
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(update.InstallerPath)!
            });
            if (process is null)
            {
                throw new InvalidOperationException("The WindexBar installer could not be started.");
            }

            Shutdown();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (HttpRequestException error)
        {
            LogMessage($"Automatic update check deferred: {error.Message}");
        }
        catch (Exception error)
        {
            LogMessage($"Automatic update failed: {error}");
            AppUpdateService.RecordFailure(SettingsStore.Config.AppUpdates, error.Message);
            SettingsStore.Save();
            TrayIconService.ShowError("WindexBar update failed", error.Message);
        }
    }

    private static void LogMessage(string message)
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var logDir = Path.Combine(appData, "WindexBar");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(
                Path.Combine(logDir, "windexbar.log"),
                $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
