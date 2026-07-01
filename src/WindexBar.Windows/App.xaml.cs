using WindexBar.Core.Config;
using WindexBar.Core.Refresh;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinApplication = Microsoft.UI.Xaml.Application;

namespace WindexBar.Windows;

public partial class App : WinApplication
{
    private const string AppMutexName = @"Local\WindexBar";

    private bool _initialized;
    private Mutex? _singleInstanceMutex;

    public App()
    {
        InitializeComponent();
        LogMessage("App constructed.");
    }

    public static new App Current => (App)WinApplication.Current;

    public SettingsStore SettingsStore { get; private set; } = null!;
    public UsageStore UsageStore { get; private set; } = null!;
    public TrayIconService TrayIconService { get; private set; } = null!;

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

        UsageStore = new UsageStore(SettingsStore);
        TrayIconService = new TrayIconService(SettingsStore, UsageStore, DispatcherQueue.GetForCurrentThread());
        LogMessage("Tray icon service created.");
        UsageStore.StartBackgroundRefresh();
        TrayIconService.ShowStatusWindow();
        LogMessage("ShowStatusWindow call completed.");
        _initialized = true;
    }

    public void Shutdown()
    {
        TrayIconService.Dispose();
        UsageStore.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        Exit();
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
