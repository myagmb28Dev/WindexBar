using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace WindexBar.Windows.UI;

public sealed class TransientScrollBarManager : IDisposable
{
    private const double KeyboardScrollStep = 36;
    private readonly List<DispatcherTimer> _scrollBarHideTimers = [];

    public void Attach(ScrollViewer scrollViewer)
    {
        scrollViewer.IsTabStop = true;
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

    public static void AttachPopupScrollInput(FrameworkElement root, ScrollViewer scrollViewer)
    {
        scrollViewer.IsTabStop = true;
        scrollViewer.PointerPressed += (_, _) => scrollViewer.Focus(FocusState.Pointer);
        scrollViewer.Loaded += (_, _) => scrollViewer.Focus(FocusState.Programmatic);
        root.AddHandler(
            UIElement.KeyDownEvent,
            new KeyEventHandler((_, e) =>
            {
                if (!e.Handled)
                {
                    HandleScrollNavigationKeyDown(scrollViewer, e);
                }
            }),
            true);
    }

    public static void HandleScrollNavigationKeyDown(
        ScrollViewer scrollViewer,
        KeyRoutedEventArgs e)
    {
        if (scrollViewer.ScrollableHeight <= 0
            || IsArrowKeyInputControl(e.OriginalSource as DependencyObject))
        {
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Down:
                ScrollBy(scrollViewer, KeyboardScrollStep);
                e.Handled = true;
                break;
            case VirtualKey.Up:
                ScrollBy(scrollViewer, -KeyboardScrollStep);
                e.Handled = true;
                break;
            case VirtualKey.PageDown:
                ScrollBy(scrollViewer, scrollViewer.ViewportHeight);
                e.Handled = true;
                break;
            case VirtualKey.PageUp:
                ScrollBy(scrollViewer, -scrollViewer.ViewportHeight);
                e.Handled = true;
                break;
            case VirtualKey.Home:
                ScrollTo(scrollViewer, 0);
                e.Handled = true;
                break;
            case VirtualKey.End:
                ScrollTo(scrollViewer, scrollViewer.ScrollableHeight);
                e.Handled = true;
                break;
        }
    }

    public static bool IsArrowKeyInputControl(DependencyObject? source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is TextBox or PasswordBox or RichEditBox or ComboBox or Slider or NumberBox or ButtonBase)
            {
                return true;
            }
        }

        return false;
    }

    public static void ScrollBy(ScrollViewer scrollViewer, double delta) =>
        ScrollTo(scrollViewer, scrollViewer.VerticalOffset + delta);

    public static void ScrollTo(ScrollViewer scrollViewer, double verticalOffset)
    {
        var targetOffset = Math.Clamp(verticalOffset, 0, Math.Max(0, scrollViewer.ScrollableHeight));
        scrollViewer.ChangeView(null, targetOffset, null, disableAnimation: false);
    }

    public void Dispose()
    {
        foreach (var timer in _scrollBarHideTimers)
        {
            timer.Stop();
        }
        _scrollBarHideTimers.Clear();
    }
}
