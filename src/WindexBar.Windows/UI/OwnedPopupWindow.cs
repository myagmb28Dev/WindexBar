using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace WindexBar.Windows.UI;

internal static class OwnedPopupWindow
{
    public static Window Create(
        Window owner,
        string title,
        FrameworkElement content,
        double rasterizationScale,
        double logicalWidth,
        double logicalHeight,
        int verticalOffset = 72)
    {
        var popup = new Window { Title = title };
        popup.Content = new Border
        {
            Padding = new Thickness(12),
            Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0xFF, 0x1F, 0x1C, 0x24)),
            BorderBrush = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0x99, 0x7D, 0x62, 0xC7)),
            BorderThickness = new Thickness(1),
            Child = content
        };
        OwnedWindowBehavior.Attach(popup, owner);
        popup.AppWindow.IsShownInSwitchers = false;
        if (popup.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
        }

        var popupWidth = (int)Math.Ceiling(logicalWidth * rasterizationScale);
        var popupHeight = (int)Math.Ceiling(logicalHeight * rasterizationScale);
        popup.AppWindow.ResizeClient(new SizeInt32(popupWidth, popupHeight));
        PositionNextToOwner(owner, popup, popupWidth, popupHeight, verticalOffset);

        var hasActivated = false;
        popup.Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated && hasActivated)
            {
                popup.Close();
                return;
            }

            hasActivated = true;
        };
        return popup;
    }

    public static void DetachContent(Window popup)
    {
        if (popup.Content is Border border)
        {
            border.Child = null;
        }
    }

    private static void PositionNextToOwner(
        Window owner,
        Window popup,
        int popupWidth,
        int popupHeight,
        int verticalOffset)
    {
        var displayArea = DisplayArea.GetFromWindowId(owner.AppWindow.Id, DisplayAreaFallback.Nearest);
        var workArea = displayArea.WorkArea;
        var popupX = owner.AppWindow.Position.X + owner.AppWindow.Size.Width + 8;
        if (popupX + popupWidth > workArea.X + workArea.Width)
        {
            popupX = owner.AppWindow.Position.X - popupWidth - 8;
        }

        popupX = Math.Clamp(popupX, workArea.X, workArea.X + workArea.Width - popupWidth);
        var popupY = Math.Clamp(
            owner.AppWindow.Position.Y + verticalOffset,
            workArea.Y,
            workArea.Y + workArea.Height - popupHeight);
        popup.AppWindow.Move(new PointInt32(popupX, popupY));
    }
}
