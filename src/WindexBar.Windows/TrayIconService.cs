using System.Globalization;
using WindexBar.Core.Config;
using WindexBar.Core.Formatting;
using WindexBar.Core.Models;
using WindexBar.Core.Presentation;
using WindexBar.Core.Refresh;
using WindexBar.Core.Windowing;
using WindexBar.Core.Updates;
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
    private readonly CodexCliUpdateService _codexCliUpdateService;
    private readonly DispatcherQueue _dispatcher;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Drawing.Icon _defaultIcon;
    private readonly GlobalHotkeyService _hotkeyService;
    private readonly ForegroundCodexActivityService _codexActivityService;
    private readonly AutoVisibilityStabilityFilter _autoVisibilityFilter = new(inactiveSamplesBeforeHide: 2);
    private MainWindow? _statusWindow;
    private string? _uiError;
    private bool _started;
    private bool _autoVisibilityMonitoringStarted;
    private bool _disposed;

    public TrayIconService(
        SettingsStore settingsStore,
        UsageStore usageStore,
        CodexCliUpdateService codexCliUpdateService,
        DispatcherQueue dispatcher)
    {
        _settingsStore = settingsStore;
        _usageStore = usageStore;
        _codexCliUpdateService = codexCliUpdateService;
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
        _codexActivityService.ActivitySampled += OnCodexActivitySampled;
        RegisterHotkeys();
        _usageStore.Changed += OnUsageChanged;
        _settingsStore.Changed += OnSettingsChanged;
        UpdateTooltip();
    }

    public void Start()
    {
        if (_disposed || _started)
        {
            return;
        }

        _started = true;
        if (_settingsStore.Config.AutoShowWithCodex)
        {
            StartAutoVisibilityMonitoring();
            return;
        }

        ShowStatusWindow();
        if (_statusWindow?.HasCompletedStartup == true)
        {
            StartAutoVisibilityMonitoring();
        }
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

    public void ShowError(string title, string message)
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.ShowBalloonTip(10000, title, message, Forms.ToolTipIcon.Error);
    }

    public async Task<bool> PromptForAppUpdateAsync(AppVersion version, CancellationToken cancellationToken)
    {
        if (_disposed || cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        if (_dispatcher.HasThreadAccess)
        {
            return await PromptForAppUpdateOnUiThreadAsync(version, cancellationToken);
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    completion.TrySetResult(await PromptForAppUpdateOnUiThreadAsync(version, cancellationToken));
                }
                catch (Exception error)
                {
                    completion.TrySetException(error);
                }
            }))
        {
            return false;
        }

        return await completion.Task.WaitAsync(cancellationToken);
    }

    public void ToggleStatusWindow()
    {
        if (_disposed)
        {
            return;
        }

        if (_settingsStore.Config.AutoShowWithCodex)
        {
            ShowModeLockedNotice(
                Text("Window toggle locked", "창 토글 잠김"),
                Text(
                    "Automatic Codex visibility is enabled. WindexBar is shown only while ChatGPT Desktop or Codex is active.",
                    "Codex 자동 표시가 켜져 있어요. ChatGPT Desktop 또는 Codex 사용 중에만 WindexBar가 표시돼요."));
            LogMessage("WindexBar window hotkey ignored because Codex auto-show is enabled.");
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
            _statusWindow = new MainWindow(_usageStore, _settingsStore, _codexCliUpdateService);
            _statusWindow.StartupCompleted += OnStatusWindowStartupCompleted;
            _statusWindow.Closed += (_, _) =>
            {
                LogMessage("WindexBar window closed.");
                _statusWindow = null;
            };
        }

        return _statusWindow;
    }

    private Task<bool> PromptForAppUpdateOnUiThreadAsync(AppVersion version, CancellationToken cancellationToken)
    {
        var window = GetOrCreateStatusWindow();
        window.ShowHudView();
        WindowCloseBehavior.Show(window);
        return window.PromptForAppUpdateAsync(version, cancellationToken);
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

    private void OnUsageChanged(object? sender, EventArgs args) =>
        _dispatcher.TryEnqueue(() =>
        {
            UpdateTooltip();
            ShowRateLimitAlerts();
        });

    private void OnSettingsChanged(object? sender, EventArgs args)
    {
        _dispatcher.TryEnqueue(() =>
        {
            RegisterHotkeys();
            if (_autoVisibilityMonitoringStarted)
            {
                ApplyAutoVisibilityMonitoring();
            }

            RebuildMenu();
            UpdateTooltip();
        });
    }

    private void RegisterHotkeys()
    {
        RegisterHotkey(ToggleWindowHotkeyId, _settingsStore.Config.Hotkeys.ToggleWindow, ToggleStatusWindow, "window");
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

        if (_settingsStore.Config.Sidebar.ShowOnHover)
        {
            ShowModeLockedNotice(
                Text("Sidebar toggle locked", "사이드바 토글 잠김"),
                Text(
                    "Sidebar hover reveal is enabled. Move the pointer to the sidebar edge to use it.",
                    "사이드바 호버 표시가 켜져 있어요. 사이드바 가장자리에 마우스를 올려 사용해 주세요."));
            LogMessage("WindexBar sidebar hotkey ignored because sidebar hover reveal is enabled.");
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

    private void ShowModeLockedNotice(string title, string message)
    {
        if (_statusWindow is { } window && WindowCloseBehavior.IsVisible(window))
        {
            window.ShowModeLockedNotice(title, message);
            return;
        }

        _notifyIcon.ShowBalloonTip(6_000, title, message, Forms.ToolTipIcon.Info);
    }

    private void OnCodexActivitySampled(object? sender, bool isActive)
    {
        _dispatcher.TryEnqueue(() => ApplyAutoVisibility(isActive));
    }

    private void OnStatusWindowStartupCompleted(object? sender, EventArgs args)
    {
        if (sender is MainWindow window)
        {
            window.StartupCompleted -= OnStatusWindowStartupCompleted;
        }

        if (_started)
        {
            try
            {
                StartAutoVisibilityMonitoring();
            }
            catch (Exception error)
            {
                _uiError = error.Message;
                LogMessage("Failed to start Codex auto-visibility monitoring.", error);
                UpdateTooltip();
            }
        }
    }

    private void StartAutoVisibilityMonitoring()
    {
        if (_disposed || _autoVisibilityMonitoringStarted)
        {
            return;
        }

        _autoVisibilityMonitoringStarted = true;
        ApplyAutoVisibilityMonitoring();
        LogMessage("Codex auto-visibility monitoring started.");
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
        _autoVisibilityFilter.Reset();
    }

    private void ApplyAutoVisibility(bool isCodexActivity)
    {
        if (_disposed || !_settingsStore.Config.AutoShowWithCodex)
        {
            return;
        }

        if (_statusWindow?.HasOpenAppUpdatePrompt == true)
        {
            return;
        }

        var stableCodexActivity = _autoVisibilityFilter.ShouldTreatAsActive(isCodexActivity);
        var shouldShow = AutoVisibilityPolicy.ShouldShow(
            _settingsStore.Config.AutoShowWithCodex,
            stableCodexActivity,
            false);

        if (shouldShow)
        {
            if (_statusWindow is not null && WindowCloseBehavior.IsVisible(_statusWindow))
            {
                return;
            }

            TryShowWindow(window =>
            {
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

    private void ShowRateLimitAlerts()
    {
        var alerts = _usageStore.TakeRateLimitAlerts();
        if (alerts.Count == 0)
        {
            return;
        }

        var korean = IsKorean(_settingsStore.Config.Language);
        var message = string.Join(
            Environment.NewLine,
            alerts.Select(alert =>
            {
                var window = alert.Window == RateLimitAlertWindow.Weekly
                    ? Text("Weekly limit", "\uC8FC\uAC04 \uD55C\uB3C4")
                    : Text("Current limit", "\uD604\uC7AC \uD55C\uB3C4");
                return korean
                    ? $"{window}: {alert.UsedPercent:0.#}% \uC0AC\uC6A9 ({alert.ThresholdPercent}% \uB3C4\uB2EC)"
                    : $"{window}: {alert.UsedPercent:0.#}% used ({alert.ThresholdPercent}% threshold)";
            }));
        _notifyIcon.ShowBalloonTip(
            10_000,
            Text("Codex limit alert", "Codex \uD55C\uB3C4 \uC54C\uB9BC"),
            message,
            Forms.ToolTipIcon.Warning);
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
        var contextPercent = SessionListViewModelFactory.TokenContextPercent(tokenUsage);
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

    private static Drawing.Icon LoadIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
        {
            return new Drawing.Icon(iconPath);
        }

        return Drawing.SystemIcons.Application;
    }

    private static void LogMessage(string message, Exception? error = null) => AppLog.Write(message, error);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _usageStore.Changed -= OnUsageChanged;
        _settingsStore.Changed -= OnSettingsChanged;
        _codexActivityService.ActivitySampled -= OnCodexActivitySampled;
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
