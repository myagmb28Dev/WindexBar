using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WindexBar.Core.Config;
using WindexBar.Core.Formatting;
using WindexBar.Core.Models;
using WindexBar.Core.Presentation;
using WindexBar.Core.Providers.Codex;
using WindexBar.Core.Refresh;
using WindexBar.Core.Updates;
using WindexBar.Core.Windowing;
using WindexBar.Windows.Controllers;
using WindexBar.Windows.Dialogs;
using WindexBar.Windows.UI;
using WindexBar.Windows.Views;
using static WindexBar.Windows.Views.FeatureViewHelpers;
using WinUiDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace WindexBar.Windows;

public sealed partial class MainWindow : Window
{
    private const double TitleBarClientHeight = 34;
    private const double HudClientWidth = 265;
    private const double ContentClientHeight = 334;
    private const double SettingsClientWidth = HudClientWidth;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    private readonly UsageStore _usageStore;
    private readonly SettingsStore _settingsStore;
    private readonly WinUiDispatcherQueue _dispatcher;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly WindowPlacementController _windowPlacement = new(new WindowPosition(96, 96));
    private readonly List<Button> _quitButtons = [];
    private readonly GaugeAnimator _gaugeAnimator;
    private readonly SettingsController _settingsController;
    private readonly CodexUpdateController _codexUpdateController;
    private readonly RateLimitResetCreditRedemptionCoordinator _resetCreditRedemptionCoordinator;

    private readonly TransientScrollBarManager _scrollBarManager = new();
    private readonly SidebarController _sidebarController;
    private readonly GaugeColorPickerPopup _gaugeColorPickerPopup = new();
    private readonly ResetCreditBankDialog _resetCreditBankDialog;

    private bool _isFastServiceTier;
    private bool _hasAppliedInitialWindowSize;
    private bool _projectSessionsFirst = true;
    private bool _codexVersionCheckStarted;

    private TitleBarControl TitleBar = null!;
    private Grid ContentRootGrid = null!;
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
    private Button SaveStyleButton = null!;
    private ComboBox GaugeThicknessComboBox = null!;
    private ComboBox GaugeAnimationComboBox = null!;
    private Button GaugeColorButton = null!;

    private Window? _sessionDetailsWindow;
    private Window? _shortcutWindow;
    private Window? _codexUpdateDetailsWindow;
    private Window? _appUpdatePromptWindow;
    private object? _codexUpdateOriginalInstallMethod;
    private string _codexUpdateOriginalCustomCommand = string.Empty;
    private bool _codexUpdateDetailsApplied;
    private global::Windows.UI.Color _selectedGaugeColor = global::Windows.UI.Color.FromArgb(0xFF, 0x8D, 0x78, 0xD6);
    private global::Windows.UI.Color _previewGaugeColor = global::Windows.UI.Color.FromArgb(0xFF, 0x8D, 0x78, 0xD6);

    public MainWindow(
        UsageStore usageStore,
        SettingsStore settingsStore,
        CodexCliUpdateService codexCliUpdateService,
        IRateLimitResetCreditConsumer? resetCreditConsumer = null)
    {
        InitializeComponent();
        _usageStore = usageStore;
        _settingsStore = settingsStore;
        _resetCreditRedemptionCoordinator = new RateLimitResetCreditRedemptionCoordinator(
            resetCreditConsumer ?? new CodexRateLimitResetCreditConsumer());
        _dispatcher = WinUiDispatcherQueue.GetForCurrentThread();
        _gaugeAnimator = new GaugeAnimator(
            () => _settingsStore.Config.Style.GaugeAnimation,
            () => _isFastServiceTier);

        _sidebarController = new SidebarController(
            this,
            RootLayout,
            _settingsStore,
            Text,
            Brush);

        _resetCreditBankDialog = new ResetCreditBankDialog(
            this,
            _usageStore,
            _resetCreditRedemptionCoordinator,
            () => PopupScale,
            Text,
            () => CurrentLanguage,
            Brush,
            () => _settingsStore.Codex.Enabled,
            _lifetimeCancellation.Token);

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
            _codexUpdateController,
            ShowHudView);

        _gaugeAnimator.SetPrimaryBars(HudView.CurrentGauge, HudView.WeeklyGauge);
        _gaugeAnimator.SetPreview(
            StyleView.PreviewGauge,
            ReadStyleSelection,
            () => StyleView.Visibility == Visibility.Visible);

        ApplyLanguage();
        ConfigureCompactWindow();
        ApplyInitialWindowSize();

        AppWindow.Changed += OnAppWindowChanged;
        RootLayout.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnScrollNavigationKeyDown), true);
        RootLayout.PointerPressed += (_, args) =>
        {
            if (!TransientScrollBarManager.IsArrowKeyInputControl(args.OriginalSource as DependencyObject))
            {
                RootLayout.Focus(FocusState.Pointer);
            }
        };

        RootLayout.Loaded += OnRootLayoutLoaded;
        RootLayout.SizeChanged += RootLayout_SizeChanged;

        _usageStore.Changed += OnUsageChanged;
        _settingsStore.Changed += OnSettingsChanged;

        _gaugeAnimator.SetActive(AppWindow.IsVisible);

        Closed += (_, _) =>
        {
            CloseGaugeColorWindow();
            CloseAuxiliaryWindows();
            _codexUpdateController.Dispose();
            _sidebarController.Dispose();
            _scrollBarManager.Dispose();
            AppWindow.Changed -= OnAppWindowChanged;
            _usageStore.Changed -= OnUsageChanged;
            _settingsStore.Changed -= OnSettingsChanged;
            _lifetimeCancellation.Cancel();
            _lifetimeCancellation.Dispose();
            _gaugeAnimator.Dispose();
        };

        UpdateState();
    }

    internal bool HasOpenAppUpdatePrompt => _appUpdatePromptWindow is not null;
    internal bool HasCompletedStartup { get; private set; }
    internal event EventHandler? StartupCompleted;

    private void BuildLayout()
    {
        RootLayout.Background = Brush(0xFF, 0x25, 0x25, 0x27);
        RootLayout.RowDefinitions.Clear();
        RootLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TitleBarClientHeight) });
        RootLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        TitleBar = new TitleBarControl(
            TitleText_PointerPressed,
            MinimizeCircleButton_Click,
            ZoomCircleButton_Click,
            Brush);
        Grid.SetRow(TitleBar, 0);
        RootLayout.Children.Add(TitleBar);

        ContentRootGrid = new Grid { Padding = new Thickness(10, 0, 10, 10) };
        Grid.SetRow(ContentRootGrid, 1);
        RootLayout.Children.Add(ContentRootGrid);

        _sidebarController.HomeButton.Click += (_, _) => NavigateFromSideBar(ShowHudView);
        _sidebarController.SessionsButton.Click += SessionsButton_Click;
        _sidebarController.CreditsButton.Click += CreditsButton_Click;
        _sidebarController.ResetCreditDetailsButton.Click += ResetCreditDetailsButton_Click;
        _sidebarController.StyleButton.Click += StyleButton_Click;
        _sidebarController.SettingsButton.Click += SettingsButton_Click;

        HudView = new HudViewControl(CreateQuitButton(), AppReleaseVersion.DisplayValue);
        _scrollBarManager.Attach(HudView.ScrollViewer);
        ContentRootGrid.Children.Add(HudView);

        SessionsView = new SessionsViewControl(CreateQuitButton())
        {
            Visibility = Visibility.Collapsed
        };
        SessionsView.HomeRequested += (_, _) => ShowHudView();
        SessionsView.SortPreferenceChanged += (_, projectFirst) => ApplySessionSortPreference(projectFirst);
        SessionsView.SessionDetailsRequested += (_, args) => ShowSessionDetailsWindow(args);
        _scrollBarManager.Attach(SessionsView.ScrollViewer);
        ContentRootGrid.Children.Add(SessionsView);

        CreditsView = new CreditsViewControl(CreateQuitButton()) { Visibility = Visibility.Collapsed };
        CreditsView.HomeRequested += (_, _) => ShowHudView();
        _scrollBarManager.Attach(CreditsView.ScrollViewer);
        CreditsTitleText = CreditsView.TitleText;
        CreditsDetailText = CreditsView.DetailText;
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
        _scrollBarManager.Attach(SettingsView.ScrollViewer);
        ContentRootGrid.Children.Add(SettingsView);

        StyleView = new StyleViewControl(CreateQuitButton()) { Visibility = Visibility.Collapsed };
        StyleView.HomeRequested += (_, _) => ShowHudView();
        StyleView.ColorButton.Click += (_, _) => ShowGaugeColorWindow();
        StyleView.ThicknessComboBox.SelectionChanged += (_, _) => ApplyStylePreview();
        StyleView.AnimationComboBox.SelectionChanged += (_, _) => ApplyStylePreview();
        StyleView.SaveButton.Click += SaveStyleButton_Click;
        _scrollBarManager.Attach(StyleView.ScrollViewer);
        BindStyleControls();
        ContentRootGrid.Children.Add(StyleView);

        ResetCreditDetailsView = new ResetCreditDetailsViewControl(CreateQuitButton()) { Visibility = Visibility.Collapsed };
        ResetCreditDetailsView.HomeRequested += (_, _) => ShowHudView();
        ResetCreditDetailsView.DetailsRequested += (_, _) => ShowResetCreditDetailsWindow();
        _scrollBarManager.Attach(ResetCreditDetailsView.ScrollViewer);
        ResetCreditDetailsTitleText = ResetCreditDetailsView.TitleText;
        ResetCreditSummaryText = ResetCreditDetailsView.SummaryText;
        ResetCreditDetailsText = ResetCreditDetailsView.DetailText;
        ContentRootGrid.Children.Add(ResetCreditDetailsView);

        _sidebarController.ApplySideBarLayout();
    }

    private async void OnRootLayoutLoaded(object sender, RoutedEventArgs args)
    {
        RootLayout.Loaded -= OnRootLayoutLoaded;
        RootLayout.Focus(FocusState.Programmatic);
        try
        {
            ApplyInitialWindowSize();
            _hasAppliedInitialWindowSize = true;
            if (!_codexVersionCheckStarted)
            {
                _codexVersionCheckStarted = true;
                await _codexUpdateController.CheckAsync(forceLatestVersionRefresh: false);
            }
        }
        catch (Exception error)
        {
            AppLog.Write("Failed to complete WindexBar window startup.", error);
        }
        finally
        {
            HasCompletedStartup = true;
            try
            {
                StartupCompleted?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception error)
            {
                AppLog.Write("Failed to notify that WindexBar window startup completed.", error);
            }

            AppLog.Write("WindexBar window startup completed.");
        }
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
        UpdateGaugeColorButton(_selectedGaugeColor);
    }

    private Button CreateQuitButton()
    {
        var button = CreateCompactButton("Quit");
        button.Click += QuitButton_Click;
        _quitButtons.Add(button);
        return button;
    }

    public void ShowHudView()
    {
        ShowContentView(HudView);
        RootLayout.Focus(FocusState.Programmatic);
        UpdateState();
    }

    public void ShowCreditsView()
    {
        ShowContentView(CreditsView);
        RootLayout.Focus(FocusState.Programmatic);
        UpdateCredits(_usageStore.Credits);
    }

    public void ShowSettingsView()
    {
        ShowContentView(SettingsView, beforeShow: _settingsController.Load);
    }

    public void ShowStyleView()
    {
        ShowContentView(
            StyleView,
            closeGaugeColor: false,
            beforeShow: () =>
            {
                SelectStyleOption(GaugeThicknessComboBox, _settingsStore.Config.Style.GaugeThickness);
                SelectStyleOption(GaugeAnimationComboBox, _settingsStore.Config.Style.GaugeAnimation);
                _selectedGaugeColor = ParseGaugeColor(_settingsStore.Config.Style.GaugeColor);
                _previewGaugeColor = _selectedGaugeColor;
                UpdateGaugeColorButton(_selectedGaugeColor);
            });
        ApplyStylePreview();
        RootLayout.Focus(FocusState.Programmatic);
    }

    public void ShowResetCreditDetailsView()
    {
        ShowContentView(ResetCreditDetailsView);
        RootLayout.Focus(FocusState.Programmatic);
        UpdateResetCreditDetails(_usageStore.Snapshot?.RateLimitResetCredits);
    }

    public void ShowSessionsView()
    {
        ShowContentView(SessionsView);
        RootLayout.Focus(FocusState.Programmatic);
        UpdateSessionUsageView(_usageStore.Snapshot?.Sessions);
    }

    private void ShowContentView(
        FrameworkElement visibleView,
        bool closeGaugeColor = true,
        Action? beforeShow = null)
    {
        if (closeGaugeColor)
        {
            CloseGaugeColorWindow();
        }

        CloseAuxiliaryWindows();
        beforeShow?.Invoke();
        foreach (var view in new FrameworkElement[]
                 {
                     HudView,
                     SessionsView,
                     CreditsView,
                     StyleView,
                     SettingsView,
                     ResetCreditDetailsView
                 })
        {
            view.Visibility = ReferenceEquals(view, visibleView)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        ApplyLanguage();
    }

    private void ConfigureCompactWindow()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar.DragRegion);
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

    private void ApplyInitialWindowSize()
    {
        if (!TryRestoreWindowSize())
        {
            ResizeForCurrentView();
        }
    }

    private bool TryRestoreWindowSize()
    {
        var window = _settingsStore.Config.Window;
        if (window.ClientWidth is not { } width || window.ClientHeight is not { } height)
        {
            return false;
        }

        ResizeClientToEffectiveSize(width, height);
        return true;
    }

    private void ResizeClientToEffectiveSize(double width, double height)
    {
        var scale = RootLayout.XamlRoot?.RasterizationScale ?? WindowScale();
        var clientWidth = (int)Math.Ceiling(width * scale);
        var clientHeight = (int)Math.Ceiling(height * scale);
        var position = _windowPlacement.PositionForResize(new WindowPosition(AppWindow.Position.X, AppWindow.Position.Y));
        AppWindow.ResizeClient(new SizeInt32(clientWidth, clientHeight));
        AppWindow.Move(new PointInt32(position.X, position.Y));
    }

    private double WindowScale()
    {
        var window = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var dpi = window == IntPtr.Zero ? 0 : GetDpiForWindow(window);
        return dpi == 0 ? 1d : dpi / 96d;
    }

    private void RootLayout_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        _sidebarController.UpdateSideBarHeight();
        if (!_hasAppliedInitialWindowSize)
        {
            return;
        }

        _settingsStore.Config.Window.ClientWidth = Math.Round(args.NewSize.Width, 2);
        _settingsStore.Config.Window.ClientHeight = Math.Round(args.NewSize.Height, 2);
    }

    public void ToggleSideBar() => _sidebarController.ToggleSideBar();

    private void OnUsageChanged(object? sender, EventArgs args) =>
        _dispatcher.TryEnqueue(UpdateState);

    private void OnSettingsChanged(object? sender, EventArgs args)
    {
        _dispatcher.TryEnqueue(() =>
        {
            ApplyLanguage();
            _sidebarController.ApplySideBarHoverPreference();
            UpdateState();
        });
    }

    private void OnScrollNavigationKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var scrollViewer = VisibleScrollViewer();
        if (scrollViewer is not null)
        {
            TransientScrollBarManager.HandleScrollNavigationKeyDown(scrollViewer, e);
        }
    }

    private ScrollViewer? VisibleScrollViewer()
    {
        if (HudView.Visibility == Visibility.Visible)
        {
            return HudView.ScrollViewer;
        }

        if (SessionsView.Visibility == Visibility.Visible)
        {
            return SessionsView.ScrollViewer;
        }

        if (CreditsView.Visibility == Visibility.Visible)
        {
            return CreditsView.ScrollViewer;
        }

        if (SettingsView.Visibility == Visibility.Visible)
        {
            return SettingsView.ScrollViewer;
        }

        if (StyleView.Visibility == Visibility.Visible)
        {
            return StyleView.ScrollViewer;
        }

        return ResetCreditDetailsView.Visibility == Visibility.Visible
            ? ResetCreditDetailsView.ScrollViewer
            : null;
    }

    private void MinimizeCircleButton_Click(object sender, RoutedEventArgs args) =>
        WindowCloseBehavior.Hide(this);

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

    private void SessionsButton_Click(object sender, RoutedEventArgs args)
    {
        if (SessionsView.Visibility == Visibility.Visible)
        {
            NavigateFromSideBar(ShowHudView);
            return;
        }

        NavigateFromSideBar(ShowSessionsView);
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
            NavigateFromSideBar(ShowHudView);
            return;
        }

        NavigateFromSideBar(ShowCreditsView);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs args)
    {
        if (SettingsView.Visibility == Visibility.Visible)
        {
            NavigateFromSideBar(ShowHudView);
            return;
        }

        NavigateFromSideBar(ShowSettingsView);
    }

    private void StyleButton_Click(object sender, RoutedEventArgs args)
    {
        if (StyleView.Visibility == Visibility.Visible)
        {
            NavigateFromSideBar(ShowHudView);
            return;
        }

        NavigateFromSideBar(ShowStyleView);
    }

    private void ResetCreditDetailsButton_Click(object sender, RoutedEventArgs args)
    {
        if (ResetCreditDetailsView.Visibility == Visibility.Visible)
        {
            NavigateFromSideBar(ShowHudView);
            return;
        }

        NavigateFromSideBar(ShowResetCreditDetailsView);
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidVisibilityChange)
        {
            return;
        }

        if (!sender.IsVisible)
        {
            _gaugeAnimator.SetActive(false);
            _sidebarController.OnWindowVisibilityChanged(false);
            CloseGaugeColorWindow();
            CloseAuxiliaryWindows();
            return;
        }

        _gaugeAnimator.SetActive(true);
        _sidebarController.OnWindowVisibilityChanged(true);
    }

    private void CloseAuxiliaryWindows()
    {
        var appUpdatePrompt = _appUpdatePromptWindow;
        _appUpdatePromptWindow = null;
        appUpdatePrompt?.Close();

        var sessionDetails = _sessionDetailsWindow;
        _sessionDetailsWindow = null;
        sessionDetails?.Close();

        _resetCreditBankDialog.Close();

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

        _sidebarController.CloseModeLockedNotice();
    }

    internal void ShowModeLockedNotice(string title, string message) =>
        _sidebarController.ShowModeLockedNotice(title, message);

    internal Task<bool> PromptForAppUpdateAsync(AppVersion version, CancellationToken cancellationToken)
    {
        _appUpdatePromptWindow?.Close();
        return AppUpdatePromptPopup.PromptAsync(
            this,
            version,
            PopupScale,
            Text,
            popup => _appUpdatePromptWindow = popup,
            popup =>
            {
                if (ReferenceEquals(_appUpdatePromptWindow, popup))
                {
                    _appUpdatePromptWindow = null;
                }
            },
            cancellationToken);
    }

    private void ShowSessionDetailsWindow(SessionDetailsRequestedEventArgs args)
    {
        _sessionDetailsWindow?.Close();
        SessionDetailsPopup.Show(
            this,
            args,
            PopupScale,
            Text,
            UnknownText,
            popup => _sessionDetailsWindow = popup,
            popup =>
            {
                if (ReferenceEquals(_sessionDetailsWindow, popup))
                {
                    _sessionDetailsWindow = null;
                }
            });
    }

    private void ShowResetCreditDetailsWindow() =>
        _resetCreditBankDialog.Show(ResetCreditDetailsText.Text);

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

        HotkeyCapturePopup.Show(
            this,
            target,
            targetButton,
            otherButton,
            PopupScale,
            Text,
            Brush,
            popup => _shortcutWindow = popup,
            popup =>
            {
                if (ReferenceEquals(_shortcutWindow, popup))
                {
                    _shortcutWindow = null;
                }
            });
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
            Text("Codex update details", "\uC0C9\uC778 \uC5C5\uB370\uC774\uD2B8 \uC0C1\uC138"),
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

    private void NavigateFromSideBar(Action showView)
    {
        showView();
        _dispatcher.TryEnqueue(() =>
        {
            WindowCloseBehavior.ActivateForInput(this);
            var scrollViewer = VisibleScrollViewer();
            if (scrollViewer is not null)
            {
                _ = scrollViewer.Focus(FocusState.Programmatic);
            }
        });
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

    private void CloseGaugeColorWindow(bool discardPendingColor = true)
    {
        if (discardPendingColor)
        {
            _previewGaugeColor = _selectedGaugeColor;
        }

        _gaugeColorPickerPopup.Close();
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
        _gaugeColorPickerPopup.Show(
            this,
            _previewGaugeColor,
            PopupScale,
            Text,
            Brush,
            onColorChanged: color =>
            {
                _previewGaugeColor = color;
                UpdateGaugeColorButton(_previewGaugeColor);
                ApplyStylePreview();
            },
            onApply: color =>
            {
                _previewGaugeColor = color;
                UpdateGaugeColorButton(_previewGaugeColor);
                ApplyStylePreview();
            },
            onClosed: () =>
            {
                UpdateGaugeColorButton(_previewGaugeColor);
                ApplyStylePreview();
            });
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
        if (GaugeThicknessComboBox?.SelectedItem is null
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
        _sidebarController.ApplyLanguage();
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
        SaveStyleButton.Content = Text("Save", "\uC800\uC7A5");
        _settingsController.ApplyLanguage(Text);
        ApplyStyleTooltips();
        UpdateSessionSortToggleAppearance();
    }

    private void ApplyStyleTooltips()
    {
        SetToolTip(Text("Return to the usage overview.", "\uC0AC\uC6A9\uB7C9 \uD654\uBA74\uC73C\uB85C \uB3CC\uC544\uAC00\uC694."), StyleTitleText);
        SetToolTip(Text(
            "Choose how thin or thick the gauge rings appear.",
            "\uAC8C\uC774\uC9C0 \uB9C1\uC758 \uB450\uAED8\uB97C \uC120\uD0DD\uD788\uC694."), GaugeThicknessLabelText, GaugeThicknessComboBox);
        SetToolTip(Text(
            "Choose the fill color used by the gauges.",
            "\uAC8C\uC774\uC9C0\uC5D0 \uC0AC\uC6A9\uD560 \uCC44\uC6B0\uAE30 \uC0C9\uC0C1\uC744 \uC120\uD0DD\uD788\uC694."), GaugeColorLabelText, GaugeColorButton);
        SetToolTip(Text(
            "Choose how the gauge fill animation behaves.",
            "\uAC8C\uC774\uC9C0 \uCC44\uC6B0\uAE30 \uC560\uB2C8\uBA54\uC774\uC158 \uBC29\uC2DD\uC744 \uC120\uD0DD\uD788\uC694."), GaugeAnimationLabelText, GaugeAnimationComboBox);
        SetToolTip(Text("Save the current style settings.", "\uD604\uC7AC \uC2A4\uD0C0\uC77C \uC124\uC815\uC744 \uC800\uC7A5\uD788\uC694."), SaveStyleButton);
    }

    private string CurrentLanguage => WindexBarConfig.NormalizeLanguage(_settingsStore.Config.Language);

    private bool IsKorean => CurrentLanguage == "ko";

    private string Text(string english, string korean) => IsKorean ? korean : english;

    private string UnknownText => Text("unknown", "\uC54C \uC218 \uC5C6\uC74C");

    private void ApplyWindowSectionLabels()
    {
        HudView.SetWindowLabels(
            Text("5 hours", "5\uC2DC\uAC04"),
            Text("Weekly", "\uC8FC\uAC04"));
        UpdateSessionSortToggleAppearance();
    }

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
            Text("5 hours", "5\uC2DC\uAC04"),
            Text("Weekly", "\uC8FC\uAC04"));
        HudView.SetFastTierAppearance(_isFastServiceTier);
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
        _resetCreditBankDialog.RenderRows(resetCredits);
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
