using WindexBar.Core.Config;
using WindexBar.Core.Formatting;
using WindexBar.Core.Models;
using WindexBar.Core.Refresh;
using WindexBar.Core.Windowing;
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
    private const double ContentClientHeight = 334;
    private const double SettingsClientWidth = HudClientWidth;
    private const double KeyboardScrollStep = 36;
    private const double SideBarCollapsedWidth = 6;
    private const double SideBarExpandedWidth = 34;
    private const double SideBarExpandedGap = 7;
    private const double SideBarOuterWidth = SideBarExpandedWidth + SideBarExpandedGap;
    private const double SideBarVisualWidth = SideBarOuterWidth + 10;
    private const double StandardBarSweepStep = 0.035;
    private const double FastBarSweepStep = 0.075;
    private const double StandardBarEaseFactor = 0.16;
    private const double FastBarEaseFactor = 0.28;
    private const string FastIndicatorGlyph = "\u26A1";

    private readonly UsageStore _usageStore;
    private readonly SettingsStore _settingsStore;
    private readonly WinUiDispatcherQueue _dispatcher;
    private readonly WindowPlacementController _windowPlacement = new(new WindowPosition(96, 96));
    private readonly DispatcherTimer _barAnimationTimer = new();
    private readonly List<DispatcherTimer> _scrollBarHideTimers = [];
    private readonly List<ModelUsageView> _modelUsages = [];
    private double _barSweepPhase;
    private double _currentBarValue;
    private double _targetCurrentBarValue;
    private double _weeklyBarValue;
    private double _targetWeeklyBarValue;
    private double _tokenBarValue;
    private double _targetTokenBarValue;
    private bool _isFastServiceTier;
    private bool _isSideBarOpen = true;
    private Grid TitleBarDragRegion = null!;
    private Grid ContentRootGrid = null!;
    private Grid SideBarHost = null!;
    private ColumnDefinition SideBarColumn = null!;
    private StackPanel SideBarPanel = null!;
    private Border HudView = null!;
    private Border CreditsView = null!;
    private Border SettingsView = null!;
    private Border ResetCreditDetailsView = null!;
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
    private TextBlock CreditsTitleText = null!;
    private TextBlock CreditsDetailText = null!;
    private TextBlock ResetCreditDetailsTitleText = null!;
    private TextBlock ResetCreditSummaryText = null!;
    private TextBlock ResetCreditDetailsText = null!;
    private TextBlock AccountText = null!;
    private TextBlock ErrorText = null!;
    private TextBlock CurrentWindowLabelText = null!;
    private TextBlock WeeklyWindowLabelText = null!;
    private TextBlock TokenWindowLabelText = null!;
    private TextBlock AccountLabelText = null!;
    private TextBlock SettingsTitleText = null!;
    private TextBlock RefreshIntervalLabelText = null!;
    private TextBlock SecondsLabelText = null!;
    private TextBlock LanguageLabelText = null!;
    private TextBlock ToggleHotkeyLabelText = null!;
    private TextBlock ToggleSidebarHotkeyLabelText = null!;
    private CheckBox StartWithWindowsCheckBox = null!;
    private CheckBox AutoShowWithCodexCheckBox = null!;
    private Button SettingsButton = null!;
    private Button ResetCreditDetailsButton = null!;
    private Button QuitButton = null!;
    private Button SaveSettingsButton = null!;
    private TextBox RefreshIntervalSecondsTextBox = null!;
    private TextBox ToggleHotkeyTextBox = null!;
    private TextBox ToggleSidebarHotkeyTextBox = null!;
    private ComboBox LanguageComboBox = null!;
    private Button HomeButton = null!;
    private Button CreditsButton = null!;

    public MainWindow(UsageStore usageStore, SettingsStore settingsStore)
    {
        InitializeComponent();
        _usageStore = usageStore;
        _settingsStore = settingsStore;
        _dispatcher = WinUiDispatcherQueue.GetForCurrentThread();

        BuildLayout();
        ApplyLanguage();
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

        _usageStore.Changed += OnUsageChanged;
        _settingsStore.Changed += OnSettingsChanged;

        _barAnimationTimer.Interval = TimeSpan.FromMilliseconds(33);
        _barAnimationTimer.Tick += (_, _) => AnimateProgressBars();
        _barAnimationTimer.Start();

        Closed += (_, _) =>
        {
            _usageStore.Changed -= OnUsageChanged;
            _settingsStore.Changed -= OnSettingsChanged;
            _barAnimationTimer.Stop();
            foreach (var timer in _scrollBarHideTimers)
            {
                timer.Stop();
            }
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
        customTitleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        customTitleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        customTitleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        Grid.SetRow(customTitleBar, 0);
        RootLayout.Children.Add(customTitleBar);

        var titleText = new TextBlock
        {
            Text = "WindexBar",
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush(0xFF, 0xB9, 0xA7, 0xE8)
        };
        titleText.PointerPressed += TitleText_PointerPressed;
        customTitleBar.Children.Add(titleText);

        TitleBarDragRegion = new Grid { Background = Brush(0, 0, 0, 0) };
        Grid.SetColumn(TitleBarDragRegion, 1);
        customTitleBar.Children.Add(TitleBarDragRegion);

        var windowButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            Spacing = 7
        };
        Grid.SetColumn(windowButtons, 2);
        customTitleBar.Children.Add(windowButtons);

        windowButtons.Children.Add(CreateTitleButton(Brush(0xFF, 0xFF, 0xBD, 0x2E), MinimizeCircleButton_Click));
        windowButtons.Children.Add(CreateTitleButton(Brush(0xFF, 0x28, 0xC8, 0x40), ZoomCircleButton_Click));

        ContentRootGrid = new Grid { Padding = new Thickness(10, 0, 10, 10), ColumnSpacing = 0 };
        SideBarColumn = new ColumnDefinition { Width = new GridLength(SideBarCollapsedWidth) };
        ContentRootGrid.ColumnDefinitions.Add(SideBarColumn);
        ContentRootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(ContentRootGrid, 1);
        RootLayout.Children.Add(ContentRootGrid);

        SideBarHost = new Grid
        {
            Width = SideBarVisualWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Brush(1, 0, 0, 0)
        };
        Grid.SetRow(SideBarHost, 1);

        SideBarPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 7,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = SideBarVisualWidth,
            Opacity = 0.72
        };
        SideBarHost.Children.Add(SideBarPanel);

        HomeButton = CreateSideBarButton("\u2302");
        HomeButton.Click += HomeButton_Click;
        SideBarPanel.Children.Add(HomeButton);

        CreditsButton = CreateSideBarButton("$");
        CreditsButton.Click += CreditsButton_Click;
        SideBarPanel.Children.Add(CreditsButton);

        ResetCreditDetailsButton = CreateSideBarButton("\u21BB");
        ResetCreditDetailsButton.Click += ResetCreditDetailsButton_Click;
        SideBarPanel.Children.Add(ResetCreditDetailsButton);

        SettingsButton = CreateSideBarButton("\u2699");
        SettingsButton.Click += SettingsButton_Click;
        SideBarPanel.Children.Add(SettingsButton);

        HudView = new Border
        {
            Padding = new Thickness(11, 9, 11, 9),
            Background = Brush(0xFF, 0x1F, 0x1C, 0x24),
            BorderBrush = Brush(0x99, 0x7D, 0x62, 0xC7),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14)
        };
        Grid.SetColumn(HudView, 1);
        ContentRootGrid.Children.Add(HudView);

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
        AttachTransientScrollBar(HudScrollViewer);
        Grid.SetRow(HudScrollViewer, 0);
        hudGrid.Children.Add(HudScrollViewer);

        var hudContent = new Grid { RowSpacing = 8 };
        for (var i = 0; i < 5; i++)
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

        AddWindowSection(ModelContentPanel, 0, "Current", out CurrentWindowLabelText, out CurrentWindowPercentText, out CurrentWindowTrackRoot, out CurrentWindowFillBar, out CurrentWindowSweepBar, out CurrentWindowText);
        AddWindowSection(ModelContentPanel, 3, "Weekly", out WeeklyWindowLabelText, out WeeklyWindowPercentText, out WeeklyWindowTrackRoot, out WeeklyWindowFillBar, out WeeklyWindowSweepBar, out WeeklyWindowText);
        AddWindowSection(ModelContentPanel, 6, "Tokens", out TokenWindowLabelText, out TokenWindowPercentText, out TokenWindowTrackRoot, out TokenWindowFillBar, out TokenWindowSweepBar, out TokenWindowText);

        AccountText = AddLabelValueRow(hudContent, 3, "Account", out AccountLabelText);

        ErrorText = new TextBlock
        {
            Foreground = Brush(0xFF, 0xFF, 0x5F, 0x57),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(ErrorText, 4);
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
        QuitButton = CreateBackButton("Quit");
        QuitButton.Click += QuitButton_Click;
        hudButtons.Children.Add(QuitButton);

        CreditsView = new Border
        {
            Padding = new Thickness(11, 9, 11, 9),
            Background = Brush(0xFF, 0x1F, 0x1C, 0x24),
            BorderBrush = Brush(0x99, 0x7D, 0x62, 0xC7),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Visibility = Visibility.Collapsed
        };
        Grid.SetColumn(CreditsView, 1);
        ContentRootGrid.Children.Add(CreditsView);
        BuildCreditsView();

        SettingsView = new Border
        {
            Padding = new Thickness(11, 9, 11, 9),
            Background = Brush(0xFF, 0x1F, 0x1C, 0x24),
            BorderBrush = Brush(0x99, 0x7D, 0x62, 0xC7),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Visibility = Visibility.Collapsed
        };
        Grid.SetColumn(SettingsView, 1);
        ContentRootGrid.Children.Add(SettingsView);
        BuildSettingsView();

        ResetCreditDetailsView = new Border
        {
            Padding = new Thickness(11, 9, 11, 9),
            Background = Brush(0xFF, 0x1F, 0x1C, 0x24),
            BorderBrush = Brush(0x99, 0x7D, 0x62, 0xC7),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Visibility = Visibility.Collapsed
        };
        Grid.SetColumn(ResetCreditDetailsView, 1);
        ContentRootGrid.Children.Add(ResetCreditDetailsView);
        BuildResetCreditDetailsView();
        RootLayout.Children.Add(SideBarHost);
        ApplySideBarProgress();
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

    private static Button CreateSideBarButton(object content)
    {
        var buttonContent = content is string text
            ? new TextBlock
            {
                Text = text,
                Width = 32,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 15
            }
            : content;

        return new Button
        {
            Content = buttonContent,
            Width = 32,
            Height = 32,
            MinWidth = 32,
            MinHeight = 32,
            HorizontalAlignment = HorizontalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0),
            FontSize = 15
        };
    }

    private static void SetSideBarButtonText(Button button, string text)
    {
        if (button.Content is TextBlock textBlock)
        {
            textBlock.Text = text;
            return;
        }

        button.Content = new TextBlock
        {
            Text = text,
            Width = 32,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 15
        };
    }

    private static void AddWindowSection(
        Grid root,
        int row,
        string label,
        out TextBlock labelText,
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

        labelText = new TextBlock
        {
            Text = label,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Brush(0xFF, 0xED, 0xE7, 0xFF)
        };
        header.Children.Add(labelText);

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

    private static TextBlock AddLabelValueRow(Grid root, int row, string label, out TextBlock labelText)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        if (row == 3)
        {
            grid.Margin = new Thickness(0, 4, 0, 0);
        }

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(grid, row);
        root.Children.Add(grid);

        labelText = new TextBlock
        {
            Text = label,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        grid.Children.Add(labelText);

        var value = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        return value;
    }

    private void AttachTransientScrollBar(ScrollViewer scrollViewer)
    {
        var hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        _scrollBarHideTimers.Add(hideTimer);

        void Hide()
        {
            hideTimer.Stop();
            scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        }

        void Show(bool autoHide)
        {
            scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            hideTimer.Stop();
            if (autoHide)
            {
                hideTimer.Start();
            }
        }

        hideTimer.Tick += (_, _) => Hide();
        scrollViewer.PointerPressed += (_, _) => Show(autoHide: false);
        scrollViewer.PointerWheelChanged += (_, _) => Show(autoHide: true);
        scrollViewer.ViewChanged += (_, _) => Show(autoHide: true);
        scrollViewer.PointerReleased += (_, _) => hideTimer.Start();
        scrollViewer.PointerCanceled += (_, _) => hideTimer.Start();
        scrollViewer.PointerExited += (_, _) => hideTimer.Start();
        scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
    }

    private void BuildSettingsView()
    {
        var settingsRoot = new Grid { RowSpacing = 8 };
        settingsRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        settingsRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var settingsScrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        AttachTransientScrollBar(settingsScrollViewer);
        Grid.SetRow(settingsScrollViewer, 0);
        settingsRoot.Children.Add(settingsScrollViewer);

        var grid = new Grid { RowSpacing = 9 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        settingsScrollViewer.Content = grid;
        SettingsView.Child = settingsRoot;

        SettingsTitleText = new TextBlock
        {
            Text = "Settings",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        grid.Children.Add(SettingsTitleText);

        var intervalGrid = new Grid { ColumnSpacing = 8 };
        intervalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        intervalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        intervalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(intervalGrid, 1);
        grid.Children.Add(intervalGrid);

        RefreshIntervalLabelText = new TextBlock
        {
            Text = "Refresh interval",
            VerticalAlignment = VerticalAlignment.Center
        };
        intervalGrid.Children.Add(RefreshIntervalLabelText);
        RefreshIntervalSecondsTextBox = new TextBox { TextAlignment = TextAlignment.Right };
        Grid.SetColumn(RefreshIntervalSecondsTextBox, 1);
        intervalGrid.Children.Add(RefreshIntervalSecondsTextBox);
        SecondsLabelText = new TextBlock
        {
            Text = "s",
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(SecondsLabelText, 2);
        intervalGrid.Children.Add(SecondsLabelText);

        var languageGrid = new Grid { ColumnSpacing = 8 };
        languageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        languageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
        Grid.SetRow(languageGrid, 2);
        grid.Children.Add(languageGrid);

        LanguageLabelText = new TextBlock
        {
            Text = "Language",
            VerticalAlignment = VerticalAlignment.Center
        };
        languageGrid.Children.Add(LanguageLabelText);
        LanguageComboBox = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 124
        };
        LanguageComboBox.Items.Add(new ComboBoxItem { Content = "English", Tag = "en" });
        LanguageComboBox.Items.Add(new ComboBoxItem { Content = "\uD55C\uAD6D\uC5B4", Tag = "ko" });
        Grid.SetColumn(LanguageComboBox, 1);
        languageGrid.Children.Add(LanguageComboBox);

        var hotkeyGrid = new Grid { ColumnSpacing = 8 };
        hotkeyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hotkeyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
        Grid.SetRow(hotkeyGrid, 3);
        grid.Children.Add(hotkeyGrid);

        ToggleHotkeyLabelText = new TextBlock
        {
            Text = "Toggle shortcut",
            VerticalAlignment = VerticalAlignment.Center
        };
        hotkeyGrid.Children.Add(ToggleHotkeyLabelText);
        ToggleHotkeyTextBox = new TextBox
        {
            TextAlignment = TextAlignment.Right,
            MinWidth = 124
        };
        Grid.SetColumn(ToggleHotkeyTextBox, 1);
        hotkeyGrid.Children.Add(ToggleHotkeyTextBox);

        var sidebarHotkeyGrid = new Grid { ColumnSpacing = 8 };
        sidebarHotkeyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sidebarHotkeyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
        Grid.SetRow(sidebarHotkeyGrid, 4);
        grid.Children.Add(sidebarHotkeyGrid);

        ToggleSidebarHotkeyLabelText = new TextBlock
        {
            Text = "Sidebar shortcut",
            VerticalAlignment = VerticalAlignment.Center
        };
        sidebarHotkeyGrid.Children.Add(ToggleSidebarHotkeyLabelText);
        ToggleSidebarHotkeyTextBox = new TextBox
        {
            TextAlignment = TextAlignment.Right,
            MinWidth = 124
        };
        Grid.SetColumn(ToggleSidebarHotkeyTextBox, 1);
        sidebarHotkeyGrid.Children.Add(ToggleSidebarHotkeyTextBox);

        StartWithWindowsCheckBox = new CheckBox
        {
            Content = "Start with Windows"
        };
        Grid.SetRow(StartWithWindowsCheckBox, 5);
        grid.Children.Add(StartWithWindowsCheckBox);

        AutoShowWithCodexCheckBox = new CheckBox
        {
            Content = "Show only while using Codex"
        };
        AutoShowWithCodexCheckBox.Checked += (_, _) => ApplyAutoShowShortcutState();
        AutoShowWithCodexCheckBox.Unchecked += (_, _) => ApplyAutoShowShortcutState();
        Grid.SetRow(AutoShowWithCodexCheckBox, 6);
        grid.Children.Add(AutoShowWithCodexCheckBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 6
        };
        Grid.SetRow(buttons, 1);
        settingsRoot.Children.Add(buttons);
        SaveSettingsButton = CreateBackButton("Save");
        SaveSettingsButton.Click += SaveSettingsButton_Click;
        buttons.Children.Add(SaveSettingsButton);
    }

    private void BuildCreditsView()
    {
        var grid = new Grid { RowSpacing = 10 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        CreditsView.Child = grid;

        CreditsTitleText = new TextBlock
        {
            Text = "Credits",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        grid.Children.Add(CreditsTitleText);

        CreditsDetailText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush(0xFF, 0xED, 0xE7, 0xFF)
        };
        Grid.SetRow(CreditsDetailText, 1);
        grid.Children.Add(CreditsDetailText);

    }

    private void BuildResetCreditDetailsView()
    {
        var grid = new Grid { RowSpacing = 10 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        ResetCreditDetailsView.Child = grid;

        ResetCreditDetailsTitleText = new TextBlock
        {
            Text = "Credit details",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        grid.Children.Add(ResetCreditDetailsTitleText);

        ResetCreditSummaryText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush(0xFF, 0xED, 0xE7, 0xFF)
        };
        Grid.SetRow(ResetCreditSummaryText, 1);
        grid.Children.Add(ResetCreditSummaryText);

        ResetCreditDetailsText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            Foreground = Brush(0xFF, 0xED, 0xE7, 0xFF)
        };
        Grid.SetRow(ResetCreditDetailsText, 2);
        grid.Children.Add(ResetCreditDetailsText);

    }

    private static Button CreateBackButton(object content)
    {
        return new Button
        {
            Content = content,
            MinWidth = 56,
            MinHeight = 26,
            Padding = new Thickness(9, 2, 9, 2),
            FontSize = 12
        };
    }

    private static SolidColorBrush Brush(byte a, byte r, byte g, byte b) =>
        new(global::Windows.UI.Color.FromArgb(a, r, g, b));

    public void ShowHudView()
    {
        CreditsView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        ResetCreditDetailsView.Visibility = Visibility.Collapsed;
        HudView.Visibility = Visibility.Visible;
        ApplyLanguage();
        RootLayout.Focus(FocusState.Programmatic);
        UpdateState();
    }

    public void ShowCreditsView()
    {
        HudView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        ResetCreditDetailsView.Visibility = Visibility.Collapsed;
        CreditsView.Visibility = Visibility.Visible;
        ApplyLanguage();
        RootLayout.Focus(FocusState.Programmatic);
        UpdateCredits(_usageStore.Credits);
    }

    public void ShowSettingsView()
    {
        RefreshIntervalSecondsTextBox.Text = _settingsStore.Codex.RefreshIntervalSeconds.ToString();
        ToggleHotkeyTextBox.Text = _settingsStore.Config.Hotkeys.ToggleWindow;
        ToggleSidebarHotkeyTextBox.Text = _settingsStore.Config.Hotkeys.ToggleSidebar;
        StartWithWindowsCheckBox.IsChecked = _settingsStore.Config.StartWithWindows;
        AutoShowWithCodexCheckBox.IsChecked = _settingsStore.Config.AutoShowWithCodex;
        ApplyAutoShowShortcutState();
        SelectLanguage(_settingsStore.Config.Language);
        HudView.Visibility = Visibility.Collapsed;
        CreditsView.Visibility = Visibility.Collapsed;
        ResetCreditDetailsView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Visible;
        ApplyLanguage();
        _modelUsages.Clear();
    }

    public void ShowResetCreditDetailsView()
    {
        HudView.Visibility = Visibility.Collapsed;
        CreditsView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        ResetCreditDetailsView.Visibility = Visibility.Visible;
        ApplyLanguage();
        RootLayout.Focus(FocusState.Programmatic);
        UpdateResetCreditDetails(_usageStore.Snapshot?.RateLimitResetCredits);
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
            ResizeClientToEffectiveSize(SettingsClientWidth, ContentClientHeight);
            return;
        }

        ResizeClientToEffectiveSize(HudClientWidth, ContentClientHeight);
    }

    private void ResizeClientToEffectiveSize(double width, double height)
    {
        var scale = RootLayout.XamlRoot?.RasterizationScale ?? 1;
        var position = _windowPlacement.PositionForResize(new WindowPosition(AppWindow.Position.X, AppWindow.Position.Y));
        var sideBarWidth = _isSideBarOpen ? SideBarOuterWidth : SideBarCollapsedWidth;
        AppWindow.ResizeClient(new SizeInt32(
            (int)Math.Ceiling((width + sideBarWidth - SideBarCollapsedWidth) * scale),
            (int)Math.Ceiling(height * scale)));
        AppWindow.Move(new PointInt32(position.X, position.Y));
    }

    private void AnimateProgressBars()
    {
        var sweepStep = _isFastServiceTier ? FastBarSweepStep : StandardBarSweepStep;
        var activeWindowEaseFactor = _isFastServiceTier ? FastBarEaseFactor : StandardBarEaseFactor;

        _barSweepPhase = (_barSweepPhase + sweepStep) % 1.0;
        var activeWindowSweep = EaseSweep(_barSweepPhase);
        _currentBarValue = EaseBarValue(_currentBarValue, _targetCurrentBarValue, activeWindowEaseFactor);
        _weeklyBarValue = EaseBarValue(_weeklyBarValue, _targetWeeklyBarValue, activeWindowEaseFactor);
        _tokenBarValue = EaseBarValue(_tokenBarValue, _targetTokenBarValue, StandardBarEaseFactor);
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

    private static double EaseBarValue(double current, double target, double factor)
    {
        var delta = target - current;
        return Math.Abs(delta) < 0.1 ? target : current + (delta * factor);
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

    private void ApplySideBarProgress()
    {
        ApplySideBarLayout();
    }

    private void ApplySideBarLayout()
    {
        SideBarColumn.Width = new GridLength(_isSideBarOpen ? SideBarExpandedWidth : SideBarCollapsedWidth);
        SideBarHost.Width = _isSideBarOpen ? SideBarVisualWidth : SideBarCollapsedWidth;
        ContentRootGrid.ColumnSpacing = _isSideBarOpen ? SideBarExpandedGap : 0;
        SideBarPanel.Opacity = _isSideBarOpen ? 0.72 : 0;
        SideBarPanel.IsHitTestVisible = _isSideBarOpen;
    }

    public void ToggleSideBar()
    {
        _isSideBarOpen = !_isSideBarOpen;
        ApplySideBarLayout();
        ResizeForCurrentView();
    }

    private void OnUsageChanged(object? sender, EventArgs args)
    {
        _dispatcher.TryEnqueue(UpdateState);
    }

    private void OnSettingsChanged(object? sender, EventArgs args)
    {
        _dispatcher.TryEnqueue(() =>
        {
            ApplyLanguage();
            UpdateState();
        });
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

    private void MinimizeCircleButton_Click(object sender, RoutedEventArgs args)
    {
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

    private void HomeButton_Click(object sender, RoutedEventArgs args) => ShowHudView();

    private void TitleText_PointerPressed(object sender, PointerRoutedEventArgs args)
    {
        ToggleSideBar();
        args.Handled = true;
    }

    private void CreditsButton_Click(object sender, RoutedEventArgs args)
    {
        if (CreditsView.Visibility == Visibility.Visible)
        {
            ShowHudView();
            return;
        }

        ShowCreditsView();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs args)
    {
        if (SettingsView.Visibility == Visibility.Visible)
        {
            ShowHudView();
            return;
        }

        ShowSettingsView();
    }

    private void ResetCreditDetailsButton_Click(object sender, RoutedEventArgs args)
    {
        if (ResetCreditDetailsView.Visibility == Visibility.Visible)
        {
            ShowHudView();
            return;
        }

        ShowResetCreditDetailsView();
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs args)
    {
        _settingsStore.Update(config =>
        {
            var codex = config.GetProviderConfig(UsageProvider.Codex);
            codex.RefreshIntervalSeconds = ReadRefreshIntervalSeconds();
            config.SetProviderConfig(codex);
            config.Language = ReadSelectedLanguage();
            config.Hotkeys.ToggleWindow = HotkeyShortcut.NormalizeOrDefault(
                ToggleHotkeyTextBox.Text,
                WindexBarConfig.DefaultToggleWindowHotkey);
            config.Hotkeys.ToggleSidebar = HotkeyShortcut.NormalizeOrDefault(
                ToggleSidebarHotkeyTextBox.Text,
                WindexBarConfig.DefaultToggleSidebarHotkey);
            config.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
            config.AutoShowWithCodex = AutoShowWithCodexCheckBox.IsChecked == true;
        });
        StartupShortcutService.Apply(_settingsStore.Config.StartWithWindows);
        _usageStore.StartBackgroundRefresh();
        ShowHudView();
    }

    private void ApplyAutoShowShortcutState()
    {
        var autoShowEnabled = AutoShowWithCodexCheckBox.IsChecked == true;
        ToggleHotkeyTextBox.IsEnabled = !autoShowEnabled;
        ToggleHotkeyTextBox.Opacity = autoShowEnabled ? 0.45 : 1;
        ToggleHotkeyLabelText.Opacity = autoShowEnabled ? 0.65 : 1;
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

    private string ReadSelectedLanguage()
    {
        if (LanguageComboBox.SelectedItem is ComboBoxItem { Tag: string language })
        {
            return WindexBarConfig.NormalizeLanguage(language);
        }

        return WindexBarConfig.DefaultLanguage;
    }

    private void SelectLanguage(string? language)
    {
        var normalized = WindexBarConfig.NormalizeLanguage(language);
        foreach (var item in LanguageComboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string value && WindexBarConfig.NormalizeLanguage(value) == normalized)
            {
                LanguageComboBox.SelectedItem = item;
                return;
            }
        }

        LanguageComboBox.SelectedIndex = 0;
    }

    private void ApplyLanguage()
    {
        Title = SettingsView.Visibility == Visibility.Visible ? Text("WindexBar Settings", "WindexBar \uC124\uC815") : "WindexBar";
        ApplyWindowSectionLabels();
        AccountLabelText.Text = Text("Account", "\uACC4\uC815");
        SetSideBarButtonText(HomeButton, "\u2302");
        SetSideBarButtonText(CreditsButton, "$");
        SetSideBarButtonText(SettingsButton, "\u2699");
        SetSideBarButtonText(ResetCreditDetailsButton, "\u21BB");
        QuitButton.Content = Text("Quit", "\uC885\uB8CC");
        CreditsTitleText.Text = Text("Credits", "\uD06C\uB808\uB527");
        ResetCreditDetailsTitleText.Text = Text("Reset credit details", "\uCD08\uAE30\uD654\uAD8C \uC0C1\uC138");
        SettingsTitleText.Text = Text("Settings", "\uC124\uC815");
        RefreshIntervalLabelText.Text = Text("Refresh interval", "\uC0C8\uB85C\uACE0\uCE68 \uAC04\uACA9");
        SecondsLabelText.Text = Text("s", "\uCD08");
        LanguageLabelText.Text = Text("Language", "\uC5B8\uC5B4");
        ToggleHotkeyLabelText.Text = Text("Toggle shortcut", "\uD1A0\uAE00 \uB2E8\uCD95\uD0A4");
        ToggleSidebarHotkeyLabelText.Text = Text("Sidebar shortcut", "\uC0AC\uC774\uB4DC\uBC14 \uB2E8\uCD95\uD0A4");
        StartWithWindowsCheckBox.Content = Text("Start with Windows", "Windows \uC2DC\uC791 \uC2DC \uC2E4\uD589");
        AutoShowWithCodexCheckBox.Content = Text("Show only while using ChatGPT or Codex", "ChatGPT \uB610\uB294 Codex \uC0AC\uC6A9 \uC911\uC5D0\uB9CC \uD45C\uC2DC");
        SaveSettingsButton.Content = Text("Save", "\uC800\uC7A5");
    }

    private string CurrentLanguage => WindexBarConfig.NormalizeLanguage(_settingsStore.Config.Language);

    private bool IsKorean => CurrentLanguage == "ko";

    private string Text(string english, string korean) => IsKorean ? korean : english;

    private string UnknownText => Text("unknown", "\uC54C \uC218 \uC5C6\uC74C");

    private void ApplyWindowSectionLabels()
    {
        CurrentWindowLabelText.Text = WithFastIndicator(Text("Current", "\uD604\uC7AC"));
        WeeklyWindowLabelText.Text = WithFastIndicator(Text("Weekly", "\uC8FC\uAC04"));
        TokenWindowLabelText.Text = Text("Tokens", "\uD1A0\uD070");
    }

    private string WithFastIndicator(string label) =>
        _isFastServiceTier ? $"{label} {FastIndicatorGlyph}" : label;

    private void ApplyProgressBarTheme()
    {
        ApplyProgressBarTheme(CurrentWindowFillBar, CurrentWindowSweepBar, _isFastServiceTier);
        ApplyProgressBarTheme(WeeklyWindowFillBar, WeeklyWindowSweepBar, _isFastServiceTier);
        ApplyProgressBarTheme(TokenWindowFillBar, TokenWindowSweepBar, isFast: false);
    }

    private static void ApplyProgressBarTheme(Border fillBar, Border sweepBar, bool isFast)
    {
        fillBar.Background = isFast
            ? Brush(0xFF, 0xD7, 0x56, 0x7D)
            : Brush(0xFF, 0x8D, 0x78, 0xD6);
        sweepBar.Background = isFast
            ? Brush(0xFF, 0xFF, 0x9D, 0xB2)
            : Brush(0xFF, 0xC8, 0xB9, 0xFF);
    }

    private static bool IsFastServiceTier(CodexModelSelection? activeModel) =>
        string.Equals(activeModel?.ServiceTier, "fast", StringComparison.OrdinalIgnoreCase);

    private void QuitButton_Click(object sender, RoutedEventArgs args) => App.Current.Shutdown();

    private void UpdateState()
    {
        BuildModelUsages();

        var snapshot = _usageStore.Snapshot;
        var credits = _usageStore.Credits;
        var disabled = !_settingsStore.Codex.Enabled;

        _isFastServiceTier = IsFastServiceTier(snapshot?.ActiveModel);
        ApplyWindowSectionLabels();
        ApplyProgressBarTheme();

        UpdateCredits(credits);
        UpdateResetCreditDetails(snapshot?.RateLimitResetCredits);
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

    private void ApplyWindowView(TextBlock percentText, TextBlock detailText, RateWindow? window, out double targetValue)
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
            return Text("Provider disabled", "\uC81C\uACF5\uC790 \uBE44\uD65C\uC131\uD654");
        }

        return string.IsNullOrWhiteSpace(_usageStore.LastError) ? string.Empty : _usageStore.LastError;
    }

    private string FormatHudPercent(RateWindow? window) =>
        window is null ? UnknownText : $"{window.RemainingPercent:0.#}%";

    private string FormatWindow(RateWindow? window)
    {
        if (window is null)
        {
            return UnknownText;
        }

        var reset = window.ResetsAt is null
            ? string.Empty
            : IsKorean ? $", \uCD08\uAE30\uD654 {FormatResetDescription(window.ResetsAt.Value)}" : $", resets {FormatResetDescription(window.ResetsAt.Value)}";
        return IsKorean ? $"{window.UsedPercent:0.#}% \uC0AC\uC6A9{reset}" : $"Used {window.UsedPercent:0.#}%{reset}";
    }

    private void UpdateCredits(CreditsSnapshot? credits)
    {
        CreditsDetailText.Text = FormatCredits(credits);
    }

    private void UpdateResetCreditDetails(RateLimitResetCreditsSnapshot? resetCredits)
    {
        ResetCreditSummaryText.Text = string.Join(
            Environment.NewLine,
            RateLimitResetCreditFormatter.FormatSummary(resetCredits, CurrentLanguage),
            "\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500");
        ResetCreditDetailsText.Text = RateLimitResetCreditFormatter.FormatDetail(resetCredits, CurrentLanguage);
    }

    private string FormatCredits(CreditsSnapshot? credits)
    {
        if (credits is null)
        {
            return UnknownText;
        }

        var balance = IsKorean
            ? $"\ud83d\udcb0 {credits.Remaining:0.##}\uAC1C \uBCF4\uC720"
            : $"\ud83d\udcb0 {credits.Remaining:0.##} held";
        var updated = IsKorean
            ? $"Updated: {credits.UpdatedAt:yyyy-MM-dd HH:mm}"
            : $"Updated: {credits.UpdatedAt:yyyy-MM-dd HH:mm}";
        return string.Join(Environment.NewLine, balance, updated);
    }

    private string FormatResetDescription(DateTimeOffset resetsAt)
    {
        var delta = resetsAt - DateTimeOffset.Now;
        if (delta.TotalSeconds <= 0)
        {
            return Text("now", "\uC9C0\uAE08");
        }

        if (delta.TotalHours >= 24)
        {
            var days = delta.TotalDays >= 10 ? Math.Round(delta.TotalDays) : Math.Round(delta.TotalDays, 1);
            return IsKorean ? $"{days:0.#}\uC77C \uD6C4" : $"in {days:0.#}d";
        }

        if (delta.TotalHours >= 1)
        {
            var hours = Math.Max(1, (int)Math.Round(delta.TotalHours));
            return IsKorean ? $"{hours}\uC2DC\uAC04 \uD6C4" : $"in {hours}h";
        }

        var minutes = Math.Max(1, (int)Math.Round(delta.TotalMinutes));
        return IsKorean ? $"{minutes}\uBD84 \uD6C4" : $"in {minutes}m";
    }

    private string FormatTokenPercent(TokenUsageSnapshot? tokenUsage)
    {
        var percent = TokenContextPercent(tokenUsage);
        return percent is null ? UnknownText : $"{percent.Value:0.#}%";
    }

    private string FormatTokenUsage(TokenUsageSnapshot? tokenUsage)
    {
        if (tokenUsage is null)
        {
            return UnknownText;
        }

        var values = new List<string>();
        var current = tokenUsage.Last ?? tokenUsage.Total;
        if (current is not null && tokenUsage.ModelContextWindow is { } contextWindow)
        {
            values.Add($"{Text("Context", "\uCEE8\uD14D\uC2A4\uD2B8")}: {TokenCountFormatter.Format(current.TotalTokens, CurrentLanguage)} / {TokenCountFormatter.Format(contextWindow, CurrentLanguage)}");
        }
        else if (current is not null)
        {
            values.Add($"{Text("Context", "\uCEE8\uD14D\uC2A4\uD2B8")}: {TokenCountFormatter.Format(current.TotalTokens, CurrentLanguage)}");
        }

        if (tokenUsage.Total is not null)
        {
            values.Add($"{Text("Session total", "\uC138\uC158 \uD569\uACC4")}: {TokenCountFormatter.Format(tokenUsage.Total.TotalTokens, CurrentLanguage)}");
        }

        return values.Count == 0 ? UnknownText : string.Join(Environment.NewLine, values);
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

    private string FormatIdentity(ProviderIdentitySnapshot? identity)
    {
        if (identity is null || (string.IsNullOrWhiteSpace(identity.AccountEmail) && string.IsNullOrWhiteSpace(identity.LoginMethod)))
        {
            return UnknownText;
        }

        if (string.IsNullOrWhiteSpace(identity.AccountEmail))
        {
            return identity.LoginMethod ?? UnknownText;
        }

        if (string.IsNullOrWhiteSpace(identity.LoginMethod))
        {
            return identity.AccountEmail;
        }

        return $"{identity.AccountEmail} ({identity.LoginMethod})";
    }

    private sealed record ModelUsageView(string DisplayName, RateWindow? Current, RateWindow? Weekly);
}
