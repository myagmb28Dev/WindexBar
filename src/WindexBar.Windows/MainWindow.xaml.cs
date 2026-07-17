using WindexBar.Core.Config;
using WindexBar.Core.Formatting;
using WindexBar.Core.Models;
using WindexBar.Core.Refresh;
using WindexBar.Core.Windowing;
using WindexBar.Core.Updates;
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
    private readonly CodexCliUpdateService _codexCliUpdateService;
    private readonly WinUiDispatcherQueue _dispatcher;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly WindowPlacementController _windowPlacement = new(new WindowPosition(96, 96));
    private readonly DispatcherTimer _barAnimationTimer = new();
    private readonly List<DispatcherTimer> _scrollBarHideTimers = [];
    private readonly List<ModelUsageView> _modelUsages = [];
    private readonly List<Button> _quitButtons = [];
    private readonly List<SessionUsageBarView> _sessionUsageBars = [];
    private readonly Dictionary<string, double> _sessionBarValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _collapsedSessionGroups = new(StringComparer.OrdinalIgnoreCase);
    private double _barSweepPhase;
    private double _stylePreviewSweepPhase;
    private double _currentBarValue;
    private double _targetCurrentBarValue;
    private double _weeklyBarValue;
    private double _targetWeeklyBarValue;
    private bool _isFastServiceTier;
    private bool _isSideBarOpen = true;
    private bool _projectSessionsFirst = true;
    private bool _codexVersionCheckStarted;
    private bool _codexVersionDialogOpen;
    private CodexVersionCheckResult? _lastCodexVersionCheck;
    private Grid TitleBarDragRegion = null!;
    private Grid ContentRootGrid = null!;
    private Grid SideBarHost = null!;
    private ColumnDefinition SideBarColumn = null!;
    private StackPanel SideBarPanel = null!;
    private Border HudView = null!;
    private Border SessionsView = null!;
    private Border CreditsView = null!;
    private Border StyleView = null!;
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
    private StackPanel SessionUsagePanel = null!;
    private TextBlock CreditsTitleText = null!;
    private TextBlock CreditsDetailText = null!;
    private TextBlock ResetCreditDetailsTitleText = null!;
    private TextBlock ResetCreditSummaryText = null!;
    private TextBlock ResetCreditDetailsText = null!;
    private TextBlock AccountText = null!;
    private TextBlock ErrorText = null!;
    private TextBlock CurrentWindowLabelText = null!;
    private TextBlock WeeklyWindowLabelText = null!;
    private TextBlock SessionsLabelText = null!;
    private Microsoft.UI.Xaml.Controls.Primitives.ToggleButton SessionSortToggleButton = null!;
    private TextBlock AccountLabelText = null!;
    private TextBlock SettingsTitleText = null!;
    private TextBlock StyleTitleText = null!;
    private TextBlock GaugeThicknessLabelText = null!;
    private TextBlock GaugeColorLabelText = null!;
    private TextBlock GaugeAnimationLabelText = null!;
    private TextBlock RefreshIntervalLabelText = null!;
    private TextBlock SecondsLabelText = null!;
    private TextBlock LanguageLabelText = null!;
    private TextBlock ToggleHotkeyLabelText = null!;
    private TextBlock ToggleSidebarHotkeyLabelText = null!;
    private CheckBox StartWithWindowsCheckBox = null!;
    private CheckBox AutoShowWithCodexCheckBox = null!;
    private Button SettingsButton = null!;
    private Button StyleButton = null!;
    private Button ResetCreditDetailsButton = null!;
    private Button QuitButton = null!;
    private Button SaveSettingsButton = null!;
    private Button SaveStyleButton = null!;
    private TextBox RefreshIntervalSecondsTextBox = null!;
    private TextBox ToggleHotkeyTextBox = null!;
    private TextBox ToggleSidebarHotkeyTextBox = null!;
    private ComboBox LanguageComboBox = null!;
    private ComboBox GaugeThicknessComboBox = null!;
    private ComboBox GaugeAnimationComboBox = null!;
    private Button GaugeColorButton = null!;
    private Window? _gaugeColorWindow;
    private global::Windows.UI.Color _selectedGaugeColor = global::Windows.UI.Color.FromArgb(0xFF, 0x8D, 0x78, 0xD6);
    private global::Windows.UI.Color _previewGaugeColor = global::Windows.UI.Color.FromArgb(0xFF, 0x8D, 0x78, 0xD6);
    private Grid StylePreviewTrackRoot = null!;
    private Border StylePreviewFillBar = null!;
    private Border StylePreviewSweepBar = null!;
    private ComboBox CodexInstallMethodComboBox = null!;
    private TextBlock CodexInstallMethodLabelText = null!;
    private TextBlock CodexUpdateCommandPreviewText = null!;
    private TextBlock CurrentCodexVersionText = null!;
    private Button CheckCodexVersionButton = null!;
    private TextBlock CustomCodexUpdateCommandLabelText = null!;
    private TextBox CustomCodexUpdateCommandTextBox = null!;
    private Button HomeButton = null!;
    private Button SessionsButton = null!;
    private Button CreditsButton = null!;

    public MainWindow(
        UsageStore usageStore,
        SettingsStore settingsStore,
        CodexCliUpdateService codexCliUpdateService)
    {
        InitializeComponent();
        _usageStore = usageStore;
        _settingsStore = settingsStore;
        _codexCliUpdateService = codexCliUpdateService;
        _dispatcher = WinUiDispatcherQueue.GetForCurrentThread();

        BuildLayout();
        ApplyLanguage();
        ConfigureCompactWindow();
        AppWindow.Changed += OnAppWindowChanged;
        RootLayout.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnModelNavigationKeyDown), true);
        RootLayout.PointerPressed += (_, _) =>
        {
            RootLayout.Focus(FocusState.Pointer);
        };

        RootLayout.Loaded += async (_, _) =>
        {
            RootLayout.Focus(FocusState.Programmatic);
            ResizeForCurrentView();
            if (!_codexVersionCheckStarted)
            {
                _codexVersionCheckStarted = true;
                await CheckCodexVersionAsync(forceLatestVersionRefresh: false);
            }
        };

        _usageStore.Changed += OnUsageChanged;
        _settingsStore.Changed += OnSettingsChanged;

        _barAnimationTimer.Interval = TimeSpan.FromMilliseconds(33);
        _barAnimationTimer.Tick += (_, _) => AnimateProgressBars();
        _barAnimationTimer.Start();

        Closed += (_, _) =>
        {
            _gaugeColorWindow?.Close();
            _gaugeColorWindow = null;
            AppWindow.Changed -= OnAppWindowChanged;
            _usageStore.Changed -= OnUsageChanged;
            _settingsStore.Changed -= OnSettingsChanged;
            _lifetimeCancellation.Cancel();
            _lifetimeCancellation.Dispose();
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
            Text = $"WindexBar {AppReleaseVersion.DisplayValue}",
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

        SessionsButton = CreateSideBarButton("\u2637");
        SessionsButton.Click += SessionsButton_Click;
        SideBarPanel.Children.Add(SessionsButton);

        CreditsButton = CreateSideBarButton("$");
        CreditsButton.Click += CreditsButton_Click;
        SideBarPanel.Children.Add(CreditsButton);

        ResetCreditDetailsButton = CreateSideBarButton("\u21BB");
        ResetCreditDetailsButton.Click += ResetCreditDetailsButton_Click;
        SideBarPanel.Children.Add(ResetCreditDetailsButton);

        StyleButton = CreateSideBarButton("\u25C8");
        StyleButton.Click += StyleButton_Click;
        SideBarPanel.Children.Add(StyleButton);

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
        for (var i = 0; i < 6; i++)
        {
            ModelContentPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        Grid.SetRow(ModelContentPanel, 2);
        hudContent.Children.Add(ModelContentPanel);

        AddWindowSection(ModelContentPanel, 0, "Current", out CurrentWindowLabelText, out CurrentWindowPercentText, out CurrentWindowTrackRoot, out CurrentWindowFillBar, out CurrentWindowSweepBar, out CurrentWindowText);
        AddWindowSection(ModelContentPanel, 3, "Weekly", out WeeklyWindowLabelText, out WeeklyWindowPercentText, out WeeklyWindowTrackRoot, out WeeklyWindowFillBar, out WeeklyWindowSweepBar, out WeeklyWindowText);

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
        QuitButton = CreateQuitButton();
        hudButtons.Children.Add(QuitButton);

        SessionsView = new Border
        {
            Padding = new Thickness(11, 9, 11, 9),
            Background = Brush(0xFF, 0x1F, 0x1C, 0x24),
            BorderBrush = Brush(0x99, 0x7D, 0x62, 0xC7),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Visibility = Visibility.Collapsed
        };
        Grid.SetColumn(SessionsView, 1);
        ContentRootGrid.Children.Add(SessionsView);
        BuildSessionsView();

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

        StyleView = new Border
        {
            Padding = new Thickness(11, 9, 11, 9),
            Background = Brush(0xFF, 0x1F, 0x1C, 0x24),
            BorderBrush = Brush(0x99, 0x7D, 0x62, 0xC7),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Visibility = Visibility.Collapsed
        };
        Grid.SetColumn(StyleView, 1);
        ContentRootGrid.Children.Add(StyleView);
        BuildStyleView();

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

    private void BuildSessionsView()
    {
        var sessionsRoot = new Grid { RowSpacing = 7 };
        sessionsRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        sessionsRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        SessionsView.Child = sessionsRoot;

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Auto
        };
        AttachTransientScrollBar(scrollViewer);
        Grid.SetRow(scrollViewer, 0);
        sessionsRoot.Children.Add(scrollViewer);

        var content = new StackPanel { Spacing = 8 };
        scrollViewer.Content = content;

        var sessionsTitleRow = new Grid();
        sessionsTitleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sessionsTitleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        SessionsLabelText = new TextBlock
        {
            Text = "Sessions",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        AttachSectionTitleHomeNavigation(SessionsLabelText);
        sessionsTitleRow.Children.Add(SessionsLabelText);

        SessionSortToggleButton = new Microsoft.UI.Xaml.Controls.Primitives.ToggleButton
        {
            Width = 38,
            Height = 24,
            MinWidth = 38,
            MinHeight = 24,
            Padding = new Thickness(4, 0, 4, 0),
            FontSize = 10,
            IsChecked = true,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(SessionSortToggleButton, 1);
        sessionsTitleRow.Children.Add(SessionSortToggleButton);
        content.Children.Add(sessionsTitleRow);
        content.Children.Add(CreateSectionDivider());

        SessionUsagePanel = new StackPanel { Spacing = 7 };
        content.Children.Add(SessionUsagePanel);
        SessionSortToggleButton.Checked += (_, _) => ApplySessionSortPreference(projectSessionsFirst: true);
        SessionSortToggleButton.Unchecked += (_, _) => ApplySessionSortPreference(projectSessionsFirst: false);
        UpdateSessionSortToggleAppearance();

        var sessionsButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        Grid.SetRow(sessionsButtons, 1);
        sessionsRoot.Children.Add(sessionsButtons);
        sessionsButtons.Children.Add(CreateQuitButton());
    }

    private void ApplySessionSortPreference(bool projectSessionsFirst)
    {
        _projectSessionsFirst = projectSessionsFirst;
        UpdateSessionSortToggleAppearance();
        UpdateSessionUsageView(_usageStore.Snapshot?.Sessions);
    }

    private void UpdateSessionSortToggleAppearance()
    {
        SessionSortToggleButton.Content = _projectSessionsFirst ? "P\u2191" : "N\u2191";
        ToolTipService.SetToolTip(
            SessionSortToggleButton,
            _projectSessionsFirst
                ? Text("Project sessions first", "\uD504\uB85C\uC81D\uD2B8 \uC138\uC158 \uC6B0\uC120")
                : Text("Non-project sessions first", "\uBE44\uD504\uB85C\uC81D\uD2B8 \uC138\uC158 \uC6B0\uC120"));
    }

    private static Border CreateSectionDivider() => new()
    {
        Height = 1,
        Margin = new Thickness(0, 0, 0, 2),
        Background = Brush(0x88, 0x7D, 0x62, 0xC7),
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    private void AttachSectionTitleHomeNavigation(TextBlock title)
    {
        title.PointerPressed += (_, args) =>
        {
            ShowHudView();
            args.Handled = true;
        };
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
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        AttachSectionTitleHomeNavigation(SettingsTitleText);
        grid.Children.Add(SettingsTitleText);
        var settingsDivider = CreateSectionDivider();
        Grid.SetRow(settingsDivider, 1);
        grid.Children.Add(settingsDivider);

        var intervalGrid = new Grid { ColumnSpacing = 8 };
        intervalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        intervalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        intervalGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(intervalGrid, 2);
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
        Grid.SetRow(languageGrid, 3);
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
        Grid.SetRow(hotkeyGrid, 4);
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
        Grid.SetRow(sidebarHotkeyGrid, 5);
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
        Grid.SetRow(StartWithWindowsCheckBox, 6);
        grid.Children.Add(StartWithWindowsCheckBox);

        AutoShowWithCodexCheckBox = new CheckBox
        {
            Content = "Show only while using Codex"
        };
        AutoShowWithCodexCheckBox.Checked += (_, _) => ApplyAutoShowShortcutState();
        AutoShowWithCodexCheckBox.Unchecked += (_, _) => ApplyAutoShowShortcutState();
        Grid.SetRow(AutoShowWithCodexCheckBox, 7);
        grid.Children.Add(AutoShowWithCodexCheckBox);

        var codexVersionGrid = new Grid { ColumnSpacing = 8 };
        codexVersionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        codexVersionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(codexVersionGrid, 8);
        grid.Children.Add(codexVersionGrid);
        CurrentCodexVersionText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        codexVersionGrid.Children.Add(CurrentCodexVersionText);
        CheckCodexVersionButton = CreateBackButton("Check now");
        CheckCodexVersionButton.Click += CheckCodexVersionButton_Click;
        Grid.SetColumn(CheckCodexVersionButton, 1);
        codexVersionGrid.Children.Add(CheckCodexVersionButton);

        var installMethodGrid = new Grid { ColumnSpacing = 8 };
        installMethodGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        installMethodGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
        Grid.SetRow(installMethodGrid, 9);
        grid.Children.Add(installMethodGrid);
        CodexInstallMethodLabelText = new TextBlock
        {
            Text = "Codex install method",
            VerticalAlignment = VerticalAlignment.Center
        };
        installMethodGrid.Children.Add(CodexInstallMethodLabelText);
        CodexInstallMethodComboBox = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 124
        };
        AddInstallMethodItem("Auto-detect", CodexInstallMethodNames.Auto);
        AddInstallMethodItem("PowerShell", CodexInstallMethodNames.PowerShell);
        AddInstallMethodItem("npm", CodexInstallMethodNames.Npm);
        AddInstallMethodItem("Bun", CodexInstallMethodNames.Bun);
        AddInstallMethodItem("Homebrew", CodexInstallMethodNames.Homebrew);
        AddInstallMethodItem("WinGet", CodexInstallMethodNames.WinGet);
        AddInstallMethodItem("Custom", CodexInstallMethodNames.Custom);
        CodexInstallMethodComboBox.SelectionChanged += (_, _) => ApplyCustomUpdateCommandVisibility();
        Grid.SetColumn(CodexInstallMethodComboBox, 1);
        installMethodGrid.Children.Add(CodexInstallMethodComboBox);

        CodexUpdateCommandPreviewText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72,
            FontSize = 11
        };
        Grid.SetRow(CodexUpdateCommandPreviewText, 10);
        grid.Children.Add(CodexUpdateCommandPreviewText);

        CustomCodexUpdateCommandLabelText = new TextBlock
        {
            Text = "Custom update command",
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(CustomCodexUpdateCommandLabelText, 11);
        grid.Children.Add(CustomCodexUpdateCommandLabelText);
        CustomCodexUpdateCommandTextBox = new TextBox
        {
            PlaceholderText = "Enter a Codex CLI update command.",
            TextWrapping = TextWrapping.Wrap
        };
        CustomCodexUpdateCommandTextBox.TextChanged += (_, _) => UpdateCodexUpdateCommandPreview();
        Grid.SetRow(CustomCodexUpdateCommandTextBox, 12);
        grid.Children.Add(CustomCodexUpdateCommandTextBox);

        var buttons = new Grid { ColumnSpacing = 6 };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(buttons, 1);
        settingsRoot.Children.Add(buttons);
        SaveSettingsButton = CreateBackButton("Save");
        SaveSettingsButton.HorizontalAlignment = HorizontalAlignment.Left;
        SaveSettingsButton.Click += SaveSettingsButton_Click;
        buttons.Children.Add(SaveSettingsButton);
        var settingsQuitButton = CreateQuitButton();
        settingsQuitButton.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(settingsQuitButton, 1);
        buttons.Children.Add(settingsQuitButton);
    }

    private void BuildStyleView()
    {
        var root = new Grid { RowSpacing = 8 };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        StyleView.Child = root;

        var content = new StackPanel { Spacing = 10 };
        root.Children.Add(content);

        StyleTitleText = new TextBlock
        {
            Text = "Style",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        AttachSectionTitleHomeNavigation(StyleTitleText);
        content.Children.Add(StyleTitleText);
        content.Children.Add(CreateSectionDivider());

        StylePreviewTrackRoot = new Grid
        {
            Height = 6,
            Margin = new Thickness(0, 4, 0, 5)
        };
        StylePreviewTrackRoot.Children.Add(new Border
        {
            Background = Brush(0xFF, 0x30, 0x28, 0x3A),
            BorderBrush = Brush(0xFF, 0x5A, 0x4A, 0x74),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3)
        });
        StylePreviewFillBar = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(3)
        };
        StylePreviewTrackRoot.Children.Add(StylePreviewFillBar);
        StylePreviewSweepBar = new Border
        {
            Opacity = 0.3,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        StylePreviewTrackRoot.Children.Add(StylePreviewSweepBar);
        content.Children.Add(StylePreviewTrackRoot);

        GaugeThicknessComboBox = CreateStyleComboBox(
            ("Thin", "thin"),
            ("Default", StyleConfig.DefaultGaugeThickness),
            ("Thick", "thick"));
        GaugeThicknessLabelText = AddStyleOptionRow(content, "Gauge thickness", GaugeThicknessComboBox);

        GaugeColorButton = new Button
        {
            MinWidth = 112,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        GaugeColorButton.Click += (_, _) => ShowGaugeColorWindow();
        UpdateGaugeColorButton(_selectedGaugeColor);
        GaugeColorLabelText = AddStyleOptionRow(content, "Gauge color", GaugeColorButton);

        GaugeAnimationComboBox = CreateStyleComboBox(
            ("Smooth", StyleConfig.DefaultGaugeAnimation),
            ("Fast", "fast"),
            ("Off", "off"));
        GaugeAnimationLabelText = AddStyleOptionRow(content, "Animation", GaugeAnimationComboBox);

        GaugeThicknessComboBox.SelectionChanged += (_, _) => ApplyStylePreview();
        GaugeAnimationComboBox.SelectionChanged += (_, _) =>
        {
            _stylePreviewSweepPhase = 0;
            ApplyStylePreview();
        };

        var buttons = new Grid { ColumnSpacing = 6 };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);
        SaveStyleButton = CreateBackButton("Save");
        SaveStyleButton.HorizontalAlignment = HorizontalAlignment.Left;
        SaveStyleButton.Click += SaveStyleButton_Click;
        buttons.Children.Add(SaveStyleButton);
        var quitButton = CreateQuitButton();
        quitButton.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(quitButton, 1);
        buttons.Children.Add(quitButton);
    }

    private static ComboBox CreateStyleComboBox(params (string Label, string Value)[] options)
    {
        var comboBox = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 112
        };
        foreach (var option in options)
        {
            comboBox.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Value });
        }

        return comboBox;
    }

    private static TextBlock AddStyleOptionRow(StackPanel root, string label, FrameworkElement control)
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var labelText = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(labelText);
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        root.Children.Add(row);
        return labelText;
    }

    private void BuildCreditsView()
    {
        var grid = new Grid { RowSpacing = 10 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        CreditsView.Child = grid;

        CreditsTitleText = new TextBlock
        {
            Text = "Credits",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        AttachSectionTitleHomeNavigation(CreditsTitleText);
        grid.Children.Add(CreditsTitleText);
        var creditsDivider = CreateSectionDivider();
        Grid.SetRow(creditsDivider, 1);
        grid.Children.Add(creditsDivider);

        CreditsDetailText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush(0xFF, 0xED, 0xE7, 0xFF)
        };
        Grid.SetRow(CreditsDetailText, 2);
        grid.Children.Add(CreditsDetailText);

        var creditsQuitButton = CreateQuitButton();
        creditsQuitButton.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetRow(creditsQuitButton, 3);
        grid.Children.Add(creditsQuitButton);

    }

    private void BuildResetCreditDetailsView()
    {
        var grid = new Grid { RowSpacing = 10 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ResetCreditDetailsView.Child = grid;

        ResetCreditDetailsTitleText = new TextBlock
        {
            Text = "Credit details",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        AttachSectionTitleHomeNavigation(ResetCreditDetailsTitleText);
        grid.Children.Add(ResetCreditDetailsTitleText);
        var resetDetailsDivider = CreateSectionDivider();
        Grid.SetRow(resetDetailsDivider, 1);
        grid.Children.Add(resetDetailsDivider);

        ResetCreditSummaryText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush(0xFF, 0xED, 0xE7, 0xFF)
        };
        Grid.SetRow(ResetCreditSummaryText, 2);
        grid.Children.Add(ResetCreditSummaryText);

        ResetCreditDetailsText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            Foreground = Brush(0xFF, 0xED, 0xE7, 0xFF)
        };
        Grid.SetRow(ResetCreditDetailsText, 3);
        grid.Children.Add(ResetCreditDetailsText);

        var resetDetailsQuitButton = CreateQuitButton();
        resetDetailsQuitButton.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetRow(resetDetailsQuitButton, 4);
        grid.Children.Add(resetDetailsQuitButton);

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

    private Button CreateQuitButton()
    {
        var button = CreateBackButton("Quit");
        button.Click += QuitButton_Click;
        _quitButtons.Add(button);
        return button;
    }

    private static SolidColorBrush Brush(byte a, byte r, byte g, byte b) =>
        new(global::Windows.UI.Color.FromArgb(a, r, g, b));

    public void ShowHudView()
    {
        CloseGaugeColorWindow();
        SessionsView.Visibility = Visibility.Collapsed;
        CreditsView.Visibility = Visibility.Collapsed;
        StyleView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        ResetCreditDetailsView.Visibility = Visibility.Collapsed;
        HudView.Visibility = Visibility.Visible;
        ApplyLanguage();
        RootLayout.Focus(FocusState.Programmatic);
        UpdateState();
    }

    public void ShowCreditsView()
    {
        CloseGaugeColorWindow();
        HudView.Visibility = Visibility.Collapsed;
        SessionsView.Visibility = Visibility.Collapsed;
        StyleView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        ResetCreditDetailsView.Visibility = Visibility.Collapsed;
        CreditsView.Visibility = Visibility.Visible;
        ApplyLanguage();
        RootLayout.Focus(FocusState.Programmatic);
        UpdateCredits(_usageStore.Credits);
    }

    public void ShowSettingsView()
    {
        CloseGaugeColorWindow();
        RefreshIntervalSecondsTextBox.Text = _settingsStore.Codex.RefreshIntervalSeconds.ToString();
        ToggleHotkeyTextBox.Text = _settingsStore.Config.Hotkeys.ToggleWindow;
        ToggleSidebarHotkeyTextBox.Text = _settingsStore.Config.Hotkeys.ToggleSidebar;
        StartWithWindowsCheckBox.IsChecked = _settingsStore.Config.StartWithWindows;
        AutoShowWithCodexCheckBox.IsChecked = _settingsStore.Config.AutoShowWithCodex;
        CustomCodexUpdateCommandTextBox.Text = _settingsStore.Config.CodexUpdates.CustomCommand ?? string.Empty;
        SelectCodexInstallMethod(_settingsStore.Config.CodexUpdates.InstallMethod);
        ApplyCustomUpdateCommandVisibility();
        UpdateCodexVersionStatusText(_lastCodexVersionCheck);
        ApplyAutoShowShortcutState();
        SelectLanguage(_settingsStore.Config.Language);
        HudView.Visibility = Visibility.Collapsed;
        SessionsView.Visibility = Visibility.Collapsed;
        CreditsView.Visibility = Visibility.Collapsed;
        StyleView.Visibility = Visibility.Collapsed;
        ResetCreditDetailsView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Visible;
        ApplyLanguage();
        _modelUsages.Clear();
    }

    public void ShowStyleView()
    {
        SelectStyleOption(GaugeThicknessComboBox, _settingsStore.Config.Style.GaugeThickness);
        SelectStyleOption(GaugeAnimationComboBox, _settingsStore.Config.Style.GaugeAnimation);
        _selectedGaugeColor = ParseGaugeColor(_settingsStore.Config.Style.GaugeColor);
        _previewGaugeColor = _selectedGaugeColor;
        UpdateGaugeColorButton(_selectedGaugeColor);
        HudView.Visibility = Visibility.Collapsed;
        SessionsView.Visibility = Visibility.Collapsed;
        CreditsView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        ResetCreditDetailsView.Visibility = Visibility.Collapsed;
        StyleView.Visibility = Visibility.Visible;
        ApplyLanguage();
        ApplyStylePreview();
        RootLayout.Focus(FocusState.Programmatic);
    }

    public void ShowResetCreditDetailsView()
    {
        CloseGaugeColorWindow();
        HudView.Visibility = Visibility.Collapsed;
        SessionsView.Visibility = Visibility.Collapsed;
        CreditsView.Visibility = Visibility.Collapsed;
        StyleView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        ResetCreditDetailsView.Visibility = Visibility.Visible;
        ApplyLanguage();
        RootLayout.Focus(FocusState.Programmatic);
        UpdateResetCreditDetails(_usageStore.Snapshot?.RateLimitResetCredits);
    }

    public void ShowSessionsView()
    {
        CloseGaugeColorWindow();
        HudView.Visibility = Visibility.Collapsed;
        CreditsView.Visibility = Visibility.Collapsed;
        StyleView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        ResetCreditDetailsView.Visibility = Visibility.Collapsed;
        SessionsView.Visibility = Visibility.Visible;
        ApplyLanguage();
        RootLayout.Focus(FocusState.Programmatic);
        UpdateSessionUsageView(_usageStore.Snapshot?.Sessions);
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
        var animation = StyleConfig.NormalizeGaugeAnimation(_settingsStore.Config.Style.GaugeAnimation);
        var animationEnabled = animation != "off";
        var sweepStep = animation switch
        {
            "fast" => FastBarSweepStep * 1.6,
            "off" => 0,
            _ => _isFastServiceTier ? FastBarSweepStep : StandardBarSweepStep
        };
        var activeWindowEaseFactor = animation switch
        {
            "fast" => 0.42,
            "off" => 1,
            _ => _isFastServiceTier ? FastBarEaseFactor : StandardBarEaseFactor
        };
        var sessionEaseFactor = animation switch
        {
            "fast" => 0.42,
            "off" => 1,
            _ => StandardBarEaseFactor
        };

        _barSweepPhase = (_barSweepPhase + sweepStep) % 1.0;
        var activeWindowSweep = EaseSweep(_barSweepPhase);
        _currentBarValue = EaseBarValue(_currentBarValue, _targetCurrentBarValue, activeWindowEaseFactor);
        _weeklyBarValue = EaseBarValue(_weeklyBarValue, _targetWeeklyBarValue, activeWindowEaseFactor);
        ApplyActiveBarProgress(
            CurrentWindowTrackRoot,
            CurrentWindowFillBar,
            CurrentWindowSweepBar,
            _currentBarValue,
            _targetCurrentBarValue,
            activeWindowSweep,
            animationEnabled);
        ApplyActiveBarProgress(
            WeeklyWindowTrackRoot,
            WeeklyWindowFillBar,
            WeeklyWindowSweepBar,
            _weeklyBarValue,
            _targetWeeklyBarValue,
            activeWindowSweep,
            animationEnabled);

        foreach (var sessionBar in _sessionUsageBars)
        {
            sessionBar.CurrentValue = EaseBarValue(sessionBar.CurrentValue, sessionBar.TargetValue, sessionEaseFactor);
            _sessionBarValues[sessionBar.SessionId] = sessionBar.CurrentValue;
            ApplyActiveBarProgress(
                sessionBar.TrackRoot,
                sessionBar.FillBar,
                sessionBar.SweepBar,
                sessionBar.CurrentValue,
                sessionBar.TargetValue,
                activeWindowSweep,
                animationEnabled);
        }

        if (StyleView.Visibility == Visibility.Visible)
        {
            var previewAnimation = StyleConfig.NormalizeGaugeAnimation(
                ReadStyleOption(GaugeAnimationComboBox, StyleConfig.DefaultGaugeAnimation));
            var previewStep = previewAnimation switch
            {
                "fast" => FastBarSweepStep * 1.6,
                "off" => 0,
                _ => StandardBarSweepStep
            };
            _stylePreviewSweepPhase = (_stylePreviewSweepPhase + previewStep) % 1d;
            ApplyStylePreview();
        }
    }

    private static double EaseBarValue(double current, double target, double factor)
    {
        var delta = target - current;
        return Math.Abs(delta) < 0.1 ? target : current + (delta * factor);
    }

    private static double EaseSweep(double amount) => 1 - Math.Pow(1 - amount, 2);

    private static void ApplyActiveBarProgress(
        Grid trackRoot,
        Border fillBar,
        Border sweepBar,
        double value,
        double targetValue,
        double sweep,
        bool animationEnabled)
    {
        var width = Math.Max(0, trackRoot.ActualWidth);
        var safeValue = Math.Clamp(value, 0, 100);
        var safeTarget = Math.Clamp(targetValue, 0, 100);
        var safeSweep = Math.Clamp(sweep, 0, 1);

        fillBar.Width = width * (safeValue / 100d);
        sweepBar.Width = animationEnabled ? width * (safeTarget * safeSweep / 100d) : 0;
        sweepBar.Opacity = animationEnabled ? 0.22 + (0.28 * safeSweep) : 0;
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

    private void SessionsButton_Click(object sender, RoutedEventArgs args)
    {
        if (SessionsView.Visibility == Visibility.Visible)
        {
            ShowHudView();
            return;
        }

        ShowSessionsView();
    }

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

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidVisibilityChange && !sender.IsVisible)
        {
            CloseGaugeColorWindow();
        }
    }

    private void CloseGaugeColorWindow(bool discardPendingColor = true)
    {
        if (discardPendingColor)
        {
            _previewGaugeColor = _selectedGaugeColor;
        }

        var popup = _gaugeColorWindow;
        if (popup is null)
        {
            return;
        }

        _gaugeColorWindow = null;
        popup.Close();
    }

    private void StyleButton_Click(object sender, RoutedEventArgs args)
    {
        if (StyleView.Visibility == Visibility.Visible)
        {
            ShowHudView();
            return;
        }

        ShowStyleView();
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
            config.CodexUpdates.InstallMethod = ReadSelectedCodexInstallMethod();
            config.CodexUpdates.CustomCommand = CustomCodexUpdateCommandTextBox.Text;
        });
        StartupShortcutService.Apply(_settingsStore.Config.StartWithWindows);
        if (_lastCodexVersionCheck?.Status is CodexVersionStatus.Current
            or CodexVersionStatus.CompatibleWithoutLatestVersion
            or CodexVersionStatus.RecommendedUpdate)
        {
            _usageStore.StartBackgroundRefresh();
        }
        ShowHudView();
    }

    private void SaveStyleButton_Click(object sender, RoutedEventArgs args)
    {
        CloseGaugeColorWindow(discardPendingColor: false);
        _selectedGaugeColor = _previewGaugeColor;
        _settingsStore.Update(config =>
        {
            config.Style.GaugeThickness = ReadStyleOption(
                GaugeThicknessComboBox,
                StyleConfig.DefaultGaugeThickness);
            config.Style.GaugeColor = FormatGaugeColor(_previewGaugeColor);
            config.Style.GaugeAnimation = ReadStyleOption(
                GaugeAnimationComboBox,
                StyleConfig.DefaultGaugeAnimation);
        });
        ShowHudView();
    }

    private async void CheckCodexVersionButton_Click(object sender, RoutedEventArgs args)
    {
        await CheckCodexVersionAsync(forceLatestVersionRefresh: true, showCurrentResult: true);
    }

    private async Task CheckCodexVersionAsync(bool forceLatestVersionRefresh, bool showCurrentResult = false)
    {
        if (_codexVersionDialogOpen)
        {
            return;
        }

        CheckCodexVersionButton.IsEnabled = false;
        UpdateCodexVersionStatusText(null, checking: true);
        var cachedVersion = _settingsStore.Config.CodexUpdates.LatestVersion;
        var cachedCheckedAt = _settingsStore.Config.CodexUpdates.LastCheckedAt;
        try
        {
            var result = await _codexCliUpdateService.CheckAsync(
                _settingsStore.Config.CodexUpdates,
                forceLatestVersionRefresh,
                _lifetimeCancellation.Token);
            _lastCodexVersionCheck = result;
            UpdateCodexVersionStatusText(result);

            if (result.Status is CodexVersionStatus.Current
                or CodexVersionStatus.CompatibleWithoutLatestVersion
                or CodexVersionStatus.RecommendedUpdate)
            {
                _usageStore.StartBackgroundRefresh();
            }

            if (!string.Equals(cachedVersion, _settingsStore.Config.CodexUpdates.LatestVersion, StringComparison.Ordinal)
                || cachedCheckedAt != _settingsStore.Config.CodexUpdates.LastCheckedAt)
            {
                _settingsStore.Save();
            }

            if (result.Status is CodexVersionStatus.Missing
                or CodexVersionStatus.RequiredUpdate
                or CodexVersionStatus.RecommendedUpdate)
            {
                await RunCodexUpdateAsync(result);
                return;
            }

            if (showCurrentResult)
            {
                var message = result.Status == CodexVersionStatus.Current
                    ? Text("Codex CLI is up to date.", "Codex CLI가 최신 버전이에요.")
                    : Text(
                        "The installed Codex CLI is compatible. The latest version could not be checked.",
                        "설치된 Codex CLI는 호환돼요. 최신 버전은 확인하지 못했어요.");
                await ShowMessageAsync(Text("Codex CLI", "Codex CLI"), message);
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            UpdateCodexVersionStatusText(null, checkError: error.Message);
            if (showCurrentResult)
            {
                await ShowMessageAsync(
                    Text("Codex version check failed", "Codex 버전 확인 실패"),
                    error.Message);
            }
        }
        finally
        {
            CheckCodexVersionButton.IsEnabled = true;
        }
    }

    private async Task ShowRequiredCodexUpdateDialogAsync(CodexVersionCheckResult result)
    {
        _codexVersionDialogOpen = true;
        try
        {
            var installed = result.InstalledVersion?.ToString() ?? Text("not installed", "설치되지 않음");
            var message = Text(
                $"Installed: {installed}\nRequired by WindexBar: {result.RequiredVersion}\n\nUpdate Codex CLI to use WindexBar.",
                $"설치 버전: {installed}\nWindexBar 요구 버전: {result.RequiredVersion}\n\nWindexBar를 사용하려면 Codex CLI를 업데이트해야 해요.");
            var content = CreateUpdateDialogContent(message, result, out var methodSelector, out var customCommand);
            var dialog = CreateDialog(Text("Codex CLI update required", "Codex CLI 업데이트 필요"), content);
            dialog.PrimaryButtonText = Text("Update now", "지금 업데이트");
            dialog.CloseButtonText = Text("Exit WindexBar", "WindexBar 종료");
            var choice = await dialog.ShowAsync();
            if (choice == ContentDialogResult.Primary)
            {
                if (!SaveUpdateDialogSettings(methodSelector, customCommand))
                {
                    ShowSettingsView();
                    return;
                }
                await RunCodexUpdateAsync(result);
            }
            else
            {
                App.Current.Shutdown();
            }
        }
        finally
        {
            _codexVersionDialogOpen = false;
        }
    }

    private async Task ShowRecommendedCodexUpdateDialogAsync(CodexVersionCheckResult result)
    {
        _codexVersionDialogOpen = true;
        try
        {
            var message = Text(
                $"Installed: {result.InstalledVersion}\nLatest: {result.LatestVersion}\n\nYour current version is compatible with WindexBar.",
                $"설치 버전: {result.InstalledVersion}\n최신 버전: {result.LatestVersion}\n\n현재 버전으로도 WindexBar를 사용할 수 있어요.");
            var content = CreateUpdateDialogContent(message, result, out var methodSelector, out var customCommand);
            var dialog = CreateDialog(Text("Codex CLI update available", "Codex CLI 업데이트 가능"), content);
            dialog.PrimaryButtonText = Text("Update now", "지금 업데이트");
            dialog.CloseButtonText = Text("Dismiss", "닫기");
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                if (!SaveUpdateDialogSettings(methodSelector, customCommand))
                {
                    ShowSettingsView();
                    return;
                }
                await RunCodexUpdateAsync(result);
            }
        }
        finally
        {
            _codexVersionDialogOpen = false;
        }
    }

    private async Task RunCodexUpdateAsync(CodexVersionCheckResult check)
    {
        var target = check.LatestVersion ?? check.RequiredVersion;
        var progressContent = new StackPanel { Spacing = 12, MinWidth = 220 };
        progressContent.Children.Add(new TextBlock
        {
            Text = Text(
                $"Updating Codex CLI to {target}...",
                $"Codex CLI를 {target}(으)로 업데이트하는 중이에요..."),
            TextWrapping = TextWrapping.Wrap
        });
        progressContent.Children.Add(new ProgressBar
        {
            IsIndeterminate = true,
            HorizontalAlignment = HorizontalAlignment.Stretch
        });
        progressContent.Children.Add(new TextBlock
        {
            Text = Text(
                "WindexBar will verify the installed version when the update finishes.",
                "업데이트가 끝나면 WindexBar가 설치 버전을 다시 확인해요."),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72,
            FontSize = 11
        });
        var progress = CreateDialog(
            Text("Updating Codex CLI", "Codex CLI 업데이트 중"),
            progressContent);
        var progressOperation = progress.ShowAsync();
        await Task.Yield();
        CodexUpdateResult? result = null;
        Exception? updateError = null;
        try
        {
            result = await _codexCliUpdateService.UpdateAsync(
                _settingsStore.Config.CodexUpdates.InstallMethod,
                _settingsStore.Config.CodexUpdates.CustomCommand,
                target,
                _lifetimeCancellation.Token);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            updateError = error;
        }
        finally
        {
            progress.Hide();
            await progressOperation;
        }

        if (updateError is not null)
        {
            await ShowMessageAsync(
                Text("Codex CLI update failed", "Codex CLI 업데이트 실패"),
                updateError.Message);
            return;
        }

        if (result!.IsSuccess)
        {
            _lastCodexVersionCheck = new CodexVersionCheckResult(
                CodexVersionStatus.Current,
                result.InstalledVersion,
                check.RequiredVersion,
                check.LatestVersion,
                check.DetectedInstallMethod,
                check.UsedCachedLatestVersion,
                null);
            UpdateCodexVersionStatusText(_lastCodexVersionCheck);
            await ShowMessageAsync(
                Text("Codex CLI updated", "Codex CLI 업데이트 완료"),
                Text(
                    $"Codex CLI {result.InstalledVersion} is ready.",
                    $"Codex CLI {result.InstalledVersion}을 사용할 수 있어요."));
            _usageStore.StartBackgroundRefresh();
            return;
        }

        var details = string.IsNullOrWhiteSpace(result.Command.CombinedOutput)
            ? result.ErrorDescription ?? Text("Unknown error", "알 수 없는 오류")
            : $"{result.ErrorDescription}\n\n{result.Command.CombinedOutput}";
        await ShowMessageAsync(
            Text("Codex CLI update failed", "Codex CLI 업데이트 실패"),
            details);
    }

    private StackPanel CreateUpdateDialogContent(
        string message,
        CodexVersionCheckResult result,
        out ComboBox methodSelector,
        out TextBox customCommand)
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        var commandPreview = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72,
            FontSize = 11,
            MinHeight = 18
        };
        panel.Children.Add(commandPreview);
        panel.Children.Add(new TextBlock
        {
            Text = Text("Update method", "업데이트 방식"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        var selector = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        AddInstallMethodItem(selector, $"Auto-detect ({result.DetectedInstallMethod})", CodexInstallMethodNames.Auto);
        AddInstallMethodItem(selector, "PowerShell", CodexInstallMethodNames.PowerShell);
        AddInstallMethodItem(selector, "npm", CodexInstallMethodNames.Npm);
        AddInstallMethodItem(selector, "Bun", CodexInstallMethodNames.Bun);
        AddInstallMethodItem(selector, "Homebrew", CodexInstallMethodNames.Homebrew);
        AddInstallMethodItem(selector, "WinGet", CodexInstallMethodNames.WinGet);
        AddInstallMethodItem(selector, "Custom", CodexInstallMethodNames.Custom);
        SelectInstallMethod(selector, _settingsStore.Config.CodexUpdates.InstallMethod);
        panel.Children.Add(selector);
        var customBox = new TextBox
        {
            Text = _settingsStore.Config.CodexUpdates.CustomCommand ?? string.Empty,
            PlaceholderText = Text(
                "Enter a Codex CLI update command.",
                "Codex CLI 업데이트 명령을 입력하세요."),
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(customBox);
        void UpdateCustomVisibility()
        {
            var selectedMethod = ReadInstallMethod(selector);
            customBox.Visibility = selectedMethod == CodexInstallMethodNames.Custom
                ? Visibility.Visible
                : Visibility.Collapsed;
            var effectiveMethod = selectedMethod == CodexInstallMethodNames.Auto
                ? result.DetectedInstallMethod
                : selectedMethod;
            try
            {
                var target = result.LatestVersion ?? result.RequiredVersion;
                var command = CodexCliUpdateService.BuildUpdateCommand(effectiveMethod, customBox.Text, target);
                commandPreview.Text = Text("Command: ", "명령: ") + command.Arguments[^1];
            }
            catch (InvalidOperationException)
            {
                commandPreview.Text = Text("Enter a custom update command.", "사용자 지정 업데이트 명령을 입력하세요.");
            }
        }

        selector.SelectionChanged += (_, _) => UpdateCustomVisibility();
        customBox.TextChanged += (_, _) => UpdateCustomVisibility();
        UpdateCustomVisibility();
        methodSelector = selector;
        customCommand = customBox;
        return panel;
    }

    private bool SaveUpdateDialogSettings(ComboBox methodSelector, TextBox customCommand)
    {
        var method = ReadInstallMethod(methodSelector);
        if (method == CodexInstallMethodNames.Custom && string.IsNullOrWhiteSpace(customCommand.Text))
        {
            return false;
        }

        _settingsStore.Update(config =>
        {
            config.CodexUpdates.InstallMethod = method;
            config.CodexUpdates.CustomCommand = customCommand.Text;
        });
        return true;
    }

    private static void AddInstallMethodItem(ComboBox comboBox, string content, string value) =>
        comboBox.Items.Add(new ComboBoxItem { Content = content, Tag = value });

    private static string ReadInstallMethod(ComboBox comboBox) =>
        comboBox.SelectedItem is ComboBoxItem { Tag: string method }
            ? CodexInstallMethodNames.Normalize(method)
            : CodexInstallMethodNames.Auto;

    private static void SelectInstallMethod(ComboBox comboBox, string? installMethod)
    {
        var normalized = CodexInstallMethodNames.Normalize(installMethod);
        comboBox.SelectedItem = comboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag is string value
                && string.Equals(value, normalized, StringComparison.OrdinalIgnoreCase));
        comboBox.SelectedIndex = comboBox.SelectedItem is null ? 0 : comboBox.SelectedIndex;
    }

    private ContentDialog CreateDialog(string title, string message) =>
        CreateDialog(title, new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });

    private ContentDialog CreateDialog(string title, UIElement content) => new()
    {
        Title = title,
        Content = content,
        XamlRoot = RootLayout.XamlRoot
    };

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = CreateDialog(title, message);
        dialog.CloseButtonText = Text("Close", "닫기");
        await dialog.ShowAsync();
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

    private static string ReadStyleOption(ComboBox comboBox, string fallback) =>
        comboBox.SelectedItem is ComboBoxItem { Tag: string value } ? value : fallback;

    private static void SelectStyleOption(ComboBox comboBox, string? value)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string candidate && string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private StyleConfig ReadStyleSelection() => new StyleConfig
    {
        GaugeThickness = ReadStyleOption(GaugeThicknessComboBox, StyleConfig.DefaultGaugeThickness),
        GaugeColor = FormatGaugeColor(_previewGaugeColor),
        GaugeAnimation = ReadStyleOption(GaugeAnimationComboBox, StyleConfig.DefaultGaugeAnimation)
    }.Normalized();

    private static global::Windows.UI.Color ParseGaugeColor(string? value)
    {
        var normalized = StyleConfig.NormalizeGaugeColor(value);
        return global::Windows.UI.Color.FromArgb(
            0xFF,
            Convert.ToByte(normalized.Substring(1, 2), 16),
            Convert.ToByte(normalized.Substring(3, 2), 16),
            Convert.ToByte(normalized.Substring(5, 2), 16));
    }

    private static string FormatGaugeColor(global::Windows.UI.Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private void ShowGaugeColorWindow()
    {
        if (_gaugeColorWindow is not null)
        {
            _gaugeColorWindow.Activate();
            return;
        }

        var popup = new Window { Title = Text("Gauge color", "\uAC8C\uC774\uC9C0 \uC0C9\uC0C1") };
        _gaugeColorWindow = popup;
        var brightnessValue = Math.Max(_previewGaugeColor.R, Math.Max(_previewGaugeColor.G, _previewGaugeColor.B)) / 255d;
        var baseColor = NormalizeGaugeColorBrightness(_previewGaugeColor);
        var palette = new[]
        {
            baseColor,
            global::Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x5F, 0x57),
            global::Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xA3, 0x3E),
            global::Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xD1, 0x66),
            global::Windows.UI.Color.FromArgb(0xFF, 0x43, 0xC5, 0x8A),
            global::Windows.UI.Color.FromArgb(0xFF, 0x3B, 0xC7, 0xC4),
            global::Windows.UI.Color.FromArgb(0xFF, 0x4F, 0x9D, 0xFF),
            global::Windows.UI.Color.FromArgb(0xFF, 0x66, 0x70, 0xD9),
            global::Windows.UI.Color.FromArgb(0xFF, 0x8D, 0x78, 0xD6),
            global::Windows.UI.Color.FromArgb(0xFF, 0xC6, 0x5F, 0xD4),
            global::Windows.UI.Color.FromArgb(0xFF, 0xD7, 0x56, 0x7D),
            global::Windows.UI.Color.FromArgb(0xFF, 0x8B, 0x8B, 0x94)
        };
        var swatchButtons = new List<Button>();
        var paletteGrid = new Grid
        {
            Width = 210,
            RowSpacing = 8,
            ColumnSpacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        for (var column = 0; column < 6; column++)
        {
            paletteGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        }
        paletteGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        paletteGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });

        void UpdateSwatchSelection()
        {
            for (var index = 0; index < swatchButtons.Count; index++)
            {
                var selected = palette[index].Equals(baseColor);
                swatchButtons[index].BorderBrush = selected
                    ? Brush(0xFF, 0xFF, 0xFF, 0xFF)
                    : Brush(0x66, 0xFF, 0xFF, 0xFF);
                swatchButtons[index].BorderThickness = new Thickness(selected ? 3 : 1);
            }
        }

        for (var index = 0; index < palette.Length; index++)
        {
            var paletteColor = palette[index];
            var swatch = new Button
            {
                Width = 30,
                Height = 30,
                MinWidth = 30,
                MinHeight = 30,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(paletteColor),
                CornerRadius = new CornerRadius(6)
            };
            swatch.Click += (_, _) =>
            {
                baseColor = paletteColor;
                _previewGaugeColor = ApplyGaugeBrightness(baseColor, brightnessValue);
                UpdateSwatchSelection();
                UpdateGaugeColorButton(_previewGaugeColor);
                ApplyStylePreview();
            };
            Grid.SetColumn(swatch, index % 6);
            Grid.SetRow(swatch, index / 6);
            paletteGrid.Children.Add(swatch);
            swatchButtons.Add(swatch);
        }
        UpdateSwatchSelection();

        var brightness = new Slider
        {
            Width = 210,
            Height = 32,
            Minimum = 0,
            Maximum = 100,
            StepFrequency = 1
        };
        brightness.Value = brightnessValue * 100d;
        brightness.ValueChanged += (_, args) =>
        {
            brightnessValue = args.NewValue / 100d;
            _previewGaugeColor = ApplyGaugeBrightness(baseColor, brightnessValue);
            UpdateGaugeColorButton(_previewGaugeColor);
            ApplyStylePreview();
        };

        var panel = new StackPanel { Width = 210, Spacing = 8 };
        panel.Children.Add(paletteGrid);
        panel.Children.Add(new TextBlock
        {
            Text = Text("Brightness", "\uBC1D\uAE30"),
            FontSize = 11,
            Opacity = 0.75
        });
        panel.Children.Add(brightness);
        popup.Content = new Border
        {
            Padding = new Thickness(10),
            Background = Brush(0xFF, 0x1F, 0x1C, 0x24),
            BorderBrush = Brush(0x99, 0x7D, 0x62, 0xC7),
            BorderThickness = new Thickness(1),
            Child = panel
        };
        OwnedWindowBehavior.Attach(popup, this);

        popup.AppWindow.IsShownInSwitchers = false;
        if (popup.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
        }

        var popupScale = RootLayout.XamlRoot?.RasterizationScale ?? 1d;
        var popupWidth = (int)Math.Ceiling(240 * popupScale);
        var popupHeight = (int)Math.Ceiling(154 * popupScale);
        popup.AppWindow.ResizeClient(new SizeInt32(popupWidth, popupHeight));
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        var workArea = displayArea.WorkArea;
        var popupX = AppWindow.Position.X + AppWindow.Size.Width + 8;
        if (popupX + popupWidth > workArea.X + workArea.Width)
        {
            popupX = AppWindow.Position.X - popupWidth - 8;
        }

        popupX = Math.Clamp(popupX, workArea.X, workArea.X + workArea.Width - popupWidth);
        var popupY = Math.Clamp(
            AppWindow.Position.Y + 88,
            workArea.Y,
            workArea.Y + workArea.Height - popupHeight);
        popup.AppWindow.Move(new PointInt32(popupX, popupY));
        var hasActivated = false;
        popup.Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated && hasActivated)
            {
                CloseGaugeColorWindow(discardPendingColor: false);
                return;
            }

            hasActivated = true;
        };
        popup.Closed += (_, _) =>
        {
            _gaugeColorWindow = null;
            ApplyStylePreview();
        };
        popup.Activate();
    }

    private static global::Windows.UI.Color NormalizeGaugeColorBrightness(global::Windows.UI.Color color)
    {
        var max = Math.Max(color.R, Math.Max(color.G, color.B));
        if (max == 0)
        {
            return global::Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
        }

        var scale = 255d / max;
        return global::Windows.UI.Color.FromArgb(
            0xFF,
            (byte)Math.Clamp(Math.Round(color.R * scale), 0, 255),
            (byte)Math.Clamp(Math.Round(color.G * scale), 0, 255),
            (byte)Math.Clamp(Math.Round(color.B * scale), 0, 255));
    }

    private static global::Windows.UI.Color ApplyGaugeBrightness(global::Windows.UI.Color color, double brightness)
    {
        var value = Math.Clamp(brightness, 0, 1);
        return global::Windows.UI.Color.FromArgb(
            0xFF,
            (byte)Math.Clamp(Math.Round(color.R * value), 0, 255),
            (byte)Math.Clamp(Math.Round(color.G * value), 0, 255),
            (byte)Math.Clamp(Math.Round(color.B * value), 0, 255));
    }

    private void UpdateGaugeColorButton(global::Windows.UI.Color color)
    {
        GaugeColorButton.Content = Text("Choose", "\uC120\uD0DD");
        GaugeColorButton.Background = new SolidColorBrush(color);
        var luminance = (0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B);
        GaugeColorButton.Foreground = luminance >= 150
            ? Brush(0xFF, 0x20, 0x20, 0x20)
            : Brush(0xFF, 0xFF, 0xFF, 0xFF);
    }

    private static global::Windows.UI.Color LightenGaugeColor(global::Windows.UI.Color color, double amount)
    {
        byte Blend(byte channel) => (byte)Math.Clamp(
            Math.Round(channel + ((255 - channel) * amount)),
            byte.MinValue,
            byte.MaxValue);
        return global::Windows.UI.Color.FromArgb(0xFF, Blend(color.R), Blend(color.G), Blend(color.B));
    }

    private void ApplyStylePreview()
    {
        if (StylePreviewTrackRoot is null
            || GaugeThicknessComboBox?.SelectedItem is null
            || GaugeAnimationComboBox?.SelectedItem is null)
        {
            return;
        }

        var style = ReadStyleSelection();
        ApplyGaugeAppearance(StylePreviewTrackRoot, StylePreviewFillBar, StylePreviewSweepBar, style);
        var animated = style.GaugeAnimation != "off";
        ApplyActiveBarProgress(
            StylePreviewTrackRoot,
            StylePreviewFillBar,
            StylePreviewSweepBar,
            68,
            68,
            EaseSweep(_stylePreviewSweepPhase),
            animated);
    }

    private void AddInstallMethodItem(string content, string value) =>
        CodexInstallMethodComboBox.Items.Add(new ComboBoxItem { Content = content, Tag = value });

    private string ReadSelectedCodexInstallMethod()
    {
        return CodexInstallMethodComboBox.SelectedItem is ComboBoxItem { Tag: string method }
            ? CodexInstallMethodNames.Normalize(method)
            : CodexInstallMethodNames.Auto;
    }

    private void SelectCodexInstallMethod(string? installMethod)
    {
        var normalized = CodexInstallMethodNames.Normalize(installMethod);
        foreach (var item in CodexInstallMethodComboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string value && string.Equals(value, normalized, StringComparison.OrdinalIgnoreCase))
            {
                CodexInstallMethodComboBox.SelectedItem = item;
                return;
            }
        }

        CodexInstallMethodComboBox.SelectedIndex = 0;
    }

    private void ApplyCustomUpdateCommandVisibility()
    {
        var visible = ReadSelectedCodexInstallMethod() == CodexInstallMethodNames.Custom
            ? Visibility.Visible
            : Visibility.Collapsed;
        CustomCodexUpdateCommandLabelText.Visibility = visible;
        CustomCodexUpdateCommandTextBox.Visibility = visible;
        UpdateCodexUpdateCommandPreview();
    }

    private void UpdateCodexUpdateCommandPreview()
    {
        var selectedMethod = ReadSelectedCodexInstallMethod();
        var effectiveMethod = selectedMethod == CodexInstallMethodNames.Auto
            ? _lastCodexVersionCheck?.DetectedInstallMethod ?? CodexInstallMethodNames.PowerShell
            : selectedMethod;
        var target = _lastCodexVersionCheck?.LatestVersion
            ?? _lastCodexVersionCheck?.RequiredVersion
            ?? CodexVersionPolicy.MinimumRequiredVersion;
        try
        {
            var command = CodexCliUpdateService.BuildUpdateCommand(
                effectiveMethod,
                CustomCodexUpdateCommandTextBox.Text,
                target);
            CodexUpdateCommandPreviewText.Text = Text("Command: ", "명령: ") + command.Arguments[^1];
        }
        catch (InvalidOperationException)
        {
            CodexUpdateCommandPreviewText.Text = Text(
                "Enter a custom update command.",
                "사용자 지정 업데이트 명령을 입력하세요.");
        }
    }

    private void UpdateCodexVersionStatusText(
        CodexVersionCheckResult? result,
        bool checking = false,
        string? checkError = null)
    {
        var current = checking
            ? Text("Checking...", "확인 중...")
            : checkError is not null
                ? Text("Check failed", "확인 실패")
                : result?.Status == CodexVersionStatus.Missing
                    ? Text("Not installed", "설치되지 않음")
                    : result?.InstalledVersion?.ToString()
                        ?? Text("Waiting for startup check", "시작 확인 대기 중");
        CurrentCodexVersionText.Text = $"{Text("Current Codex CLI", "현재 Codex CLI")}\n{current}";
        UpdateCodexUpdateCommandPreview();
    }

    private void ApplyLanguage()
    {
        Title = SettingsView.Visibility == Visibility.Visible
            ? Text("WindexBar Settings", "WindexBar \uC124\uC815")
            : StyleView.Visibility == Visibility.Visible
                ? Text("WindexBar Style", "WindexBar \uC2A4\uD0C0\uC77C")
                : "WindexBar";
        ApplyWindowSectionLabels();
        AccountLabelText.Text = Text("Account", "\uACC4\uC815");
        SetSideBarButtonText(HomeButton, "\u2302");
        SetSideBarButtonText(SessionsButton, "\u2637");
        SetSideBarButtonText(CreditsButton, "$");
        SetSideBarButtonText(StyleButton, "\u25C8");
        SetSideBarButtonText(SettingsButton, "\u2699");
        SetSideBarButtonText(ResetCreditDetailsButton, "\u21BB");
        foreach (var quitButton in _quitButtons)
        {
            quitButton.Content = Text("Quit", "\uC885\uB8CC");
        }
        CreditsTitleText.Text = Text("Credits", "\uD06C\uB808\uB527");
        ResetCreditDetailsTitleText.Text = Text("Reset credit details", "\uCD08\uAE30\uD654\uAD8C \uC0C1\uC138");
        StyleTitleText.Text = Text("Style", "\uC2A4\uD0C0\uC77C");
        GaugeThicknessLabelText.Text = Text("Gauge thickness", "\uAC8C\uC774\uC9C0 \uB450\uAED8");
        GaugeColorLabelText.Text = Text("Gauge color", "\uAC8C\uC774\uC9C0 \uC0C9\uC0C1");
        GaugeAnimationLabelText.Text = Text("Animation", "\uC560\uB2C8\uBA54\uC774\uC158");
        SettingsTitleText.Text = Text("Settings", "\uC124\uC815");
        RefreshIntervalLabelText.Text = Text("Refresh interval", "\uC0C8\uB85C\uACE0\uCE68 \uAC04\uACA9");
        SecondsLabelText.Text = Text("s", "\uCD08");
        LanguageLabelText.Text = Text("Language", "\uC5B8\uC5B4");
        ToggleHotkeyLabelText.Text = Text("Toggle shortcut", "\uD1A0\uAE00 \uB2E8\uCD95\uD0A4");
        ToggleSidebarHotkeyLabelText.Text = Text("Sidebar shortcut", "\uC0AC\uC774\uB4DC\uBC14 \uB2E8\uCD95\uD0A4");
        CodexInstallMethodLabelText.Text = Text("Codex install method", "Codex 설치 방식");
        CustomCodexUpdateCommandLabelText.Text = Text("Custom update command", "사용자 지정 업데이트 명령");
        CustomCodexUpdateCommandTextBox.PlaceholderText = Text(
            "Enter a Codex CLI update command.",
            "Codex CLI 업데이트 명령을 입력하세요.");
        CheckCodexVersionButton.Content = Text("Check now", "지금 확인");
        StartWithWindowsCheckBox.Content = Text("Start with Windows", "Windows \uC2DC\uC791 \uC2DC \uC2E4\uD589");
        AutoShowWithCodexCheckBox.Content = Text("Show only while using ChatGPT or Codex", "ChatGPT \uB610\uB294 Codex \uC0AC\uC6A9 \uC911\uC5D0\uB9CC \uD45C\uC2DC");
        SaveSettingsButton.Content = Text("Save", "\uC800\uC7A5");
        SaveStyleButton.Content = Text("Save", "\uC800\uC7A5");
        UpdateGaugeColorButton(_selectedGaugeColor);
        UpdateCodexVersionStatusText(_lastCodexVersionCheck);
        UpdateSessionSortToggleAppearance();
    }

    private string CurrentLanguage => WindexBarConfig.NormalizeLanguage(_settingsStore.Config.Language);

    private bool IsKorean => CurrentLanguage == "ko";

    private string Text(string english, string korean) => IsKorean ? korean : english;

    private string UnknownText => Text("unknown", "\uC54C \uC218 \uC5C6\uC74C");

    private void ApplyWindowSectionLabels()
    {
        CurrentWindowLabelText.Text = WithFastIndicator(Text("Current", "\uD604\uC7AC"));
        WeeklyWindowLabelText.Text = WithFastIndicator(Text("Weekly", "\uC8FC\uAC04"));
        SessionsLabelText.Text = Text("Sessions", "\uC138\uC158");
    }

    private string WithFastIndicator(string label) =>
        _isFastServiceTier ? $"{label} {FastIndicatorGlyph}" : label;

    private void ApplyProgressBarTheme()
    {
        var style = _settingsStore.Config.Style.Normalized();
        ApplyGaugeAppearance(CurrentWindowTrackRoot, CurrentWindowFillBar, CurrentWindowSweepBar, style);
        ApplyGaugeAppearance(WeeklyWindowTrackRoot, WeeklyWindowFillBar, WeeklyWindowSweepBar, style);
        foreach (var sessionBar in _sessionUsageBars)
        {
            ApplyGaugeAppearance(sessionBar.TrackRoot, sessionBar.FillBar, sessionBar.SweepBar, style);
        }
    }

    private static void ApplyGaugeAppearance(
        Grid trackRoot,
        Border fillBar,
        Border sweepBar,
        StyleConfig style)
    {
        var thickness = StyleConfig.NormalizeGaugeThickness(style.GaugeThickness) switch
        {
            "thin" => 4d,
            "thick" => 9d,
            _ => 6d
        };
        var radius = new CornerRadius(thickness / 2d);
        trackRoot.Height = thickness;
        foreach (var border in trackRoot.Children.OfType<Border>())
        {
            border.CornerRadius = radius;
        }

        var color = ParseGaugeColor(style.GaugeColor);
        fillBar.Background = new SolidColorBrush(color);
        sweepBar.Background = new SolidColorBrush(LightenGaugeColor(color, 0.45));
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
        UpdateSessionUsageView(snapshot?.Sessions);
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
            " ultra reasoning effort",
            " max reasoning effort",
            " ultra reasoning",
            " max reasoning",
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
            " ultra",
            " max",
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
            var percent = TokenContextPercent(tokenUsage);
            var percentText = percent is null ? string.Empty : $" ({percent.Value:0.#}%)";
            values.Add($"{Text("Context", "\uCEE8\uD14D\uC2A4\uD2B8")}: {TokenCountFormatter.Format(current.TotalTokens, CurrentLanguage)} / {TokenCountFormatter.Format(contextWindow, CurrentLanguage)}{percentText}");
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

    private void UpdateSessionUsageView(IReadOnlyList<CodexSessionUsageSnapshot>? sessions)
    {
        foreach (var sessionBar in _sessionUsageBars)
        {
            _sessionBarValues[sessionBar.SessionId] = sessionBar.CurrentValue;
        }

        _sessionUsageBars.Clear();
        SessionUsagePanel.Children.Clear();
        if (sessions is null || sessions.Count == 0)
        {
            SessionUsagePanel.Children.Add(new TextBlock
            {
                Text = Text("No session token usage", "\uC138\uC158 \uD1A0\uD070 \uC0AC\uC6A9\uB7C9 \uC5C6\uC74C"),
                Foreground = Brush(0xFF, 0xB9, 0xA7, 0xE8),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        var projectGroups = sessions
            .GroupBy(session => SessionGroupKey(session.ProjectPath), StringComparer.OrdinalIgnoreCase)
            .Select(projectGroup =>
            {
                var projectSessions = projectGroup.OrderByDescending(item => item.UpdatedAt).ToArray();
                var projectPath = projectSessions[0].ProjectPath;
                var isNonProject = string.IsNullOrWhiteSpace(projectPath) || IsDefaultSessionPath(projectPath);
                return new SessionProjectGroupView(
                    projectGroup.Key,
                    SessionProjectDisplayName(projectPath, projectSessions.Length),
                    isNonProject,
                    projectSessions);
            })
            .ToArray();
        var orderedProjectGroups = projectGroups
            .OrderBy(group => _projectSessionsFirst ? (group.IsNonProject ? 1 : 0) : (group.IsNonProject ? 0 : 1))
            .ThenBy(group => group.ProjectName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        foreach (var projectGroup in orderedProjectGroups)
        {
            var groupKey = projectGroup.Key;
            var projectSessions = projectGroup.Sessions;
            var projectName = projectGroup.ProjectName;
            var isCollapsed = _collapsedSessionGroups.TryGetValue(groupKey, out var collapsed)
                ? collapsed
                : true;

            var projectHeaderContent = new Grid();
            projectHeaderContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            projectHeaderContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            projectHeaderContent.Children.Add(new TextBlock
            {
                Text = $"{projectName} ({projectSessions.Length})",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var chevron = new TextBlock
            {
                Text = isCollapsed ? "\u25B8" : "\u25BE",
                FontSize = 14,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brush(0xFF, 0xB9, 0xA7, 0xE8)
            };
            Grid.SetColumn(chevron, 1);
            projectHeaderContent.Children.Add(chevron);

            var projectHeader = new Border
            {
                Padding = new Thickness(8, 6, 8, 6),
                Background = Brush(0x66, 0x35, 0x2E, 0x40),
                BorderBrush = Brush(0x55, 0x7D, 0x62, 0xC7),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Child = projectHeaderContent
            };

            var sessionCards = new StackPanel
            {
                Spacing = 7,
                Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible
            };
            projectHeader.PointerPressed += (_, args) =>
            {
                var nextCollapsed = sessionCards.Visibility == Visibility.Visible;
                sessionCards.Visibility = nextCollapsed ? Visibility.Collapsed : Visibility.Visible;
                chevron.Text = nextCollapsed ? "\u25B8" : "\u25BE";
                _collapsedSessionGroups[groupKey] = nextCollapsed;
                args.Handled = true;
            };

            var projectSection = new StackPanel { Spacing = 6 };
            projectSection.Children.Add(projectHeader);
            projectSection.Children.Add(sessionCards);
            SessionUsagePanel.Children.Add(projectSection);

            foreach (var session in projectSessions)
            {
                var cardContent = new StackPanel { Spacing = 3 };
                cardContent.Children.Add(new TextBlock
                {
                    Text = $"[{SessionDisplayName(session)}]",
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextWrapping = TextWrapping.WrapWholeWords
                });
                cardContent.Children.Add(CreateSessionContextHeader(session.TokenUsage));
                cardContent.Children.Add(CreateSessionContextBar(session.SessionId, session.TokenUsage));
                cardContent.Children.Add(new TextBlock
                {
                    Text = FormatSessionTokenUsageDetails(session.TokenUsage),
                    FontSize = 11,
                    Foreground = Brush(0xFF, 0xED, 0xE7, 0xFF),
                    TextWrapping = TextWrapping.Wrap
                });

                sessionCards.Children.Add(new Border
                {
                    Padding = new Thickness(8, 6, 8, 6),
                    Background = Brush(0x55, 0x45, 0x3A, 0x56),
                    BorderBrush = Brush(0x66, 0x7D, 0x62, 0xC7),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Child = cardContent
                });
            }
        }
    }

    private Grid CreateSessionContextHeader(TokenUsageSnapshot tokenUsage)
    {
        var header = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = Text("Context", "\uCEE8\uD14D\uC2A4\uD2B8"),
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        var percent = TokenContextPercent(tokenUsage);
        var percentText = new TextBlock
        {
            Text = percent is null ? UnknownText : $"{percent.Value:0.#}%",
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        Grid.SetColumn(percentText, 1);
        header.Children.Add(percentText);
        return header;
    }

    private Grid CreateSessionContextBar(string sessionId, TokenUsageSnapshot tokenUsage)
    {
        var targetValue = TokenContextPercent(tokenUsage) ?? 0;
        var currentValue = _sessionBarValues.TryGetValue(sessionId, out var previousValue) ? previousValue : 0;
        var track = new Grid { Height = 6, Margin = new Thickness(0, 1, 0, 1) };
        track.Children.Add(new Border
        {
            Background = Brush(0xFF, 0x30, 0x28, 0x3A),
            BorderBrush = Brush(0xFF, 0x5A, 0x4A, 0x74),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3)
        });

        var fill = new Border
        {
            Background = Brush(0xFF, 0x8D, 0x78, 0xD6),
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(3)
        };
        track.Children.Add(fill);

        var sweep = new Border
        {
            Background = Brush(0xFF, 0xC8, 0xB9, 0xFF),
            Opacity = 0.3,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        track.Children.Add(sweep);
        ApplyGaugeAppearance(track, fill, sweep, _settingsStore.Config.Style.Normalized());
        _sessionUsageBars.Add(new SessionUsageBarView(sessionId, track, fill, sweep, currentValue, targetValue));
        return track;
    }

    private string FormatSessionTokenUsageDetails(TokenUsageSnapshot tokenUsage)
    {
        var values = new List<string>();
        var current = tokenUsage.Last ?? tokenUsage.Total;
        if (current is not null && tokenUsage.ModelContextWindow is { } contextWindow)
        {
            values.Add($"{TokenCountFormatter.Format(current.TotalTokens, CurrentLanguage)} / {TokenCountFormatter.Format(contextWindow, CurrentLanguage)}");
        }
        else if (current is not null)
        {
            values.Add(TokenCountFormatter.Format(current.TotalTokens, CurrentLanguage));
        }

        if (tokenUsage.Total is not null)
        {
            values.Add($"{Text("Session total", "\uC138\uC158 \uD569\uACC4")}: {TokenCountFormatter.Format(tokenUsage.Total.TotalTokens, CurrentLanguage)}");
        }

        return values.Count == 0 ? UnknownText : string.Join(Environment.NewLine, values);
    }

    private string SessionDisplayName(CodexSessionUsageSnapshot session)
    {
        if (!string.IsNullOrWhiteSpace(session.SessionName))
        {
            return session.SessionName;
        }

        var shortId = session.SessionId.Length <= 8 ? session.SessionId : session.SessionId[..8];
        return $"{Text("Session", "\uC138\uC158")} {shortId}";
    }

    private string ProjectDisplayName(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return Text("General session", "\uC77C\uBC18 \uC138\uC158");
        }

        var normalized = NormalizeProjectPath(projectPath);
        var name = Path.GetFileName(normalized);
        return string.IsNullOrWhiteSpace(name) ? normalized : name;
    }

    private string SessionProjectDisplayName(string? projectPath, int sessionCount)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return Text(sessionCount == 1 ? "General session" : "General sessions", "\uC77C\uBC18 \uC138\uC158");
        }

        return IsDefaultSessionPath(projectPath) ? "default-session" : ProjectDisplayName(projectPath);
    }

    private static string NormalizeProjectPath(string? projectPath) =>
        string.IsNullOrWhiteSpace(projectPath)
            ? string.Empty
            : projectPath.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string SessionGroupKey(string? projectPath) =>
        IsDefaultSessionPath(projectPath) ? "default-session" : NormalizeProjectPath(projectPath);

    private static bool IsDefaultSessionPath(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return false;
        }

        var normalized = NormalizeProjectPath(projectPath);
        var folderName = Path.GetFileName(normalized);
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return false;
        }

        var datePath = Path.GetDirectoryName(normalized);
        var codexPath = string.IsNullOrWhiteSpace(datePath) ? null : Path.GetDirectoryName(datePath);
        var documentsPath = string.IsNullOrWhiteSpace(codexPath) ? null : Path.GetDirectoryName(codexPath);
        var dateFolder = string.IsNullOrWhiteSpace(datePath) ? null : Path.GetFileName(datePath);
        return string.Equals(Path.GetFileName(codexPath), "Codex", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(documentsPath), "Documents", StringComparison.OrdinalIgnoreCase)
            && DateTime.TryParseExact(
                dateFolder,
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out _);
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

    private sealed record SessionProjectGroupView(
        string Key,
        string ProjectName,
        bool IsNonProject,
        CodexSessionUsageSnapshot[] Sessions);

    private sealed class SessionUsageBarView(
        string sessionId,
        Grid trackRoot,
        Border fillBar,
        Border sweepBar,
        double currentValue,
        double targetValue)
    {
        public string SessionId { get; } = sessionId;
        public Grid TrackRoot { get; } = trackRoot;
        public Border FillBar { get; } = fillBar;
        public Border SweepBar { get; } = sweepBar;
        public double CurrentValue { get; set; } = currentValue;
        public double TargetValue { get; } = targetValue;
    }
}
