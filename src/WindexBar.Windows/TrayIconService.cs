using System.Globalization;
using WindexBar.Core.Config;
using WindexBar.Core.Formatting;
using WindexBar.Core.Models;
using WindexBar.Core.Refresh;
using WindexBar.Core.Windowing;
using Microsoft.UI.Dispatching;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace WindexBar.Windows;

public sealed class TrayIconService : IDisposable
{
    private const int ToggleWindowHotkeyId = 0x5742;
    private const int ToggleSidebarHotkeyId = 0x5743;

    private readonly SettingsStore _settingsStore;
    private readonly UsageStore _usageStore;
    private readonly DispatcherQueue _dispatcher;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Drawing.Icon _defaultIcon;
    private readonly GlobalHotkeyService _hotkeyService;
    private readonly ForegroundCodexActivityService _codexActivityService;
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
        _hotkeyService = new GlobalHotkeyService();
        _codexActivityService = new ForegroundCodexActivityService();
        _codexActivityService.ActivityChanged += OnCodexActivityChanged;
        RegisterHotkeys();
        _usageStore.Changed += OnUsageChanged;
        _settingsStore.Changed += OnSettingsChanged;
        ApplyAutoVisibilityMonitoring();
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

    public void ToggleStatusWindow()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var window = GetOrCreateStatusWindow();
            if (WindowCloseBehavior.IsVisible(window))
            {
                WindowCloseBehavior.Hide(window);
                LogMessage("WindexBar window hidden by hotkey.");
            }
            else
            {
                var status = WindowCloseBehavior.Show(window);
                LogMessage($"WindexBar window shown by hotkey for {status}.");
            }

            _uiError = null;
        }
        catch (Exception error)
        {
            _statusWindow = null;
            _uiError = error.Message;
            LogMessage("Failed to toggle WindexBar window by hotkey.", error);
        }
        finally
        {
            UpdateTooltip();
        }
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
        menu.Items.Add(Text("Settings", "\uC124\uC815"), null, (_, _) => _dispatcher.TryEnqueue(ShowSettingsWindow));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Text("Quit", "\uC885\uB8CC"), null, (_, _) => _dispatcher.TryEnqueue(App.Current.Shutdown));
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

    private void OnSettingsChanged(object? sender, EventArgs args)
    {
        _dispatcher.TryEnqueue(() =>
        {
            RegisterHotkeys();
            ApplyAutoVisibilityMonitoring();
            RebuildMenu();
            UpdateTooltip();
        });
    }

    private void RegisterHotkeys()
    {
        if (!_settingsStore.Config.AutoShowWithCodex)
        {
            RegisterHotkey(ToggleWindowHotkeyId, _settingsStore.Config.Hotkeys.ToggleWindow, ToggleStatusWindow, "window");
        }
        else
        {
            _hotkeyService.Unregister(ToggleWindowHotkeyId);
            LogMessage("WindexBar window hotkey disabled while Codex auto-show is enabled.");
        }

        RegisterHotkey(ToggleSidebarHotkeyId, _settingsStore.Config.Hotkeys.ToggleSidebar, ToggleSidebar, "sidebar");
    }

    private void RegisterHotkey(int id, string shortcut, Action action, string name)
    {
        if (_hotkeyService.Register(id, shortcut, () => _dispatcher.TryEnqueue(action.Invoke), out var error))
        {
            LogMessage($"Registered WindexBar {name} hotkey: {shortcut}.");
            return;
        }

        LogMessage($"Failed to register WindexBar {name} hotkey {shortcut}: {error}");
    }

    private void ToggleSidebar()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var window = _statusWindow;
            if (window is null || !WindowCloseBehavior.IsVisible(window))
            {
                LogMessage("WindexBar sidebar hotkey ignored because the window is hidden.");
                return;
            }

            window.ToggleSideBar();
            var status = WindowCloseBehavior.Show(window);
            LogMessage($"WindexBar sidebar toggled by hotkey for {status}.");
            _uiError = null;
        }
        catch (Exception error)
        {
            _statusWindow = null;
            _uiError = error.Message;
            LogMessage("Failed to toggle WindexBar sidebar by hotkey.", error);
        }
        finally
        {
            UpdateTooltip();
        }
    }

    private void OnCodexActivityChanged(object? sender, bool isActive)
    {
        _dispatcher.TryEnqueue(() => ApplyAutoVisibility(isActive));
    }

    private void ApplyAutoVisibilityMonitoring()
    {
        if (_settingsStore.Config.AutoShowWithCodex)
        {
            _codexActivityService.Start();
            ApplyAutoVisibility(_codexActivityService.IsActive);
            return;
        }

        _codexActivityService.Stop();
    }

    private void ApplyAutoVisibility(bool isCodexActivity)
    {
        if (_disposed || !_settingsStore.Config.AutoShowWithCodex)
        {
            return;
        }

        var shouldShow = AutoVisibilityPolicy.ShouldShow(
            _settingsStore.Config.AutoShowWithCodex,
            isCodexActivity,
            false);

        if (shouldShow)
        {
            TryShowWindow(window =>
            {
                window.ShowHudView();
                var status = WindowCloseBehavior.ShowPassive(window);
                LogMessage($"WindexBar window auto-shown for {status}.");
            });
            return;
        }

        if (_statusWindow is not null && WindowCloseBehavior.IsVisible(_statusWindow))
        {
            WindowCloseBehavior.Hide(_statusWindow);
            LogMessage("WindexBar window auto-hidden.");
        }
    }

    private void UpdateTooltip()
    {
        _notifyIcon.Text = TooltipText(
            _usageStore.Snapshot,
            _usageStore.Credits,
            _uiError ?? _usageStore.LastError,
            _settingsStore.Config.Language);
    }

    private static string TooltipText(UsageSnapshot? snapshot, CreditsSnapshot? credits, string? error, string language)
    {
        var isKorean = IsKorean(language);
        if (!string.IsNullOrWhiteSpace(error) && snapshot is null)
        {
            return TrimTooltip($"WindexBar - {error}");
        }

        if (snapshot?.Primary is null)
        {
            var tokenOnlyText = TooltipTokenText(snapshot?.TokenUsage, language);
            var resetOnlyText = TooltipResetCreditsText(snapshot?.RateLimitResetCredits, language);
            if (tokenOnlyText is null && resetOnlyText is not null)
            {
                return TrimTooltip(isKorean
                    ? $"WindexBar - \uCD08\uAE30\uD654\uAD8C {resetOnlyText}"
                    : $"WindexBar - resets {resetOnlyText}");
            }

            return tokenOnlyText is null
                ? TrimTooltip(isKorean ? "WindexBar - Codex \uC0AC\uC6A9\uB7C9 \uC54C \uC218 \uC5C6\uC74C" : "WindexBar - Codex usage unknown")
                : TrimTooltip(isKorean ? $"WindexBar - \uD1A0\uD070 {tokenOnlyText}" : $"WindexBar - tokens {tokenOnlyText}");
        }

        var creditsText = credits is null
            ? string.Empty
            : isKorean ? $", \uD06C\uB808\uB527 {credits.Remaining:0.##}" : $", credits {credits.Remaining:0.##}";
        var resetCreditsText = snapshot.RateLimitResetCredits is null
            ? string.Empty
            : isKorean
                ? $", \uCD08\uAE30\uD654\uAD8C {RateLimitResetCreditFormatter.FormatCompact(snapshot.RateLimitResetCredits, language)}"
                : $", resets {RateLimitResetCreditFormatter.FormatCompact(snapshot.RateLimitResetCredits, language)}";
        var tokenText = TooltipTokenText(snapshot.TokenUsage, language);
        var tokens = tokenText is null
            ? string.Empty
            : isKorean ? $", \uD1A0\uD070 {tokenText}" : $", tokens {tokenText}";
        return TrimTooltip(isKorean
            ? $"WindexBar - \uC138\uC158 {snapshot.Primary.RemainingPercent:0.#}% \uB0A8\uC74C{resetCreditsText}{creditsText}{tokens}"
            : $"WindexBar - session {snapshot.Primary.RemainingPercent:0.#}% left{resetCreditsText}{creditsText}{tokens}");
    }

    private void RebuildMenu()
    {
        var oldMenu = _notifyIcon.ContextMenuStrip;
        _notifyIcon.ContextMenuStrip = BuildMenu();
        oldMenu?.Dispose();
    }

    private string Text(string english, string korean) => IsKorean(_settingsStore.Config.Language) ? korean : english;

    private static bool IsKorean(string? language) => WindexBarConfig.NormalizeLanguage(language) == "ko";

    private static string TrimTooltip(string text) => text.Length <= 63 ? text : text[..63];

    private static string? TooltipTokenText(TokenUsageSnapshot? tokenUsage, string language)
    {
        var contextPercent = TokenContextPercent(tokenUsage);
        if (contextPercent is not null)
        {
            return IsKorean(language)
                ? $"\uCEE8\uD14D\uC2A4\uD2B8 {contextPercent.Value.ToString("0.#", CultureInfo.InvariantCulture)}%"
                : $"ctx {contextPercent.Value.ToString("0.#", CultureInfo.InvariantCulture)}%";
        }

        var sessionTokens = tokenUsage?.Total?.TotalTokens;
        return sessionTokens is null
            ? null
            : IsKorean(language)
                ? $"\uC138\uC158 {TokenCountFormatter.Format(sessionTokens.Value, language)}"
                : $"session {TokenCountFormatter.Format(sessionTokens.Value, language)}";
    }

    private static string? TooltipResetCreditsText(RateLimitResetCreditsSnapshot? resetCredits, string language) =>
        resetCredits is null ? null : RateLimitResetCreditFormatter.FormatCompact(resetCredits, language);

    private static double? TokenContextPercent(TokenUsageSnapshot? tokenUsage)
    {
        var current = tokenUsage?.Last ?? tokenUsage?.Total;
        if (current is null || tokenUsage?.ModelContextWindow is not { } contextWindow || contextWindow <= 0)
        {
            return null;
        }

        return Math.Clamp(current.TotalTokens * 100d / contextWindow, 0, 100);
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
        _settingsStore.Changed -= OnSettingsChanged;
        _codexActivityService.ActivityChanged -= OnCodexActivityChanged;
        _codexActivityService.Dispose();
        _notifyIcon.MouseClick -= OnMouseClick;
        _notifyIcon.MouseDoubleClick -= OnMouseDoubleClick;
        _notifyIcon.DoubleClick -= OnDoubleClick;
        _hotkeyService.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _defaultIcon.Dispose();
    }
}
