using System.Globalization;
using WindexBar.Core.Config;
using WindexBar.Core.Models;
using WindexBar.Core.Refresh;
using Microsoft.UI.Dispatching;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace WindexBar.Windows;

public sealed class TrayIconService : IDisposable
{
    private readonly SettingsStore _settingsStore;
    private readonly UsageStore _usageStore;
    private readonly DispatcherQueue _dispatcher;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Drawing.Icon _defaultIcon;
    private MainWindow? _statusWindow;
    private string? _uiError;
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
            Text = "WindexBar",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _notifyIcon.MouseClick += OnMouseClick;
        _notifyIcon.MouseDoubleClick += OnMouseDoubleClick;
        _notifyIcon.DoubleClick += OnDoubleClick;
        _usageStore.Changed += OnUsageChanged;
        UpdateTooltip();
    }

    public void ShowStatusWindow()
    {
        if (_disposed)
        {
            return;
        }

        LogMessage("ShowStatusWindow requested.");
        TryShowWindow(window =>
        {
            window.ShowHudView();
            var status = WindowCloseBehavior.Show(window);
            LogMessage($"WindexBar window show requested for {status}.");
        });
    }

    public void ShowSettingsWindow()
    {
        if (_disposed)
        {
            return;
        }

        LogMessage("ShowSettingsWindow requested.");
        TryShowWindow(window =>
        {
            window.ShowSettingsView();
            var status = WindowCloseBehavior.Show(window);
            LogMessage($"WindexBar settings window show requested for {status}.");
        });
    }

    private void TryShowWindow(Action<MainWindow> show)
    {
        try
        {
            var window = GetOrCreateStatusWindow();
            show(window);
            _uiError = null;
            LogMessage("WindexBar window shown.");
        }
        catch (Exception error)
        {
            _statusWindow = null;
            _uiError = error.Message;
            LogMessage("Failed to show WindexBar window.", error);
        }
        finally
        {
            UpdateTooltip();
        }
    }

    private MainWindow GetOrCreateStatusWindow()
    {
        if (_statusWindow is null)
        {
            _statusWindow = new MainWindow(_usageStore, _settingsStore);
            _statusWindow.Closed += (_, _) =>
            {
                LogMessage("WindexBar window closed.");
                _statusWindow = null;
            };
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

    private void OnMouseDoubleClick(object? sender, Forms.MouseEventArgs args)
    {
        if (args.Button == Forms.MouseButtons.Left)
        {
            _dispatcher.TryEnqueue(ShowStatusWindow);
        }
    }

    private void OnDoubleClick(object? sender, EventArgs args)
    {
        _dispatcher.TryEnqueue(ShowStatusWindow);
    }

    private void OnUsageChanged(object? sender, EventArgs args)
    {
        _dispatcher.TryEnqueue(UpdateTooltip);
    }

    private void UpdateTooltip()
    {
        _notifyIcon.Text = TooltipText(_usageStore.Snapshot, _usageStore.Credits, _uiError ?? _usageStore.LastError);
    }

    private static string TooltipText(UsageSnapshot? snapshot, CreditsSnapshot? credits, string? error)
    {
        if (!string.IsNullOrWhiteSpace(error) && snapshot is null)
        {
            return TrimTooltip($"WindexBar - {error}");
        }

        if (snapshot?.Primary is null)
        {
            var tokenOnlyText = TooltipTokenText(snapshot?.TokenUsage);
            return tokenOnlyText is null
                ? "WindexBar - Codex usage unknown"
                : TrimTooltip($"WindexBar - tokens {tokenOnlyText}");
        }

        var creditsText = credits is null ? string.Empty : $", credits {credits.Remaining:0.##}";
        var tokenText = TooltipTokenText(snapshot.TokenUsage);
        var tokens = tokenText is null ? string.Empty : $", tokens {tokenText}";
        return TrimTooltip($"WindexBar - session {snapshot.Primary.RemainingPercent:0.#}% left{creditsText}{tokens}");
    }

    private static string TrimTooltip(string text) => text.Length <= 63 ? text : text[..63];

    private static string? TooltipTokenText(TokenUsageSnapshot? tokenUsage)
    {
        var contextPercent = TokenContextPercent(tokenUsage);
        if (contextPercent is not null)
        {
            return $"ctx {contextPercent.Value.ToString("0.#", CultureInfo.InvariantCulture)}%";
        }

        var sessionTokens = tokenUsage?.Total?.TotalTokens;
        return sessionTokens is null ? null : $"session {FormatTokenCount(sessionTokens.Value)}";
    }

    private static double? TokenContextPercent(TokenUsageSnapshot? tokenUsage)
    {
        var current = tokenUsage?.Last ?? tokenUsage?.Total;
        if (current is null || tokenUsage?.ModelContextWindow is not { } contextWindow || contextWindow <= 0)
        {
            return null;
        }

        return Math.Clamp(current.TotalTokens * 100d / contextWindow, 0, 100);
    }

    private static string FormatTokenCount(long tokens)
    {
        var magnitude = Math.Abs(tokens);
        if (magnitude >= 1_000_000)
        {
            return (tokens / 1_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + "M";
        }

        if (magnitude >= 1_000)
        {
            return (tokens / 1_000d).ToString("0.#", CultureInfo.InvariantCulture) + "K";
        }

        return tokens.ToString(CultureInfo.InvariantCulture);
    }

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

    private static void LogMessage(string message, Exception? error = null)
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var logDir = Path.Combine(appData, "WindexBar");
            Directory.CreateDirectory(logDir);
            var detail = error is null ? string.Empty : $"{Environment.NewLine}{error}";
            File.AppendAllText(
                Path.Combine(logDir, "windexbar.log"),
                $"[{DateTimeOffset.Now:O}] {message}{detail}{Environment.NewLine}");
        }
        catch
        {
        }
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
        _notifyIcon.MouseDoubleClick -= OnMouseDoubleClick;
        _notifyIcon.DoubleClick -= OnDoubleClick;
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _defaultIcon.Dispose();
    }
}
