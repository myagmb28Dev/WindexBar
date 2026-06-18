using CodexBar.Core.Config;
using CodexBar.Core.Models;
using CodexBar.Core.Refresh;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using Windows.System;
using WinUiDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace CodexBar.Windows;

public sealed partial class MainWindow : Window
{
    private const double HudClientWidth = 380;
    private const double HudClientHeight = 225;
    private const double SettingsClientWidth = HudClientWidth;
    private const double SettingsClientHeight = 184;

    private readonly UsageStore _usageStore;
    private readonly SettingsStore _settingsStore;
    private readonly WinUiDispatcherQueue _dispatcher;
    private readonly DispatcherTimer _barAnimationTimer = new();
    private readonly List<ModelWindowView> _modelWindows = [];
    private double _barSweepPhase;
    private double _activeBarValue;
    private double _targetActiveBarValue;
    private int _activeModelIndex;
    private bool _isModelTransitioning;
    private int _pendingModelIndex = -1;
    private int _pendingModelDirection;

    public MainWindow(UsageStore usageStore, SettingsStore settingsStore)
    {
        InitializeComponent();
        ConfigureCompactWindow();
        RootLayout.KeyDown += OnModelNavigationKeyDown;
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
        HudView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Visible;
        ResizeForCurrentView();
        _modelWindows.Clear();
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
        _activeBarValue = EaseBarValue(_activeBarValue, _targetActiveBarValue);
        ApplyActiveBarProgress(
            ActiveWindowTrackRoot,
            ActiveWindowFillBar,
            ActiveWindowSweepBar,
            _activeBarValue,
            _targetActiveBarValue,
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
        if (HudView.Visibility != Visibility.Visible || _modelWindows.Count <= 1)
        {
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Left:
                SetActiveModel(_activeModelIndex - 1, -1);
                e.Handled = true;
                break;
            case VirtualKey.Right:
                SetActiveModel(_activeModelIndex + 1, 1);
                e.Handled = true;
                break;
        }
    }

    private void PrevModelButton_Click(object sender, RoutedEventArgs args) => SetActiveModel(_activeModelIndex - 1, -1);

    private void NextModelButton_Click(object sender, RoutedEventArgs args) => SetActiveModel(_activeModelIndex + 1, 1);

    private void SetActiveModel(int nextIndex, int preferredDirection = 0)
    {
        if (_modelWindows.Count <= 1)
        {
            return;
        }

        var normalized = NormalizeModelIndex(nextIndex);
        var current = _activeModelIndex;
        if (normalized == current)
        {
            return;
        }

        var direction = ResolveTransitionDirection(current, normalized, preferredDirection);

        if (_isModelTransitioning)
        {
            _pendingModelIndex = normalized;
            _pendingModelDirection = direction;
            return;
        }

        _activeModelIndex = normalized;
        AnimateModelTransition(direction);
    }

    private void ProcessPendingModelTransition()
    {
        if (_pendingModelIndex < 0)
        {
            return;
        }

        var target = _pendingModelIndex;
        var direction = _pendingModelDirection;
        _pendingModelIndex = -1;
        _pendingModelDirection = 0;

        if (target == _activeModelIndex)
        {
            return;
        }

        var resolvedDirection = direction != 0 ? direction : ResolveTransitionDirection(_activeModelIndex, target, 0);
        _activeModelIndex = target;
        AnimateModelTransition(resolvedDirection);
    }

    private void AnimateModelTransition(int direction)
    {
        var panel = ModelContentPanel;
        if (panel is null || panel.ActualWidth <= 0 || ModelContentTransform is null)
        {
            ApplyActiveModel();
            return;
        }

        var offset = Math.Max(24, panel.ActualWidth * 0.3);
        var outOffset = direction >= 0 ? -offset : offset;

        _isModelTransitioning = true;
        panel.IsHitTestVisible = false;

        var outSlide = new DoubleAnimation
        {
            From = 0,
            To = outOffset,
            Duration = TimeSpan.FromMilliseconds(140),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        var outFade = new DoubleAnimation
        {
            From = 1,
            To = 0.24,
            Duration = TimeSpan.FromMilliseconds(130),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var outStory = new Storyboard();
        Storyboard.SetTarget(outSlide, ModelContentTransform);
        Storyboard.SetTargetProperty(outSlide, nameof(TranslateTransform.X));
        Storyboard.SetTarget(outFade, panel);
        Storyboard.SetTargetProperty(outFade, "Opacity");
        outStory.Children.Add(outSlide);
        outStory.Children.Add(outFade);

        outStory.Completed += (_, _) =>
        {
            ApplyActiveModel();
            var inOffset = -outOffset;
            ModelContentTransform.X = inOffset;
            panel.Opacity = 0;

            var inSlide = new DoubleAnimation
            {
                From = inOffset,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var inFade = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            var inStory = new Storyboard();
            Storyboard.SetTarget(inSlide, ModelContentTransform);
            Storyboard.SetTargetProperty(inSlide, nameof(TranslateTransform.X));
            Storyboard.SetTarget(inFade, panel);
            Storyboard.SetTargetProperty(inFade, "Opacity");
            inStory.Children.Add(inSlide);
            inStory.Children.Add(inFade);

            inStory.Completed += (_, _) =>
            {
                ModelContentTransform.X = 0;
                panel.Opacity = 1;
                panel.IsHitTestVisible = true;
                _isModelTransitioning = false;
                ProcessPendingModelTransition();
            };

            inStory.Begin();
        };

        outStory.Begin();
    }

    private int ResolveTransitionDirection(int currentIndex, int targetIndex, int preferredDirection)
    {
        if (preferredDirection != 0)
        {
            return preferredDirection < 0 ? -1 : 1;
        }

        if (_modelWindows.Count <= 1)
        {
            return 0;
        }

        var forwardDistance = (targetIndex - currentIndex + _modelWindows.Count) % _modelWindows.Count;
        var backwardDistance = (currentIndex - targetIndex + _modelWindows.Count) % _modelWindows.Count;
        return forwardDistance <= backwardDistance ? 1 : -1;
    }

    private int NormalizeModelIndex(int index)
    {
        return ((index % _modelWindows.Count) + _modelWindows.Count) % _modelWindows.Count;
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
        });
        _usageStore.StartBackgroundRefresh();
        ShowHudView();
    }

    private void QuitButton_Click(object sender, RoutedEventArgs args) => App.Current.Shutdown();

    private void UpdateState()
    {
        BuildModelWindows();

        var snapshot = _usageStore.Snapshot;
        var credits = _usageStore.Credits;
        var disabled = !_settingsStore.Codex.Enabled;

        CreditsText.Text = credits is null ? "unknown" : $"{credits.Remaining:0.##} remaining";
        AccountText.Text = FormatIdentity(snapshot?.Identity);
        ErrorText.Text = string.IsNullOrWhiteSpace(_usageStore.LastError) ? string.Empty : _usageStore.LastError;
        HudMetaText.Text = FormatHudMeta(snapshot, disabled);
        HudHeaderText.Text = "Codex";

        UpdateModelPager();
        ApplyActiveModel();
    }

    private void ApplyActiveModel()
    {
        var model = _modelWindows.Count > _activeModelIndex ? _modelWindows[_activeModelIndex] : null;
        var window = model?.Window;

        ActiveWindowLabelText.Text = model?.DisplayLabel ?? "No model";
        ActiveWindowPercentText.Text = FormatHudPercent(window);
        ActiveWindowText.Text = FormatWindow(window);
        ModelSwitcherMetaText.Text = model?.DisplayLabel ?? string.Empty;
        ModelPageText.Text = _modelWindows.Count <= 1 ? string.Empty : $"{_activeModelIndex + 1} / {_modelWindows.Count}";
        _targetActiveBarValue = window?.RemainingPercent ?? 0;
    }

    private void BuildModelWindows()
    {
        _modelWindows.Clear();
        var snapshot = _usageStore.Snapshot;
        AddWindow("Primary", snapshot?.Primary);
        AddWindow("Secondary", snapshot?.Secondary);
        AddWindow("Tertiary", snapshot?.Tertiary);

        if (_activeModelIndex >= _modelWindows.Count)
        {
            _activeModelIndex = Math.Max(0, _modelWindows.Count - 1);
        }
    }

    private void AddWindow(string label, RateWindow? window)
    {
        if (window is null)
        {
            return;
        }

        _modelWindows.Add(new ModelWindowView(label, window));
    }

    private void UpdateModelPager()
    {
        var canNavigate = _modelWindows.Count > 1;
        ModelTogglePanel.Visibility = canNavigate ? Visibility.Visible : Visibility.Collapsed;
        PrevModelButton.IsEnabled = canNavigate;
        NextModelButton.IsEnabled = canNavigate;
        ModelPageText.Visibility = canNavigate ? Visibility.Visible : Visibility.Collapsed;
    }

    private string FormatHudMeta(UsageSnapshot? snapshot, bool disabled)
    {
        if (disabled)
        {
            return "Provider disabled";
        }

        if (_usageStore.IsRefreshing)
        {
            return "Refreshing";
        }

        if (snapshot is not null)
        {
            return $"Updated {snapshot.UpdatedAt.LocalDateTime:t}";
        }

        return string.IsNullOrWhiteSpace(_usageStore.LastError) ? "No usage loaded" : _usageStore.LastError;
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

    private sealed record ModelWindowView(string DisplayLabel, RateWindow Window);
}
