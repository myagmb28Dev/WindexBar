using WindexBar.Core.Config;
using WindexBar.Core.Formatting;
using WindexBar.Core.Models;
using WindexBar.Core.Presentation;
using WindexBar.Core.Refresh;
using WindexBar.Core.Windowing;
using WindexBar.Core.Updates;
using WindexBar.Windows.Controllers;
using WindexBar.Windows.UI;
using WindexBar.Windows.Views;
using Microsoft.UI.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;
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
    private const double SideBarButtonHeight = 32;
    private const double SideBarButtonSpacing = 7;
    private const int SideBarButtonCount = 6;
    private const double SideBarNaturalHeight =
        (SideBarButtonHeight * SideBarButtonCount) + (SideBarButtonSpacing * (SideBarButtonCount - 1));
    private const string FastIndicatorGlyph = "\u26A1";

    private readonly UsageStore _usageStore;
    private readonly SettingsStore _settingsStore;
    private readonly WinUiDispatcherQueue _dispatcher;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly WindowPlacementController _windowPlacement = new(new WindowPosition(96, 96));
    private readonly List<DispatcherTimer> _scrollBarHideTimers = [];
    private readonly List<Button> _quitButtons = [];
    private readonly GaugeAnimator _gaugeAnimator;
    private readonly SettingsController _settingsController;
    private readonly CodexUpdateController _codexUpdateController;
    private bool _isFastServiceTier;
    private bool _isSideBarOpen = true;
    private bool _projectSessionsFirst = true;
    private bool _codexVersionCheckStarted;
    private Grid TitleBarDragRegion = null!;
    private Grid ContentRootGrid = null!;
    private Grid SideBarHost = null!;
    private ColumnDefinition SideBarColumn = null!;
    private Grid SideBarPanel = null!;
    private HudViewControl HudView = null!;
    private SessionsViewControl SessionsView = null!;
    private CreditsViewControl CreditsView = null!;
    private StyleViewControl StyleView = null!;
    private SettingsViewControl SettingsView = null!;
    private ResetCreditDetailsViewControl ResetCreditDetailsView = null!;
    private TextBlock CreditsTitleText = null!;
    private TextBlock CreditsDetailText = null!;
    private TextBlock ResetCreditDetailsTitleText = null!;
    private TextBlock ResetCreditSummaryText = null!;
    private TextBlock ResetCreditDetailsText = null!;
    private TextBlock StyleTitleText = null!;
    private TextBlock GaugeThicknessLabelText = null!;
    private TextBlock GaugeColorLabelText = null!;
    private TextBlock GaugeAnimationLabelText = null!;
    private Button SettingsButton = null!;
    private Button StyleButton = null!;
    private Button ResetCreditDetailsButton = null!;
    private Button SaveStyleButton = null!;
    private ComboBox GaugeThicknessComboBox = null!;
    private ComboBox GaugeAnimationComboBox = null!;
    private Button GaugeColorButton = null!;
    private Window? _gaugeColorWindow;
    private Window? _sessionDetailsWindow;
    private Window? _resetCreditDetailsWindow;
    private Window? _shortcutWindow;
    private Window? _codexUpdateDetailsWindow;
    private object? _codexUpdateOriginalInstallMethod;
    private string _codexUpdateOriginalCustomCommand = string.Empty;
    private bool _codexUpdateDetailsApplied;
    private global::Windows.UI.Color _selectedGaugeColor = global::Windows.UI.Color.FromArgb(0xFF, 0x8D, 0x78, 0xD6);
    private global::Windows.UI.Color _previewGaugeColor = global::Windows.UI.Color.FromArgb(0xFF, 0x8D, 0x78, 0xD6);
    private Grid StylePreviewTrackRoot = null!;
    private Border StylePreviewFillBar = null!;
    private Border StylePreviewSweepBar = null!;
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
        _dispatcher = WinUiDispatcherQueue.GetForCurrentThread();
        _gaugeAnimator = new GaugeAnimator(
            () => _settingsStore.Config.Style.GaugeAnimation,
            () => _isFastServiceTier);

        BuildLayout();
        _codexUpdateController = new CodexUpdateController(
            SettingsView,
            _settingsStore,
            _usageStore,
            codexCliUpdateService,
            () => RootLayout.XamlRoot,
            Text,
            _lifetimeCancellation.Token);
        _settingsController = new SettingsController(
            SettingsView,
            _settingsStore,
            _usageStore,
            _codexUpdateController,
            ShowHudView);
        _gaugeAnimator.SetPrimaryBars(HudView.CurrentGauge, HudView.WeeklyGauge);
        _gaugeAnimator.SetPreview(
            StyleView.PreviewGauge,
            ReadStyleSelection,
            () => StyleView.Visibility == Visibility.Visible);
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
                await _codexUpdateController.CheckAsync(forceLatestVersionRefresh: false);
            }
        };

        _usageStore.Changed += OnUsageChanged;
        _settingsStore.Changed += OnSettingsChanged;

        _gaugeAnimator.Start();

        Closed += (_, _) =>
        {
            _gaugeColorWindow?.Close();
            _gaugeColorWindow = null;
            CloseAuxiliaryWindows();
            AppWindow.Changed -= OnAppWindowChanged;
            _usageStore.Changed -= OnUsageChanged;
            _settingsStore.Changed -= OnSettingsChanged;
            _lifetimeCancellation.Cancel();
            _lifetimeCancellation.Dispose();
            _gaugeAnimator.Dispose();
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

        SideBarPanel = new Grid
        {
            RowSpacing = SideBarButtonSpacing,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = SideBarVisualWidth,
            Opacity = 0.72
        };
        for (var row = 0; row < SideBarButtonCount; row++)
        {
            SideBarPanel.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star)
            });
        }

        SideBarHost.SizeChanged += (_, args) =>
        {
            var availableHeight = Math.Max(0, args.NewSize.Height - SideBarButtonSpacing);
            SideBarPanel.Height = Math.Min(SideBarNaturalHeight, availableHeight);
        };
        SideBarHost.Children.Add(SideBarPanel);

        HomeButton = CreateSideBarButton("\u2302");
        HomeButton.Click += HomeButton_Click;
        Grid.SetRow(HomeButton, 0);
        SideBarPanel.Children.Add(HomeButton);

        SessionsButton = CreateSideBarButton("\u2637");
        SessionsButton.Click += SessionsButton_Click;
        Grid.SetRow(SessionsButton, 1);
        SideBarPanel.Children.Add(SessionsButton);

        CreditsButton = CreateSideBarButton("$");
        CreditsButton.Click += CreditsButton_Click;
        Grid.SetRow(CreditsButton, 2);
        SideBarPanel.Children.Add(CreditsButton);

        ResetCreditDetailsButton = CreateSideBarButton("\u21BB");
        ResetCreditDetailsButton.Click += ResetCreditDetailsButton_Click;
        Grid.SetRow(ResetCreditDetailsButton, 3);
        SideBarPanel.Children.Add(ResetCreditDetailsButton);

        StyleButton = CreateSideBarButton("\u25C8");
        StyleButton.Click += StyleButton_Click;
        Grid.SetRow(StyleButton, 4);
        SideBarPanel.Children.Add(StyleButton);

        SettingsButton = CreateSideBarButton("\u2699");
        SettingsButton.Click += SettingsButton_Click;
        Grid.SetRow(SettingsButton, 5);
        SideBarPanel.Children.Add(SettingsButton);


        HudView = new HudViewControl(CreateQuitButton());
        AttachTransientScrollBar(HudView.ScrollViewer);
        Grid.SetColumn(HudView, 1);
        ContentRootGrid.Children.Add(HudView);

        SessionsView = new SessionsViewControl(CreateQuitButton())
        {
            Visibility = Visibility.Collapsed
        };
        SessionsView.HomeRequested += (_, _) => ShowHudView();
        SessionsView.SortPreferenceChanged += (_, projectFirst) => ApplySessionSortPreference(projectFirst);
        SessionsView.SessionDetailsRequested += (_, args) => ShowSessionDetailsWindow(args);
        AttachTransientScrollBar(SessionsView.ScrollViewer);
        Grid.SetColumn(SessionsView, 1);
        ContentRootGrid.Children.Add(SessionsView);

        CreditsView = new CreditsViewControl(CreateQuitButton()) { Visibility = Visibility.Collapsed };
        CreditsView.HomeRequested += (_, _) => ShowHudView();
        AttachTransientScrollBar(CreditsView.ScrollViewer);
        CreditsTitleText = CreditsView.TitleText;
        CreditsDetailText = CreditsView.DetailText;
        Grid.SetColumn(CreditsView, 1);
        ContentRootGrid.Children.Add(CreditsView);

        SettingsView = new SettingsViewControl(CreateQuitButton()) { Visibility = Visibility.Collapsed };
        SettingsView.HomeRequested += (_, _) => ShowHudView();
        SettingsView.ShortcutEditRequested += (_, args) => ShowShortcutWindow(args.Target);
        SettingsView.UpdateDetailsRequested += (_, _) => ShowCodexUpdateDetailsWindow();
        SettingsView.UpdateDetailsApplyButton.Click += (_, _) =>
        {
            _codexUpdateDetailsApplied = true;
            _codexUpdateDetailsWindow?.Close();
        };
        SettingsView.UpdateDetailsCloseButton.Click += (_, _) => _codexUpdateDetailsWindow?.Close();
        AttachTransientScrollBar(SettingsView.ScrollViewer);
        Grid.SetColumn(SettingsView, 1);
        ContentRootGrid.Children.Add(SettingsView);

        StyleView = new StyleViewControl(CreateQuitButton()) { Visibility = Visibility.Collapsed };
        StyleView.HomeRequested += (_, _) => ShowHudView();
        StyleView.ColorButton.Click += (_, _) => ShowGaugeColorWindow();
        StyleView.ThicknessComboBox.SelectionChanged += (_, _) => ApplyStylePreview();
        StyleView.AnimationComboBox.SelectionChanged += (_, _) => ApplyStylePreview();
        StyleView.SaveButton.Click += SaveStyleButton_Click;
        AttachTransientScrollBar(StyleView.ScrollViewer);
        BindStyleControls();
        Grid.SetColumn(StyleView, 1);
        ContentRootGrid.Children.Add(StyleView);

        ResetCreditDetailsView = new ResetCreditDetailsViewControl(CreateQuitButton()) { Visibility = Visibility.Collapsed };
        ResetCreditDetailsView.HomeRequested += (_, _) => ShowHudView();
        ResetCreditDetailsView.DetailsRequested += (_, _) => ShowResetCreditDetailsWindow();
        AttachTransientScrollBar(ResetCreditDetailsView.ScrollViewer);
        ResetCreditDetailsTitleText = ResetCreditDetailsView.TitleText;
        ResetCreditSummaryText = ResetCreditDetailsView.SummaryText;
        ResetCreditDetailsText = ResetCreditDetailsView.DetailText;
        Grid.SetColumn(ResetCreditDetailsView, 1);
        ContentRootGrid.Children.Add(ResetCreditDetailsView);
        RootLayout.Children.Add(SideBarHost);
        ApplySideBarProgress();
    }


    private void ApplySessionSortPreference(bool projectSessionsFirst)
    {
        _projectSessionsFirst = projectSessionsFirst;
        UpdateSessionSortToggleAppearance();
        UpdateSessionUsageView(_usageStore.Snapshot?.Sessions);
    }

    private void UpdateSessionSortToggleAppearance()
    {
        SessionsView.SetLanguage(
            Text("Sessions", "\uC138\uC158"),
            _projectSessionsFirst,
            Text("Project sessions first", "\uD504\uB85C\uC81D\uD2B8 \uC138\uC158 \uC6B0\uC120"),
            Text("Non-project sessions first", "\uBE44\uD504\uB85C\uC81D\uD2B8 \uC138\uC158 \uC6B0\uC120"));
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
            Width = SideBarButtonHeight,
            MaxHeight = SideBarButtonHeight,
            MinWidth = SideBarButtonHeight,
            MinHeight = 0,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
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

    private void BindStyleControls()
    {
        StyleTitleText = StyleView.TitleText;
        GaugeThicknessLabelText = StyleView.ThicknessLabelText;
        GaugeColorLabelText = StyleView.ColorLabelText;
        GaugeAnimationLabelText = StyleView.AnimationLabelText;
        GaugeThicknessComboBox = StyleView.ThicknessComboBox;
        GaugeAnimationComboBox = StyleView.AnimationComboBox;
        GaugeColorButton = StyleView.ColorButton;
        SaveStyleButton = StyleView.SaveButton;
        StylePreviewTrackRoot = StyleView.PreviewGauge.Track;
        StylePreviewFillBar = StyleView.PreviewGauge.Fill;
        StylePreviewSweepBar = StyleView.PreviewGauge.Sweep;
        UpdateGaugeColorButton(_selectedGaugeColor);
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
        CloseAuxiliaryWindows();
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
        CloseAuxiliaryWindows();
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
        CloseAuxiliaryWindows();
        _settingsController.Load();
        HudView.Visibility = Visibility.Collapsed;
        SessionsView.Visibility = Visibility.Collapsed;
        CreditsView.Visibility = Visibility.Collapsed;
        StyleView.Visibility = Visibility.Collapsed;
        ResetCreditDetailsView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Visible;
        ApplyLanguage();
    }

    public void ShowStyleView()
    {
        CloseAuxiliaryWindows();
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
        CloseAuxiliaryWindows();
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
        CloseAuxiliaryWindows();
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
                ScrollHudBy(HudView.ScrollViewer.ViewportHeight);
                e.Handled = true;
                break;
            case VirtualKey.PageUp:
                ScrollHudBy(-HudView.ScrollViewer.ViewportHeight);
                e.Handled = true;
                break;
            case VirtualKey.Home:
                ScrollHudTo(0);
                e.Handled = true;
                break;
            case VirtualKey.End:
                ScrollHudTo(HudView.ScrollViewer.ScrollableHeight);
                e.Handled = true;
                break;
        }
    }

    private void ScrollHudBy(double delta) => ScrollHudTo(HudView.ScrollViewer.VerticalOffset + delta);

    private void ScrollHudTo(double verticalOffset)
    {
        var targetOffset = Math.Clamp(verticalOffset, 0, Math.Max(0, HudView.ScrollViewer.ScrollableHeight));
        HudView.ScrollViewer.ChangeView(null, targetOffset, null, disableAnimation: false);
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
            CloseAuxiliaryWindows();
        }
    }

    private void CloseAuxiliaryWindows()
    {
        var sessionDetails = _sessionDetailsWindow;
        _sessionDetailsWindow = null;
        sessionDetails?.Close();

        var resetDetails = _resetCreditDetailsWindow;
        _resetCreditDetailsWindow = null;
        resetDetails?.Close();

        var shortcut = _shortcutWindow;
        _shortcutWindow = null;
        shortcut?.Close();

        var updateDetails = _codexUpdateDetailsWindow;
        _codexUpdateDetailsWindow = null;
        if (updateDetails is not null)
        {
            OwnedPopupWindow.DetachContent(updateDetails);
            updateDetails.Close();
        }
    }

    private void ShowSessionDetailsWindow(SessionDetailsRequestedEventArgs args)
    {
        _sessionDetailsWindow?.Close();

        var session = args.Session;
        var detailsPanel = new StackPanel { Spacing = 9 };
        detailsPanel.Children.Add(new TextBlock
        {
            Text = session.DisplayName,
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        detailsPanel.Children.Add(FeatureViewHelpers.CreateDivider());
        AddPopupDetail(detailsPanel, Text("Project", "\uD504\uB85C\uC81D\uD2B8"), args.ProjectName);
        AddPopupDetail(detailsPanel, Text("Path", "\uACBD\uB85C"), session.ProjectPath ?? UnknownText);
        AddPopupDetail(detailsPanel, Text("Session ID", "\uC138\uC158 ID"), session.SessionId);
        AddPopupDetail(detailsPanel, Text("Context", "\uCEE8\uD14D\uC2A4\uD2B8"), session.ContextPercentText);
        AddPopupDetail(detailsPanel, Text("Token usage", "\uD1A0\uD070 \uC0AC\uC6A9\uB7C9"), session.TokenDetails);
        AddPopupDetail(
            detailsPanel,
            Text("Last activity", "\uB9C8\uC9C0\uB9C9 \uD65C\uB3D9"),
            session.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));

        var scrollViewer = new ScrollViewer
        {
            Height = 260,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Auto,
            Content = detailsPanel
        };

        var copyButton = FeatureViewHelpers.CreateCompactButton(Text("Copy details", "\uC0C1\uC138 \uBCF5\uC0AC"));
        copyButton.Click += (_, _) =>
        {
            CopyText(string.Join(
                Environment.NewLine,
                $"{Text("Project", "\uD504\uB85C\uC81D\uD2B8")}: {args.ProjectName}",
                $"{Text("Path", "\uACBD\uB85C")}: {session.ProjectPath ?? UnknownText}",
                $"{Text("Session ID", "\uC138\uC158 ID")}: {session.SessionId}",
                $"{Text("Context", "\uCEE8\uD14D\uC2A4\uD2B8")}: {session.ContextPercentText}",
                session.TokenDetails,
                $"{Text("Last activity", "\uB9C8\uC9C0\uB9C9 \uD65C\uB3D9")}: {session.UpdatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}"));
            ShowCopiedFeedback(copyButton);
        };
        var closeButton = FeatureViewHelpers.CreateCompactButton(Text("Close", "\uB2EB\uAE30"));
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 6
        };
        buttons.Children.Add(copyButton);
        buttons.Children.Add(closeButton);
        var panel = new Grid { Width = 300, RowSpacing = 9 };
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.Children.Add(scrollViewer);
        Grid.SetRow(buttons, 1);
        panel.Children.Add(buttons);

        var popup = OwnedPopupWindow.Create(
            this,
            Text("Session details", "\uC138\uC158 \uC0C1\uC138"),
            panel,
            PopupScale,
            logicalWidth: 330,
            logicalHeight: 340);
        _sessionDetailsWindow = popup;
        closeButton.Click += (_, _) => popup.Close();
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(_sessionDetailsWindow, popup))
            {
                _sessionDetailsWindow = null;
            }
        };
        popup.Activate();
    }

    private void ShowResetCreditDetailsWindow()
    {
        if (_resetCreditDetailsWindow is not null)
        {
            _resetCreditDetailsWindow.Activate();
            return;
        }

        var detailText = new TextBlock
        {
            Text = ResetCreditDetailsText.Text,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"),
            Foreground = Brush(0xFF, 0xED, 0xE7, 0xFF)
        };
        var scrollViewer = new ScrollViewer
        {
            Height = 270,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = detailText
        };
        var panel = new StackPanel { Width = 330, Spacing = 9 };
        panel.Children.Add(new TextBlock
        {
            Text = Text("Reset credit bank", "리셋 크레딧 뱅크"),
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        panel.Children.Add(FeatureViewHelpers.CreateDivider());
        panel.Children.Add(scrollViewer);
        var copyButton = FeatureViewHelpers.CreateCompactButton(Text("Copy details", "상세 복사"));
        copyButton.Click += (_, _) =>
        {
            CopyText(ResetCreditDetailsText.Text);
            ShowCopiedFeedback(copyButton);
        };
        var closeButton = FeatureViewHelpers.CreateCompactButton(Text("Close", "닫기"));
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 6
        };
        buttons.Children.Add(copyButton);
        buttons.Children.Add(closeButton);
        panel.Children.Add(buttons);

        var popup = OwnedPopupWindow.Create(
            this,
            Text("Reset credit bank", "리셋 크레딧 뱅크"),
            panel,
            PopupScale,
            logicalWidth: 360,
            logicalHeight: 370);
        _resetCreditDetailsWindow = popup;
        closeButton.Click += (_, _) => popup.Close();
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(_resetCreditDetailsWindow, popup))
            {
                _resetCreditDetailsWindow = null;
            }
        };
        popup.Activate();
    }

    private void ShowShortcutWindow(ShortcutTarget target)
    {
        _shortcutWindow?.Close();
        _shortcutWindow = null;

        var targetButton = target == ShortcutTarget.ToggleWindow
            ? SettingsView.ToggleHotkeyButton
            : SettingsView.ToggleSidebarHotkeyButton;
        var otherButton = target == ShortcutTarget.ToggleWindow
            ? SettingsView.ToggleSidebarHotkeyButton
            : SettingsView.ToggleHotkeyButton;
        var candidate = HotkeyShortcut.NormalizeOrDefault(
            targetButton.Content as string,
            target == ShortcutTarget.ToggleWindow
                ? WindexBarConfig.DefaultToggleWindowHotkey
                : WindexBarConfig.DefaultToggleSidebarHotkey);

        var captureButton = new Button
        {
            Content = candidate,
            Height = 44,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            FontSize = 15
        };
        var status = new TextBlock
        {
            Text = Text("Press a modifier and key.", "보조 키와 일반 키를 함께 눌러 주세요."),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            FontSize = 11
        };
        var applyButton = FeatureViewHelpers.CreateCompactButton(Text("Apply", "적용"));
        var cancelButton = FeatureViewHelpers.CreateCompactButton(Text("Close", "닫기"));
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 6
        };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(applyButton);
        var panel = new StackPanel { Width = 260, Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = target == ShortcutTarget.ToggleWindow
                ? Text("Toggle shortcut", "토글 단축키")
                : Text("Sidebar shortcut", "사이드바 단축키"),
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        panel.Children.Add(status);
        panel.Children.Add(captureButton);
        panel.Children.Add(buttons);

        var popup = OwnedPopupWindow.Create(
            this,
            Text("Shortcut", "단축키"),
            panel,
            PopupScale,
            logicalWidth: 290,
            logicalHeight: 175);
        _shortcutWindow = popup;
        captureButton.KeyDown += (_, keyArgs) =>
        {
            var keyName = ShortcutKeyName(keyArgs.Key);
            if (keyName is null)
            {
                return;
            }

            var shortcut = new HotkeyShortcut(
                IsKeyDown(VirtualKey.Control),
                IsKeyDown(VirtualKey.Menu),
                IsKeyDown(VirtualKey.Shift),
                IsKeyDown(VirtualKey.LeftWindows) || IsKeyDown(VirtualKey.RightWindows),
                keyName);
            if (!shortcut.HasModifier)
            {
                status.Text = Text("Include Ctrl, Alt, Shift, or Win.", "Ctrl, Alt, Shift 또는 Win 키를 포함해 주세요.");
                status.Foreground = Brush(0xFF, 0xFF, 0x7B, 0x72);
                keyArgs.Handled = true;
                return;
            }

            candidate = shortcut.DisplayText;
            captureButton.Content = candidate;
            var conflicts = string.Equals(candidate, otherButton.Content as string, StringComparison.OrdinalIgnoreCase);
            status.Text = conflicts
                ? Text("That shortcut is already used.", "이미 다른 기능에서 사용하는 단축키예요.")
                : Text("Shortcut captured. Apply it below.", "단축키를 인식했어요. 아래에서 적용해 주세요.");
            status.Foreground = conflicts
                ? Brush(0xFF, 0xFF, 0x7B, 0x72)
                : Brush(0xFF, 0xB9, 0xA7, 0xE8);
            applyButton.IsEnabled = !conflicts;
            keyArgs.Handled = true;
        };
        applyButton.Click += (_, _) =>
        {
            targetButton.Content = candidate;
            popup.Close();
        };
        cancelButton.Click += (_, _) => popup.Close();
        captureButton.Loaded += (_, _) => captureButton.Focus(FocusState.Programmatic);
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(_shortcutWindow, popup))
            {
                _shortcutWindow = null;
            }
        };
        popup.Activate();
    }

    private void ShowCodexUpdateDetailsWindow()
    {
        if (_codexUpdateDetailsWindow is not null)
        {
            _codexUpdateDetailsWindow.Activate();
            return;
        }

        _codexUpdateOriginalInstallMethod = SettingsView.CodexInstallMethodComboBox.SelectedItem;
        _codexUpdateOriginalCustomCommand = SettingsView.CustomCodexUpdateCommandTextBox.Text;
        _codexUpdateDetailsApplied = false;
        var popup = OwnedPopupWindow.Create(
            this,
            Text("Codex update details", "Codex 업데이트 상세"),
            SettingsView.UpdateDetailsContent,
            PopupScale,
            logicalWidth: 310,
            logicalHeight: 350);
        _codexUpdateDetailsWindow = popup;
        popup.Closed += (_, _) =>
        {
            if (!_codexUpdateDetailsApplied)
            {
                SettingsView.CodexInstallMethodComboBox.SelectedItem = _codexUpdateOriginalInstallMethod;
                SettingsView.CustomCodexUpdateCommandTextBox.Text = _codexUpdateOriginalCustomCommand;
            }

            _codexUpdateOriginalInstallMethod = null;
            _codexUpdateOriginalCustomCommand = string.Empty;
            OwnedPopupWindow.DetachContent(popup);
            if (ReferenceEquals(_codexUpdateDetailsWindow, popup))
            {
                _codexUpdateDetailsWindow = null;
            }
        };
        popup.Activate();
    }

    private double PopupScale => RootLayout.XamlRoot?.RasterizationScale ?? 1d;

    private static void AddPopupDetail(StackPanel panel, string label, string value)
    {
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Opacity = 0.7
        });
        panel.Children.Add(new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        });
    }

    private static void CopyText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private void ShowCopiedFeedback(Button button)
    {
        var originalContent = button.Content;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        button.Content = Text("Copied!", "\uBCF5\uC0AC\uB428!");
        button.IsEnabled = false;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            button.Content = originalContent;
            button.IsEnabled = true;
        };
        timer.Start();
    }

    private static bool IsKeyDown(VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;

    private static string? ShortcutKeyName(VirtualKey key)
    {
        var value = (int)key;
        if (value is >= 0x30 and <= 0x39 || value is >= 0x41 and <= 0x5A)
        {
            return ((char)value).ToString();
        }

        if (value is >= 0x70 and <= 0x87)
        {
            return $"F{value - 0x70 + 1}";
        }

        return value switch
        {
            0x20 => "Space",
            0x1B => "Escape",
            0x09 => "Tab",
            0x0D => "Enter",
            0x08 => "Backspace",
            0x2D => "Insert",
            0x2E => "Delete",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x26 => "Up",
            0x28 => "Down",
            0x25 => "Left",
            0x27 => "Right",
            _ => null
        };
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
        var candidateColor = _previewGaugeColor;
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
                candidateColor = ApplyGaugeBrightness(baseColor, brightnessValue);
                UpdateSwatchSelection();
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
            candidateColor = ApplyGaugeBrightness(baseColor, brightnessValue);
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
        var applyButton = FeatureViewHelpers.CreateCompactButton(Text("Apply", "\uC801\uC6A9"));
        applyButton.Click += (_, _) =>
        {
            _previewGaugeColor = candidateColor;
            UpdateGaugeColorButton(_previewGaugeColor);
            ApplyStylePreview();
            CloseGaugeColorWindow(discardPendingColor: false);
        };
        var closeButton = FeatureViewHelpers.CreateCompactButton(Text("Close", "\uB2EB\uAE30"));
        closeButton.Click += (_, _) => CloseGaugeColorWindow(discardPendingColor: false);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 6
        };
        buttons.Children.Add(applyButton);
        buttons.Children.Add(closeButton);
        panel.Children.Add(buttons);
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
        var popupHeight = (int)Math.Ceiling(190 * popupScale);
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

    private void ApplyStylePreview()
    {
        if (StylePreviewTrackRoot is null
            || GaugeThicknessComboBox?.SelectedItem is null
            || GaugeAnimationComboBox?.SelectedItem is null)
        {
            return;
        }

        _gaugeAnimator.RefreshPreview();
    }


    private void ApplyLanguage()
    {
        Title = SettingsView.Visibility == Visibility.Visible
            ? Text("WindexBar Settings", "WindexBar \uC124\uC815")
            : StyleView.Visibility == Visibility.Visible
                ? Text("WindexBar Style", "WindexBar \uC2A4\uD0C0\uC77C")
                : "WindexBar";
        ApplyWindowSectionLabels();
        HudView.AccountLabelText.Text = Text("Account", "\uACC4\uC815");
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
        ResetCreditDetailsTitleText.Text = Text("Reset credit bank", "\uB9AC\uC14B \uD06C\uB808\uB527 \uBC45\uD06C");
        ResetCreditDetailsView.DetailsButton.Content = Text("View details", "\uC0C1\uC138 \uBCF4\uAE30");
        StyleTitleText.Text = Text("Style", "\uC2A4\uD0C0\uC77C");
        GaugeThicknessLabelText.Text = Text("Gauge thickness", "\uAC8C\uC774\uC9C0 \uB450\uAED8");
        GaugeColorLabelText.Text = Text("Gauge color", "\uAC8C\uC774\uC9C0 \uC0C9\uC0C1");
        GaugeAnimationLabelText.Text = Text("Animation", "\uC560\uB2C8\uBA54\uC774\uC158");
        _settingsController.ApplyLanguage(Text);
        UpdateSessionSortToggleAppearance();
    }

    private string CurrentLanguage => WindexBarConfig.NormalizeLanguage(_settingsStore.Config.Language);

    private bool IsKorean => CurrentLanguage == "ko";

    private string Text(string english, string korean) => IsKorean ? korean : english;

    private string UnknownText => Text("unknown", "\uC54C \uC218 \uC5C6\uC74C");

    private void ApplyWindowSectionLabels()
    {
        HudView.CurrentLabelText.Text = WithFastIndicator(Text("Current", "\uD604\uC7AC"));
        HudView.WeeklyLabelText.Text = WithFastIndicator(Text("Weekly", "\uC8FC\uAC04"));
        UpdateSessionSortToggleAppearance();
    }

    private string WithFastIndicator(string label) =>
        _isFastServiceTier ? $"{label} {FastIndicatorGlyph}" : label;

    private void ApplyProgressBarTheme()
    {
        var style = _settingsStore.Config.Style.Normalized();
        _gaugeAnimator.ApplyAppearance(style);
    }

    private void QuitButton_Click(object sender, RoutedEventArgs args) => App.Current.Shutdown();

    private void UpdateState()
    {
        var snapshot = _usageStore.Snapshot;
        var credits = _usageStore.Credits;
        var disabled = !_settingsStore.Codex.Enabled;
        var hud = HudDisplayModelFactory.Create(
            snapshot,
            _usageStore.LastError,
            disabled,
            CurrentLanguage,
            DateTimeOffset.Now);
        _isFastServiceTier = hud.IsFastServiceTier;
        ApplyWindowSectionLabels();
        UpdateCredits(credits);
        UpdateResetCreditDetails(snapshot?.RateLimitResetCredits);
        HudView.Bind(
            hud,
            WithFastIndicator(Text("Current", "현재")),
            WithFastIndicator(Text("Weekly", "주간")));
        _gaugeAnimator.SetPrimaryTargets(hud.Current.TargetValue, hud.Weekly.TargetValue);
        UpdateSessionUsageView(snapshot?.Sessions);
        ApplyProgressBarTheme();
    }


    private void UpdateCredits(CreditsSnapshot? credits)
    {
        CreditsDetailText.Text = FormatCredits(credits);
    }

    private void UpdateResetCreditDetails(RateLimitResetCreditsSnapshot? resetCredits)
    {
        ResetCreditSummaryText.Text = RateLimitResetCreditFormatter.FormatSummary(resetCredits, CurrentLanguage);
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


    private void UpdateSessionUsageView(IReadOnlyList<CodexSessionUsageSnapshot>? sessions)
    {
        var model = SessionListViewModelFactory.Create(sessions, _projectSessionsFirst, CurrentLanguage);
        SessionsView.Render(model, _settingsStore.Config.Style.Normalized());
        _gaugeAnimator.ReplaceSessionBars(SessionsView.Gauges);
    }

}
