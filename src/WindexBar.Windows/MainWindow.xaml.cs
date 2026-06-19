using System.Globalization;
using WindexBar.Core.Config;
using WindexBar.Core.Models;
using WindexBar.Core.Refresh;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using WinUiDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace WindexBar.Windows;

public sealed partial class MainWindow : Window
{
    private const double HudClientWidth = 265;
    private const double HudClientHeight = 300;
    private const double SettingsClientWidth = HudClientWidth;
    private const double SettingsClientHeight = 152;
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
    private double _tokenBarValue;
    private double _targetTokenBarValue;
    private Grid TitleBarDragRegion = null!;
    private Border HudView = null!;
    private Border SettingsView = null!;
    private ScrollViewer HudScrollViewer = null!;
    private Grid ModelContentPanel = null!;
    private TranslateTransform ModelContentTransform = null!;
    private TextBlock HudHeaderText = null!;
    private TextBlock ModelPageText = null!;
    private TextBlock HudMetaText = null!;
    private TextBlock CurrentWindowPercentText = null!;
    private Grid CurrentWindowTrackRoot = null!;
    private Border CurrentWindowFillBar = null!;
    private Border CurrentWindowSweepBar = null!;
    private TextBlock CurrentWindowText = null!;
    private TextBlock WeeklyWindowPercentText = null!;
    private Grid WeeklyWindowTrackRoot = null!;
    private Border WeeklyWindowFillBar = null!;
    private Border WeeklyWindowSweepBar = null!;
    private TextBlock WeeklyWindowText = null!;
    private TextBlock TokenWindowPercentText = null!;
    private Grid TokenWindowTrackRoot = null!;
    private Border TokenWindowFillBar = null!;
    private Border TokenWindowSweepBar = null!;
    private TextBlock TokenWindowText = null!;
    private TextBlock CreditsText = null!;
    private TextBlock AccountText = null!;
    private TextBlock ErrorText = null!;
    private TextBox RefreshIntervalSecondsTextBox = null!;

    public MainWindow(UsageStore usageStore, SettingsStore settingsStore)
    {
        InitializeComponent();
        BuildLayout();
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

    private void BuildLayout()
    {
        RootLayout.Background = Brush(0xFF, 0x25, 0x25, 0x27);
        RootLayout.RowDefinitions.Clear();
        RootLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        RootLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var customTitleBar = new Grid { Background = Brush(0, 0, 0, 0) };
        customTitleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        customTitleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
        Grid.SetRow(customTitleBar, 0);
        RootLayout.Children.Add(customTitleBar);

        TitleBarDragRegion = new Grid { Background = Brush(0, 0, 0, 0) };
        TitleBarDragRegion.Children.Add(new TextBlock
        {
            Text = "WindexBar",
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush(0xFF, 0xB9, 0xA7, 0xE8)
        });
        customTitleBar.Children.Add(TitleBarDragRegion);

        var windowButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            Spacing = 7
        };
        Grid.SetColumn(windowButtons, 1);
        customTitleBar.Children.Add(windowButtons);

        windowButtons.Children.Add(CreateTitleButton(Brush(0xFF, 0xFF, 0xBD, 0x2E), MinimizeCircleButton_Click));
        windowButtons.Children.Add(CreateTitleButton(Brush(0xFF, 0x28, 0xC8, 0x40), ZoomCircleButton_Click));
        windowButtons.Children.Add(CreateTitleButton(Brush(0xFF, 0xFF, 0x5F, 0x57), CloseCircleButton_Click));

        var contentRoot = new Grid { Padding = new Thickness(10, 0, 10, 10) };
        Grid.SetRow(contentRoot, 1);
        RootLayout.Children.Add(contentRoot);

        HudView = new Border
        {
            Padding = new Thickness(11, 9, 11, 9),
            Background = Brush(0xFF, 0x1F, 0x1C, 0x24),
            BorderBrush = Brush(0x99, 0x7D, 0x62, 0xC7),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14)
        };
        contentRoot.Children.Add(HudView);

        var hudGrid = new Grid { RowSpacing = 7 };
        hudGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        hudGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        HudView.Child = hudGrid;

        HudScrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Auto
        };
        Grid.SetRow(HudScrollViewer, 0);
        hudGrid.Children.Add(HudScrollViewer);

        var hudContent = new Grid { RowSpacing = 8 };
        for (var i = 0; i < 6; i++)
        {
            hudContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        HudScrollViewer.Content = hudContent;

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hudContent.Children.Add(headerGrid);

        HudHeaderText = new TextBlock
        {
            Text = "Codex",
            FontSize = 17,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            MaxLines = 2,
            TextWrapping = TextWrapping.WrapWholeWords
        };
        headerGrid.Children.Add(HudHeaderText);

        ModelPageText = new TextBlock
        {
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush(0xFF, 0xB9, 0xA7, 0xE8),
            FontSize = 11,
            TextAlignment = TextAlignment.Right,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(ModelPageText, 1);
        headerGrid.Children.Add(ModelPageText);

        HudMetaText = new TextBlock
        {
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
        Grid.SetRow(HudMetaText, 1);
        hudContent.Children.Add(HudMetaText);

        ModelContentPanel = new Grid { RowSpacing = 4 };
        ModelContentTransform = new TranslateTransform();
        ModelContentPanel.RenderTransform = ModelContentTransform;
        for (var i = 0; i < 9; i++)
        {
            ModelContentPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        Grid.SetRow(ModelContentPanel, 2);
        hudContent.Children.Add(ModelContentPanel);

        AddWindowSection(ModelContentPanel, 0, "Current", out CurrentWindowPercentText, out CurrentWindowTrackRoot, out CurrentWindowFillBar, out CurrentWindowSweepBar, out CurrentWindowText);
        AddWindowSection(ModelContentPanel, 3, "Weekly", out WeeklyWindowPercentText, out WeeklyWindowTrackRoot, out WeeklyWindowFillBar, out WeeklyWindowSweepBar, out WeeklyWindowText);
        AddWindowSection(ModelContentPanel, 6, "Tokens", out TokenWindowPercentText, out TokenWindowTrackRoot, out TokenWindowFillBar, out TokenWindowSweepBar, out TokenWindowText);

        CreditsText = AddLabelValueRow(hudContent, 3, "Credits");
        AccountText = AddLabelValueRow(hudContent, 4, "Account");

        ErrorText = new TextBlock
        {
            Foreground = Brush(0xFF, 0xFF, 0x5F, 0x57),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(ErrorText, 5);
        hudContent.Children.Add(ErrorText);

        var hudButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Spacing = 6
        };
        Grid.SetRow(hudButtons, 1);
        hudGrid.Children.Add(hudButtons);
        var settingsButton = new Button { Content = "Settings" };
        settingsButton.Click += SettingsButton_Click;
        var quitButton = new Button { Content = "Quit" };
        quitButton.Click += QuitButton_Click;
        hudButtons.Children.Add(settingsButton);
        hudButtons.Children.Add(quitButton);

        SettingsView = new Border
        {
            Padding = new Thickness(11, 9, 11, 9),
            Background = Brush(0xFF, 0x1F, 0x1C, 0x24),
            BorderBrush = Brush(0x99, 0x7D, 0x62, 0xC7),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Visibility = Visibility.Collapsed
        };
        contentRoot.Children.Add(SettingsView);
        BuildSettingsView();
    }

    private static Button CreateTitleButton(Brush background, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Width = 12,
            Height = 12,
            MinWidth = 12,
            MinHeight = 12,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = background
        };
        button.Click += handler;
        return button;
    }

    private static void AddWindowSection(
        Grid root,
        int row,
        string label,
        out TextBlock percentText,
        out Grid trackRoot,
        out Border fillBar,
        out Border sweepBar,
        out TextBlock detailText)
    {
        var header = new Grid { Margin = row == 0 ? new Thickness(0, 2, 0, 0) : new Thickness(0, 3, 0, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(header, row);
        root.Children.Add(header);

        header.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Brush(0xFF, 0xED, 0xE7, 0xFF)
        });

        percentText = new TextBlock { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        Grid.SetColumn(percentText, 1);
        header.Children.Add(percentText);

        trackRoot = new Grid { Height = 6 };
        Grid.SetRow(trackRoot, row + 1);
        root.Children.Add(trackRoot);
        trackRoot.Children.Add(new Border
        {
            Background = Brush(0xFF, 0x30, 0x28, 0x3A),
            BorderBrush = Brush(0xFF, 0x5A, 0x4A, 0x74),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3)
        });
        fillBar = new Border
        {
            Background = Brush(0xFF, 0x8D, 0x78, 0xD6),
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(3)
        };
        trackRoot.Children.Add(fillBar);
        sweepBar = new Border
        {
            Background = Brush(0xFF, 0xC8, 0xB9, 0xFF),
            Opacity = 0.3,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        trackRoot.Children.Add(sweepBar);

        detailText = new TextBlock
        {
            Foreground = Brush(0xFF, 0xC9, 0xC4, 0xD2),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(detailText, row + 2);
        root.Children.Add(detailText);
    }

    private static TextBlock AddLabelValueRow(Grid root, int row, string label)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        if (row == 3)
        {
            grid.Margin = new Thickness(0, 4, 0, 0);
        }

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(grid, row);
        root.Children.Add(grid);

        grid.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        var value = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        return value;
    }

    private void BuildSettingsView()
    {
        var grid = new Grid { RowSpacing = 12 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        SettingsView.Child = grid;

        var title = new TextBlock
        {
            Text = "Settings",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        grid.Children.Add(title);

        var intervalGrid = new Grid { ColumnSpacing = 8 };
        intervalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        intervalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        intervalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(intervalGrid, 1);
        grid.Children.Add(intervalGrid);

        intervalGrid.Children.Add(new TextBlock
        {
            Text = "Refresh interval",
            VerticalAlignment = VerticalAlignment.Center
        });
        RefreshIntervalSecondsTextBox = new TextBox { TextAlignment = TextAlignment.Right };
        Grid.SetColumn(RefreshIntervalSecondsTextBox, 1);
        intervalGrid.Children.Add(RefreshIntervalSecondsTextBox);
        var secondsLabel = new TextBlock
        {
            Text = "s",
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(secondsLabel, 2);
        intervalGrid.Children.Add(secondsLabel);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 6
        };
        Grid.SetRow(buttons, 2);
        grid.Children.Add(buttons);
        var cancelButton = new Button { Content = "Cancel" };
        cancelButton.Click += CancelSettingsButton_Click;
        var saveButton = new Button { Content = "Save" };
        saveButton.Click += SaveSettingsButton_Click;
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(saveButton);
    }

    private static SolidColorBrush Brush(byte a, byte r, byte g, byte b) =>
        new(global::Windows.UI.Color.FromArgb(a, r, g, b));

    public void ShowHudView()
    {
        Title = "WindexBar";
        SettingsView.Visibility = Visibility.Collapsed;
        HudView.Visibility = Visibility.Visible;
        ResizeForCurrentView();
        RootLayout.Focus(FocusState.Programmatic);
        UpdateState();
    }

    public void ShowSettingsView()
    {
        Title = "WindexBar Settings";
        RefreshIntervalSecondsTextBox.Text = _settingsStore.Codex.RefreshIntervalSeconds.ToString();
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

        AppWindow.Closing += (_, args) =>
        {
            args.Cancel = true;
            WindowCloseBehavior.Hide(this);
        };
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
        AppWindow.Move(new PointInt32(96, 96));
    }

    private void AnimateProgressBars()
    {
        _barSweepPhase = (_barSweepPhase + 0.035) % 1.0;
        var activeWindowSweep = EaseSweep(_barSweepPhase);
        _currentBarValue = EaseBarValue(_currentBarValue, _targetCurrentBarValue);
        _weeklyBarValue = EaseBarValue(_weeklyBarValue, _targetWeeklyBarValue);
        _tokenBarValue = EaseBarValue(_tokenBarValue, _targetTokenBarValue);
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
        ApplyActiveBarProgress(
            TokenWindowTrackRoot,
            TokenWindowFillBar,
            TokenWindowSweepBar,
            _tokenBarValue,
            _targetTokenBarValue,
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
            c.RefreshIntervalSeconds = ReadRefreshIntervalSeconds();
        });
        _usageStore.StartBackgroundRefresh();
        ShowHudView();
    }

    private int ReadRefreshIntervalSeconds()
    {
        if (!int.TryParse(RefreshIntervalSecondsTextBox.Text, out var value))
        {
            return WindexBarConfig.DefaultRefreshIntervalSeconds;
        }

        return Math.Clamp(
            value,
            WindexBarConfig.MinRefreshIntervalSeconds,
            WindexBarConfig.MaxRefreshIntervalSeconds);
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
        ApplyTokenUsageView(snapshot?.TokenUsage);
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

    private void ApplyTokenUsageView(TokenUsageSnapshot? tokenUsage)
    {
        TokenWindowPercentText.Text = FormatTokenPercent(tokenUsage);
        TokenWindowText.Text = FormatTokenUsage(tokenUsage);
        _targetTokenBarValue = TokenContextPercent(tokenUsage) ?? 0;
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
        return $"Used {window.UsedPercent:0.#}%{reset}";
    }

    private static string FormatTokenPercent(TokenUsageSnapshot? tokenUsage)
    {
        var percent = TokenContextPercent(tokenUsage);
        return percent is null ? "unknown" : $"{percent.Value:0.#}%";
    }

    private static string FormatTokenUsage(TokenUsageSnapshot? tokenUsage)
    {
        if (tokenUsage is null)
        {
            return "unknown";
        }

        var values = new List<string>();
        var current = tokenUsage.Last ?? tokenUsage.Total;
        if (current is not null && tokenUsage.ModelContextWindow is { } contextWindow)
        {
            values.Add($"Context: {FormatTokenCount(current.TotalTokens)} / {FormatTokenCount(contextWindow)}");
        }
        else if (current is not null)
        {
            values.Add($"Context: {FormatTokenCount(current.TotalTokens)}");
        }

        if (tokenUsage.Total is not null)
        {
            values.Add($"Session total: {FormatTokenCount(tokenUsage.Total.TotalTokens)}");
        }

        return values.Count == 0 ? "unknown" : string.Join(Environment.NewLine, values);
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
