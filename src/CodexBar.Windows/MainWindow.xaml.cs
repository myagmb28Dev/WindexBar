using CodexBar.Core.Config;
using CodexBar.Core.Models;
using CodexBar.Core.Refresh;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using Windows.System;
using WinUiDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace CodexBar.Windows;

public sealed partial class MainWindow : Window
{
    private const double HudClientWidth = 265;
    private const double HudClientHeight = 250;
    private const double SettingsClientWidth = HudClientWidth;
    private const double SettingsClientHeight = 174;

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
    private int _activeModelIndex;
    private bool _isModelTransitioning;
    private int _pendingModelIndex = -1;
    private int _pendingModelDirection;

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
        if (HudView.Visibility != Visibility.Visible || _modelUsages.Count <= 1)
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

    private void SetActiveModel(int nextIndex, int preferredDirection = 0)
    {
        if (_modelUsages.Count <= 1)
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

        if (_modelUsages.Count <= 1)
        {
            return 0;
        }

        var forwardDistance = (targetIndex - currentIndex + _modelUsages.Count) % _modelUsages.Count;
        var backwardDistance = (currentIndex - targetIndex + _modelUsages.Count) % _modelUsages.Count;
        return forwardDistance <= backwardDistance ? 1 : -1;
    }

    private int NormalizeModelIndex(int index)
    {
        return ((index % _modelUsages.Count) + _modelUsages.Count) % _modelUsages.Count;
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
        BuildModelUsages();

        var snapshot = _usageStore.Snapshot;
        var credits = _usageStore.Credits;
        var disabled = !_settingsStore.Codex.Enabled;

        CreditsText.Text = credits is null ? "unknown" : $"{credits.Remaining:0.##} remaining";
        AccountText.Text = FormatIdentity(snapshot?.Identity);
        ErrorText.Text = string.IsNullOrWhiteSpace(_usageStore.LastError) ? string.Empty : _usageStore.LastError;
        HudMetaText.Text = FormatHudMeta(snapshot, disabled);

        UpdateModelPager();
        ApplyActiveModel();
    }

    private void ApplyActiveModel()
    {
        var model = _modelUsages.Count > _activeModelIndex ? _modelUsages[_activeModelIndex] : null;
        var modelDisplayName = model is null ? string.Empty : FormatModelDisplayName(model);

        HudHeaderText.Text = FirstNonBlank(modelDisplayName, "Codex");
        ModelPageText.Text = _modelUsages.Count <= 1 ? string.Empty : $"{_activeModelIndex + 1} / {_modelUsages.Count}";
        ApplyWindowView(CurrentWindowPercentText, CurrentWindowText, model?.Current, out _targetCurrentBarValue);
        ApplyWindowView(WeeklyWindowPercentText, WeeklyWindowText, model?.Weekly, out _targetWeeklyBarValue);
    }

    private string FormatModelDisplayName(ModelUsageView model)
    {
        var activeModel = _usageStore.Snapshot?.ActiveModel;
        if (activeModel is null || string.IsNullOrWhiteSpace(activeModel.DisplayName))
        {
            return model.DisplayName;
        }

        if (IsSameModelName(model.DisplayName, activeModel.Model))
        {
            return activeModel.DisplayName;
        }

        if (IsGenericCodexModel(model.DisplayName) && !HasExplicitActiveModelPage(activeModel.Model))
        {
            return activeModel.DisplayName;
        }

        return model.DisplayName;
    }

    private void BuildModelUsages()
    {
        _modelUsages.Clear();
        var snapshot = _usageStore.Snapshot;

        if (snapshot?.Models is { Count: > 0 } models)
        {
            foreach (var model in models.Where(model => model.HasRateLimitWindows))
            {
                AddModelUsage(model.ModelName, model.Current, model.Weekly);
            }
        }
        else
        {
            AddModelUsage(FirstNonBlank(snapshot?.ActiveModel?.DisplayName, "Codex"), snapshot?.Primary, snapshot?.Secondary);
        }

        if (_activeModelIndex >= _modelUsages.Count)
        {
            _activeModelIndex = Math.Max(0, _modelUsages.Count - 1);
        }
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
        var canNavigate = _modelUsages.Count > 1;
        ModelPageText.Visibility = canNavigate ? Visibility.Visible : Visibility.Collapsed;
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

    private bool HasExplicitActiveModelPage(string modelName) =>
        _modelUsages.Any(model => !IsGenericCodexModel(model.DisplayName) && IsSameModelName(model.DisplayName, modelName));

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

    private sealed record ModelUsageView(string DisplayName, RateWindow? Current, RateWindow? Weekly);
}
