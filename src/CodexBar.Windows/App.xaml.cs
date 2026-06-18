using CodexBar.Core.Config;
using CodexBar.Core.Refresh;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinApplication = Microsoft.UI.Xaml.Application;

namespace CodexBar.Windows;

public partial class App : WinApplication
{
    private bool _initialized;

    public App()
    {
        InitializeComponent();
    }

    public static new App Current => (App)WinApplication.Current;

    public SettingsStore SettingsStore { get; private set; } = null!;
    public UsageStore UsageStore { get; private set; } = null!;
    public TrayIconService TrayIconService { get; private set; } = null!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (_initialized)
        {
            return;
        }

        SettingsStore = new SettingsStore(new CodexBarConfigStore());
        UsageStore = new UsageStore(SettingsStore);
        TrayIconService = new TrayIconService(SettingsStore, UsageStore, DispatcherQueue.GetForCurrentThread());
        UsageStore.StartBackgroundRefresh();
        TrayIconService.ShowStatusWindow();
        _initialized = true;
    }

    public void Shutdown()
    {
        TrayIconService.Dispose();
        UsageStore.Dispose();
        Exit();
    }
}
