using CodexBar.Core.Config;
using CodexBar.Core.Models;
using CodexBar.Core.Refresh;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using Windows.System;
using WinUiDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace CodexBar.Windows;

public sealed partial class MainWindow : Window
{
    private const double HudClientWidth = 265;
    private const double HudClientHeight = 240;
    private const double SettingsClientWidth = HudClientWidth;
    private const double SettingsClientHeight = 204;
    private const double KeyboardScrollStep = 36;

    private readonly UsageStore _usageStore;
    private readonly SettingsStore _settingsStore;
    private readonly WinUiDispatcherQueue _dispatcher;
    private readonly DispatcherTimer _barAnimationTimer = new();
    private readonly List<ModelUsageView> _modelUsages = [];
    private double _barSweepPhase;
    private double _currentBarValue;
    private double _targetCurrentBarValue;
    private double _weeklyBarValue;
    private double _targetWeeklyBarValue;

    public MainWindow(UsageStore usageStore, SettingsStore settingsStore)
    {
        InitializeComponent();
        ConfigureCompactWindow();
        RootLayout.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnModelNavigationKeyDown), true);
        RootLayout.PointerPressed += (_, _) =>
        {
            RootLayout.Focus(FocusState.Pointer);
        };

        RootLayout.Loaded += (_, _) =>
        {
            RootLayout.Focus(FocusState.Programmatic);
            ResizeForCurrentView();
        };

        _usageStore = usageStore;
        _settingsStore = settingsStore;
        _dispatcher = WinUiDispatcherQueue.GetForCurrentThread();
        _usageStore.Changed += OnUsageChanged;

        _barAnimationTimer.Interval = TimeSpan.FromMilliseconds(33);
        _barAnimationTimer.Tick += (_, _) => AnimateProgressBars();
        _barAnimationTimer.Start();

        Closed += (_, _) =>
        {
            _usageStore.Changed -= OnUsageChanged;
            _barAnimationTimer.Stop();
        };

        UpdateState();
    }

    public void ShowHudView()
    {
        Title = "Win CodexBar";
        SettingsView.Visibility = Visibility.Collapsed;
        HudView.Visibility = Visibility.Visible;
        ResizeForCurrentView();
        RootLayout.Focus(FocusState.Programmatic);
        UpdateState();
    }

    public void ShowSettingsView()
    {
        Title = "Win CodexBar Settings";
        CodexEnabledCheckBox.IsChecked = _settingsStore.Codex.Enabled;
        RefreshIntervalSecondsNumberBox.Value = _settingsStore.Codex.RefreshIntervalSeconds;
        HudView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Visible;
        ResizeForCurrentView();
        _modelUsages.Clear();
    }

    private void ConfigureCompactWindow()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = true;
            presenter.IsResizable = true;
        }
    }

    private void ResizeForCurrentView()
    {
        if (SettingsView.Visibility == Visibility.Visible)
        {
            ResizeClientToEffectiveSize(SettingsClientWidth, SettingsClientHeight);
            return;
        }

        ResizeClientToEffectiveSize(HudClientWidth, HudClientHeight);
    }

    private void ResizeClientToEffectiveSize(double width, double height)
    {
        var scale = RootLayout.XamlRoot?.RasterizationScale ?? 1;
        AppWindow.ResizeClient(new SizeInt32(
            (int)Math.Ceiling(width * scale),
            (int)Math.Ceiling(height * scale)));
    }

    private void AnimateProgressBars()
    {
        _barSweepPhase = (_barSweepPhase + 0.035) % 1.0;
        var activeWindowSweep = EaseSweep(_barSweepPhase);
        _currentBarValue = EaseBarValue(_currentBarValue, _targetCurrentBarValue);
        _weeklyBarValue = EaseBarValue(_weeklyBarValue, _targetWeeklyBarValue);
        ApplyActiveBarProgress(
            CurrentWindowTrackRoot,
            CurrentWindowFillBar,
            CurrentWindowSweepBar,
            _currentBarValue,
            _targetCurrentBarValue,
            activeWindowSweep);
        ApplyActiveBarProgress(
            WeeklyWindowTrackRoot,
            WeeklyWindowFillBar,
            WeeklyWindowSweepBar,
            _weeklyBarValue,
            _targetWeeklyBarValue,
            activeWindowSweep);
    }

    private static double EaseBarValue(double current, double target)
    {
        var delta = target - current;
        return Math.Abs(delta) < 0.1 ? target : current + (delta * 0.16);
    }

    private static double EaseSweep(double amount) => 1 - Math.Pow(1 - amount, 2);

    private static void ApplyActiveBarProgress(Grid trackRoot, Border fillBar, Border sweepBar, double value, double targetValue, double sweep)
    {
        var width = Math.Max(0, trackRoot.ActualWidth);
        var safeValue = Math.Clamp(value, 0, 100);
        var safeTarget = Math.Clamp(targetValue, 0, 100);
        var safeSweep = Math.Clamp(sweep, 0, 1);

        fillBar.Width = width * (safeValue / 100d);
        sweepBar.Width = width * (safeTarget * safeSweep / 100d);
        sweepBar.Opacity = 0.22 + (0.28 * safeSweep);
    }

    private void OnUsageChanged(object? sender, EventArgs args)
    {
        _dispatcher.TryEnqueue(UpdateState);
    }

    private void OnModelNavigationKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (HudView.Visibility != Visibility.Visible)
        {
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Down:
                ScrollHudBy(KeyboardScrollStep);
                e.Handled = true;
                break;
            case VirtualKey.Up:
                ScrollHudBy(-KeyboardScrollStep);
                e.Handled = true;
                break;
            case VirtualKey.PageDown:
                ScrollHudBy(HudScrollViewer.ViewportHeight);
                e.Handled = true;
                break;
            case VirtualKey.PageUp:
                ScrollHudBy(-HudScrollViewer.ViewportHeight);
                e.Handled = true;
                break;
            case VirtualKey.Home:
                ScrollHudTo(0);
                e.Handled = true;
                break;
            case VirtualKey.End:
                ScrollHudTo(HudScrollViewer.ScrollableHeight);
                e.Handled = true;
                break;
        }
    }

    private void ScrollHudBy(double delta) => ScrollHudTo(HudScrollViewer.VerticalOffset + delta);

    private void ScrollHudTo(double verticalOffset)
    {
        var targetOffset = Math.Clamp(verticalOffset, 0, Math.Max(0, HudScrollViewer.ScrollableHeight));
        HudScrollViewer.ChangeView(null, targetOffset, null, disableAnimation: false);
    }

    private void CloseCircleButton_Click(object sender, RoutedEventArgs args) => WindowCloseBehavior.Hide(this);

    private void MinimizeCircleButton_Click(object sender, RoutedEventArgs args)
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Minimize();
            return;
        }

        WindowCloseBehavior.Hide(this);
    }

    private void ZoomCircleButton_Click(object sender, RoutedEventArgs args)
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        if (presenter.State == OverlappedPresenterState.Maximized)
        {
            presenter.Restore();
            return;
        }

        presenter.Maximize();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs args) => ShowSettingsView();

    private void CancelSettingsButton_Click(object sender, RoutedEventArgs args) => ShowHudView();

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs args)
    {
        _settingsStore.UpdateCodex(c =>
        {
            c.Enabled = CodexEnabledCheckBox.IsChecked == true;
            c.RefreshIntervalSeconds = ReadRefreshIntervalSeconds();
        });
        _usageStore.StartBackgroundRefresh();
        ShowHudView();
    }

    private int ReadRefreshIntervalSeconds()
    {
        var value = RefreshIntervalSecondsNumberBox.Value;
        if (double.IsNaN(value))
        {
            return CodexBarConfig.DefaultRefreshIntervalSeconds;
        }

        return (int)Math.Clamp(
            Math.Round(value),
            CodexBarConfig.MinRefreshIntervalSeconds,
            CodexBarConfig.MaxRefreshIntervalSeconds);
    }

    private void QuitButton_Click(object sender, RoutedEventArgs args) => App.Current.Shutdown();

    private void UpdateState()
    {
        BuildModelUsages();

        var snapshot = _usageStore.Snapshot;
        var credits = _usageStore.Credits;
        var disabled = !_settingsStore.Codex.Enabled;

        CreditsText.Text = credits is null ? "unknown" : $"{credits.Remaining:0.##} remaining";
        AccountText.Text = FormatIdentity(snapshot?.Identity);
        ErrorText.Text = string.IsNullOrWhiteSpace(_usageStore.LastError) ? string.Empty : _usageStore.LastError;
        var hudMeta = FormatHudMeta(snapshot, disabled);
        HudMetaText.Text = hudMeta;
        HudMetaText.Visibility = string.IsNullOrWhiteSpace(hudMeta) ? Visibility.Collapsed : Visibility.Visible;

        UpdateModelPager();
        ApplyActiveModel();
    }

    private void ApplyActiveModel()
    {
        var model = _modelUsages.FirstOrDefault();
        var modelDisplayName = model?.DisplayName ?? string.Empty;

        HudHeaderText.Text = FirstNonBlank(modelDisplayName, "Codex");
        ModelPageText.Text = string.Empty;
        ApplyWindowView(CurrentWindowPercentText, CurrentWindowText, model?.Current, out _targetCurrentBarValue);
        ApplyWindowView(WeeklyWindowPercentText, WeeklyWindowText, model?.Weekly, out _targetWeeklyBarValue);
    }

    private void BuildModelUsages()
    {
        _modelUsages.Clear();
        var snapshot = _usageStore.Snapshot;
        if (snapshot is null)
        {
            return;
        }

        var activeModel = snapshot.ActiveModel;
        var displayName = FirstNonBlank(activeModel?.DisplayName, activeModel?.Model, "Codex");
        var currentSessionModel = FindCurrentSessionModel(snapshot.Models, activeModel);
        if (currentSessionModel is not null)
        {
            AddModelUsage(displayName, currentSessionModel.Current, currentSessionModel.Weekly);
            return;
        }

        AddModelUsage(displayName, snapshot.Primary, snapshot.Secondary);
    }

    private void AddModelUsage(string modelName, RateWindow? current, RateWindow? weekly)
    {
        if (current is null && weekly is null)
        {
            return;
        }

        _modelUsages.Add(new ModelUsageView(modelName, current, weekly));
    }

    private void UpdateModelPager()
    {
        ModelPageText.Visibility = Visibility.Collapsed;
    }

    private static void ApplyWindowView(TextBlock percentText, TextBlock detailText, RateWindow? window, out double targetValue)
    {
        percentText.Text = FormatHudPercent(window);
        detailText.Text = FormatWindow(window);
        targetValue = window?.RemainingPercent ?? 0;
    }

    private static string FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static bool IsSameModelName(string lhs, string rhs) =>
        string.Equals(NormalizeModelKey(lhs), NormalizeModelKey(rhs), StringComparison.OrdinalIgnoreCase);

    private static ModelUsageSnapshot? FindCurrentSessionModel(
        IReadOnlyList<ModelUsageSnapshot>? models,
        CodexModelSelection? activeModel)
    {
        if (models is null || models.Count == 0)
        {
            return null;
        }

        var modelsWithLimits = models.Where(model => model.HasRateLimitWindows).ToArray();
        if (modelsWithLimits.Length == 0)
        {
            return null;
        }

        if (activeModel is not null)
        {
            var matchingModel = modelsWithLimits.FirstOrDefault(model =>
                IsSameModelName(model.ModelName, activeModel.Model)
                || IsSameModelName(model.ModelName, activeModel.DisplayName));
            if (matchingModel is not null)
            {
                return matchingModel;
            }

            var genericModel = modelsWithLimits.FirstOrDefault(model => IsGenericCodexModel(model.ModelName));
            if (genericModel is not null)
            {
                return genericModel;
            }
        }

        return modelsWithLimits.FirstOrDefault(model => IsGenericCodexModel(model.ModelName))
            ?? modelsWithLimits.FirstOrDefault();
    }

    private static bool IsGenericCodexModel(string modelName) =>
        string.Equals(modelName.Trim(), "Codex", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeModelKey(string value)
    {
        var chars = StripReasoningSuffix(value)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray();
        return new string(chars);
    }

    private static string StripReasoningSuffix(string value)
    {
        var trimmed = value.Replace('_', ' ').Replace('-', ' ').Trim();
        var suffixes = new[]
        {
            " extra high reasoning effort",
            " extra high reasoning",
            " xhigh reasoning effort",
            " xhigh reasoning",
            " high reasoning effort",
            " high reasoning",
            " medium reasoning effort",
            " medium reasoning",
            " low reasoning effort",
            " low reasoning",
            " minimal reasoning effort",
            " minimal reasoning",
            " no reasoning",
            " none reasoning",
            " extra high",
            " xhigh",
            " high",
            " medium",
            " low",
            " minimal",
            " none",
            " reasoning effort",
            " reasoning"
        };

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var suffix in suffixes)
            {
                if (!trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                trimmed = trimmed[..^suffix.Length].Trim();
                changed = true;
                break;
            }
        }

        return trimmed;
    }

    private string FormatHudMeta(UsageSnapshot? snapshot, bool disabled)
    {
        if (disabled)
        {
            return "Provider disabled";
        }

        return string.IsNullOrWhiteSpace(_usageStore.LastError) ? string.Empty : _usageStore.LastError;
    }

    private static string FormatHudPercent(RateWindow? window) =>
        window is null ? "unknown" : $"{window.RemainingPercent:0.#}%";

    private static string FormatWindow(RateWindow? window)
    {
        if (window is null)
        {
            return "unknown";
        }

        var reset = window.ResetsAt is null ? string.Empty : $", resets {window.ResetDescription}";
        return $"{window.RemainingPercent:0.#}% left ({window.UsedPercent:0.#}% used{reset})";
    }

    private static string FormatIdentity(ProviderIdentitySnapshot? identity)
    {
        if (identity is null || (string.IsNullOrWhiteSpace(identity.AccountEmail) && string.IsNullOrWhiteSpace(identity.LoginMethod)))
        {
            return "unknown";
        }

        if (string.IsNullOrWhiteSpace(identity.AccountEmail))
        {
            return identity.LoginMethod ?? "unknown";
        }

        if (string.IsNullOrWhiteSpace(identity.LoginMethod))
        {
            return identity.AccountEmail;
        }

        return $"{identity.AccountEmail} ({identity.LoginMethod})";
    }

    private sealed record ModelUsageView(string DisplayName, RateWindow? Current, RateWindow? Weekly);
}
