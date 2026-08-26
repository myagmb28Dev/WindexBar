using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace WindexBar.Windows.Views;

internal sealed class TitleBarControl : Grid
{
    public TextBlock TitleText { get; }
    public Grid DragRegion { get; }
    public Button MinimizeButton { get; }
    public Button ZoomButton { get; }

    public TitleBarControl(
        PointerEventHandler onTitlePressed,
        RoutedEventHandler onMinimizeClicked,
        RoutedEventHandler onZoomClicked,
        Func<byte, byte, byte, byte, SolidColorBrush> brush)
    {
        Background = brush(0, 0, 0, 0);
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });

        TitleText = new TextBlock
        {
            Text = "WindexBar",
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = brush(0xFF, 0xB9, 0xA7, 0xE8)
        };
        TitleText.PointerPressed += onTitlePressed;
        Children.Add(TitleText);

        DragRegion = new Grid { Background = brush(0, 0, 0, 0) };
        SetColumn(DragRegion, 1);
        Children.Add(DragRegion);

        var windowButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            Spacing = 7
        };
        SetColumn(windowButtons, 2);
        Children.Add(windowButtons);

        MinimizeButton = CreateTitleButton(brush(0xFF, 0xFF, 0xBD, 0x2E), onMinimizeClicked);
        ZoomButton = CreateTitleButton(brush(0xFF, 0x28, 0xC8, 0x40), onZoomClicked);
        windowButtons.Children.Add(MinimizeButton);
        windowButtons.Children.Add(ZoomButton);
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
}
