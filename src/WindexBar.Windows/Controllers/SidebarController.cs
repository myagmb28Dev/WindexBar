using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WindexBar.Core.Config;

namespace WindexBar.Windows.Controllers;

public sealed class SidebarController : IDisposable
{
    private const double TitleBarClientHeight = 34;
    private const double SideBarVisualWidth = 51;
    private const double SideBarInternalHoverWidth = 18;
    private const double SideBarBottomMargin = 10;
    private const int SideBarHoverHideDelayMilliseconds = 500;
    private const double ModeLockNoticeWidth = 220;
    private const int ModeLockNoticeDurationMilliseconds = 1800;
    private const double SideBarButtonHeight = 32;
    private const double SideBarButtonSpacing = 7;
    private const int SideBarButtonCount = 6;
    private const double SideBarNaturalHeight =
        (SideBarButtonHeight * SideBarButtonCount) + (SideBarButtonSpacing * (SideBarButtonCount - 1));

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    private readonly Window _window;
    private readonly FrameworkElement _rootLayout;
    private readonly SettingsStore _settingsStore;
    private readonly Func<string, string, string> _text;
    private readonly Func<byte, byte, byte, byte, SolidColorBrush> _brush;

    private readonly Grid _sideBarPanel;
    private readonly Popup _sideBarPopup;
    private readonly Popup _modeLockNoticePopup;
    private readonly TextBlock _modeLockNoticeTitleText;
    private readonly TextBlock _modeLockNoticeMessageText;

    private readonly DispatcherTimer _sideBarHoverHideTimer;
    private readonly DispatcherTimer _sideBarHoverPollTimer;
    private readonly DispatcherTimer _modeLockNoticeHideTimer;

    private bool _isSideBarPinned;
    private bool _isSideBarHoverVisible;
    private bool _isPointerOverSideBarHoverRegion;

    public Button HomeButton { get; }
    public Button SessionsButton { get; }
    public Button CreditsButton { get; }
    public Button ResetCreditDetailsButton { get; }
    public Button StyleButton { get; }
    public Button SettingsButton { get; }

    public SidebarController(
        Window window,
        FrameworkElement rootLayout,
        SettingsStore settingsStore,
        Func<string, string, string> text,
        Func<byte, byte, byte, byte, SolidColorBrush> brush)
    {
        _window = window;
        _rootLayout = rootLayout;
        _settingsStore = settingsStore;
        _text = text;
        _brush = brush;

        _sideBarHoverHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SideBarHoverHideDelayMilliseconds) };
        _sideBarHoverHideTimer.Tick += SideBarHoverHideTimer_Tick;
        _sideBarHoverPollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _sideBarHoverPollTimer.Tick += (_, _) => PollSideBarHoverRegion();

        _sideBarPanel = new Grid
        {
            RowSpacing = SideBarButtonSpacing,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = SideBarVisualWidth,
            Height = SideBarNaturalHeight,
            Opacity = 0.7
        };
        for (var row = 0; row < SideBarButtonCount; row++)
        {
            _sideBarPanel.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star)
            });
        }
        _sideBarPopup = new Popup
        {
            Child = _sideBarPanel,
            HorizontalOffset = -SideBarVisualWidth,
            VerticalOffset = TitleBarClientHeight,
            ShouldConstrainToRootBounds = false
        };

        _modeLockNoticeTitleText = new TextBlock
        {
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        _modeLockNoticeMessageText = new TextBlock
        {
            FontSize = 11,
            Opacity = 0.82,
            TextWrapping = TextWrapping.Wrap
        };
        var modeLockNoticePanel = new StackPanel { Spacing = 5 };
        modeLockNoticePanel.Children.Add(_modeLockNoticeTitleText);
        modeLockNoticePanel.Children.Add(_modeLockNoticeMessageText);
        _modeLockNoticePopup = new Popup
        {
            Child = new Border
            {
                Width = ModeLockNoticeWidth,
                IsHitTestVisible = false,
                Padding = new Thickness(10, 8, 10, 9),
                Background = _brush(0xD9, 0x2A, 0x27, 0x31),
                BorderBrush = _brush(0xB3, 0x9A, 0x7C, 0xDE),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Child = modeLockNoticePanel
            },
            IsLightDismissEnabled = false
        };
        _modeLockNoticeHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(ModeLockNoticeDurationMilliseconds)
        };
        _modeLockNoticeHideTimer.Tick += (_, _) =>
        {
            _modeLockNoticeHideTimer.Stop();
            _modeLockNoticePopup.IsOpen = false;
        };

        HomeButton = CreateSideBarButton("\u2302");
        Grid.SetRow(HomeButton, 0);
        _sideBarPanel.Children.Add(HomeButton);

        SessionsButton = CreateSideBarButton("\u2637");
        Grid.SetRow(SessionsButton, 1);
        _sideBarPanel.Children.Add(SessionsButton);

        CreditsButton = CreateSideBarButton("$");
        Grid.SetRow(CreditsButton, 2);
        _sideBarPanel.Children.Add(CreditsButton);

        ResetCreditDetailsButton = CreateSideBarButton("\u21BB");
        Grid.SetRow(ResetCreditDetailsButton, 3);
        _sideBarPanel.Children.Add(ResetCreditDetailsButton);

        StyleButton = CreateSideBarButton("\u25C8");
        Grid.SetRow(StyleButton, 4);
        _sideBarPanel.Children.Add(StyleButton);

        SettingsButton = CreateSideBarButton("\u2699");
        Grid.SetRow(SettingsButton, 5);
        _sideBarPanel.Children.Add(SettingsButton);
    }

    public bool IsSideBarHoverRevealEnabled => _settingsStore.Config.Sidebar.ShowOnHover;

    public bool IsSideBarVisible => _isSideBarPinned || _isSideBarHoverVisible;

    public void ApplySideBarLayout()
    {
        var isVisible = IsSideBarVisible;
        UpdateSideBarHeight();
        if (_sideBarPopup.XamlRoot is null && _rootLayout.XamlRoot is not null)
        {
            _sideBarPopup.XamlRoot = _rootLayout.XamlRoot;
        }

        var isWindowVisible = WindowCloseBehavior.IsVisible(_window);
        var shouldOpenSideBar = isVisible && isWindowVisible;
        if (_sideBarPopup.IsOpen != shouldOpenSideBar)
        {
            _sideBarPopup.IsOpen = shouldOpenSideBar;
        }

        if (IsSideBarHoverRevealEnabled && isWindowVisible)
        {
            _sideBarHoverPollTimer.Start();
        }
        else
        {
            _sideBarHoverPollTimer.Stop();
            _isPointerOverSideBarHoverRegion = false;
        }
    }

    public void UpdateSideBarHeight()
    {
        var availableHeight = Math.Max(0, _rootLayout.ActualHeight - TitleBarClientHeight - SideBarBottomMargin);
        var sideBarHeight = Math.Min(SideBarNaturalHeight, availableHeight);
        _sideBarPanel.Height = sideBarHeight;
    }

    public void ToggleSideBar()
    {
        if (IsSideBarHoverRevealEnabled)
        {
            ShowModeLockedNotice(
                _text("Sidebar toggle locked", "\uC0AC\uC774\uB4DC\uBC14 \uD1A0\uAE00 \uC7A0\uAE40"),
                _text(
                    "Sidebar hover reveal is enabled. Move the pointer to the sidebar edge to use it.",
                    "\uC0AC\uC774\uB4DC\uBC14 \uD638\uBC84 \uD45C\uC2DC\uAC00 \uCF1C\uC838 \uC788\uC5B4\uC694. \uC0AC\uC774\uB4DC\uBC14 \uAC00\uC7A5\uC790\uB9AC\uC5D0 \uB9C8\uC6B0\uC2A4\uB97C \uC62C\uB824 \uC0AC\uC6A9\uD574 \uC8FC\uC138\uC694."));
            return;
        }

        _isSideBarPinned = !_isSideBarPinned;
        _isSideBarHoverVisible = false;
        _sideBarHoverHideTimer.Stop();
        ApplySideBarLayout();
    }

    public void ApplySideBarHoverPreference()
    {
        if (IsSideBarHoverRevealEnabled)
        {
            _isSideBarPinned = false;
            _isSideBarHoverVisible = _isPointerOverSideBarHoverRegion;
            _sideBarHoverHideTimer.Stop();
            ApplySideBarLayout();
            return;
        }

        _sideBarHoverHideTimer.Stop();
        _sideBarHoverPollTimer.Stop();
        _isSideBarHoverVisible = false;
        _isPointerOverSideBarHoverRegion = false;
        ApplySideBarLayout();
    }

    public void OnWindowVisibilityChanged(bool isVisible)
    {
        if (!isVisible)
        {
            _sideBarHoverHideTimer.Stop();
            _sideBarHoverPollTimer.Stop();
            _isPointerOverSideBarHoverRegion = false;
            _isSideBarHoverVisible = false;
        }

        ApplySideBarLayout();
    }

    private void PollSideBarHoverRegion()
    {
        if (!IsSideBarHoverRevealEnabled || !WindowCloseBehavior.IsVisible(_window))
        {
            _sideBarHoverPollTimer.Stop();
            _isPointerOverSideBarHoverRegion = false;
            return;
        }

        if (!GetCursorPos(out var cursor))
        {
            return;
        }

        var scale = _rootLayout.XamlRoot?.RasterizationScale ?? 1;
        var externalWidth = (int)Math.Ceiling(SideBarVisualWidth * scale);
        var internalWidth = (int)Math.Ceiling(SideBarInternalHoverWidth * scale);
        var sideBarHeight = (int)Math.Ceiling(_sideBarPanel.Height * scale);
        var clientHeight = (int)Math.Ceiling(_rootLayout.ActualHeight * scale);
        var windowLeft = _window.AppWindow.Position.X;
        var left = windowLeft - externalWidth;
        var top = _window.AppWindow.Position.Y + (int)Math.Ceiling(TitleBarClientHeight * scale);
        var isOverExternalRegion = cursor.X >= left
            && cursor.X < windowLeft
            && cursor.Y >= top
            && cursor.Y < top + sideBarHeight;
        var isOverInternalRegion = cursor.X >= windowLeft
            && cursor.X < windowLeft + internalWidth
            && cursor.Y >= top
            && cursor.Y < _window.AppWindow.Position.Y + clientHeight;
        var isOverHoverRegion = isOverExternalRegion || isOverInternalRegion;

        if (_isPointerOverSideBarHoverRegion == isOverHoverRegion)
        {
            return;
        }

        _isPointerOverSideBarHoverRegion = isOverHoverRegion;
        if (isOverHoverRegion)
        {
            _sideBarHoverHideTimer.Stop();
            if (!_isSideBarPinned && !_isSideBarHoverVisible)
            {
                _isSideBarHoverVisible = true;
                ApplySideBarLayout();
            }

            return;
        }

        StartSideBarHoverHideTimer();
    }

    private void StartSideBarHoverHideTimer()
    {
        if (!_isSideBarPinned && IsSideBarHoverRevealEnabled)
        {
            _sideBarHoverHideTimer.Start();
        }
    }

    private void SideBarHoverHideTimer_Tick(object? sender, object args)
    {
        _sideBarHoverHideTimer.Stop();
        if (_isSideBarPinned || _isPointerOverSideBarHoverRegion || !IsSideBarHoverRevealEnabled || !_isSideBarHoverVisible)
        {
            return;
        }

        _isSideBarHoverVisible = false;
        ApplySideBarLayout();
    }

    public void ShowModeLockedNotice(string title, string message)
    {
        if (_modeLockNoticePopup.XamlRoot is null && _rootLayout.XamlRoot is not null)
        {
            _modeLockNoticePopup.XamlRoot = _rootLayout.XamlRoot;
        }

        if (_modeLockNoticePopup.XamlRoot is null)
        {
            return;
        }

        _modeLockNoticeTitleText.Text = title;
        _modeLockNoticeMessageText.Text = message;
        _modeLockNoticePopup.HorizontalOffset = Math.Max(
            12,
            Math.Round((_rootLayout.ActualWidth - ModeLockNoticeWidth) / 2));
        _modeLockNoticePopup.VerticalOffset = TitleBarClientHeight + 12;
        _modeLockNoticePopup.IsOpen = true;
        _modeLockNoticeHideTimer.Stop();
        _modeLockNoticeHideTimer.Start();
    }

    public void CloseModeLockedNotice()
    {
        _modeLockNoticeHideTimer.Stop();
        _modeLockNoticePopup.IsOpen = false;
    }

    public void ApplyLanguage()
    {
        SetSideBarButtonText(HomeButton, "\u2302");
        SetSideBarButtonText(SessionsButton, "\u2637");
        SetSideBarButtonText(CreditsButton, "$");
        SetSideBarButtonText(StyleButton, "\u25C8");
        SetSideBarButtonText(SettingsButton, "\u2699");
        SetSideBarButtonText(ResetCreditDetailsButton, "\u21BB");
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

    public void Dispose()
    {
        _sideBarHoverHideTimer.Stop();
        _sideBarHoverPollTimer.Stop();
        _modeLockNoticeHideTimer.Stop();
        _sideBarPopup.IsOpen = false;
        _modeLockNoticePopup.IsOpen = false;
    }
}
