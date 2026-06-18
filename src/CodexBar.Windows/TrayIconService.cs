using CodexBar.Core.Config;
using CodexBar.Core.Models;
using CodexBar.Core.Refresh;
using Microsoft.UI.Dispatching;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace CodexBar.Windows;

public sealed class TrayIconService : IDisposable
{
    private readonly SettingsStore _settingsStore;
    private readonly UsageStore _usageStore;
    private readonly DispatcherQueue _dispatcher;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Drawing.Icon _defaultIcon;
    private MainWindow? _statusWindow;
    private bool _disposed;

    public TrayIconService(SettingsStore settingsStore, UsageStore usageStore, DispatcherQueue dispatcher)
    {
        _settingsStore = settingsStore;
        _usageStore = usageStore;
        _dispatcher = dispatcher;
        _defaultIcon = LoadIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _defaultIcon,
            Text = "Win CodexBar",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _notifyIcon.MouseClick += OnMouseClick;
        _usageStore.Changed += OnUsageChanged;
        UpdateTooltip();
    }

    public void ShowStatusWindow()
    {
        if (_disposed)
        {
            return;
        }

        var window = GetOrCreateStatusWindow();
        window.ShowHudView();
        window.Activate();
    }

    public void ShowSettingsWindow()
    {
        if (_disposed)
        {
            return;
        }

        var window = GetOrCreateStatusWindow();
        window.ShowSettingsView();
        window.Activate();
    }

    private MainWindow GetOrCreateStatusWindow()
    {
        if (_statusWindow is null)
        {
            _statusWindow = new MainWindow(_usageStore, _settingsStore);
            _statusWindow.Closed += (_, _) => _statusWindow = null;
        }

        return _statusWindow;
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Settings", null, (_, _) => _dispatcher.TryEnqueue(ShowSettingsWindow));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => _dispatcher.TryEnqueue(App.Current.Shutdown));
        return menu;
    }

    private void OnMouseClick(object? sender, Forms.MouseEventArgs args)
    {
        if (args.Button == Forms.MouseButtons.Left)
        {
            _dispatcher.TryEnqueue(ShowStatusWindow);
        }
    }

    private void OnUsageChanged(object? sender, EventArgs args)
    {
        _dispatcher.TryEnqueue(UpdateTooltip);
    }

    private void UpdateTooltip()
    {
        _notifyIcon.Text = TooltipText(_usageStore.Snapshot, _usageStore.Credits, _usageStore.LastError);
    }

    private static string TooltipText(UsageSnapshot? snapshot, CreditsSnapshot? credits, string? error)
    {
        if (!string.IsNullOrWhiteSpace(error) && snapshot is null)
        {
            return TrimTooltip($"Win CodexBar - {error}");
        }

        if (snapshot?.Primary is null)
        {
            return "Win CodexBar - Codex usage unknown";
        }

        var creditsText = credits is null ? string.Empty : $", credits {credits.Remaining:0.##}";
        return TrimTooltip($"Win CodexBar - session {snapshot.Primary.RemainingPercent:0.#}% left{creditsText}");
    }

    private static string TrimTooltip(string text) => text.Length <= 63 ? text : text[..63];

    private static Drawing.Icon LoadIcon()
    {
        foreach (var fileName in new[] { "TrayIcon.ico", "AppIcon.ico" })
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
            if (File.Exists(iconPath))
            {
                return new Drawing.Icon(iconPath);
            }
        }

        return Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _usageStore.Changed -= OnUsageChanged;
        _notifyIcon.MouseClick -= OnMouseClick;
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _defaultIcon.Dispose();
    }
}
