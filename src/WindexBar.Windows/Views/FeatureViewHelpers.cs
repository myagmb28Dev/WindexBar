using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WindexBar.Windows.Views;

internal static class FeatureViewHelpers
{
    public static Border CreateCard(UIElement content) => new()
    {
        Padding = new Thickness(11, 9, 11, 9),
        Background = Brush(0xFF, 0x1F, 0x1C, 0x24),
        BorderBrush = Brush(0x99, 0x7D, 0x62, 0xC7),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14),
        Child = content
    };

    public static Border CreateDivider() => new()
    {
        Height = 1,
        Margin = new Thickness(0, 0, 0, 2),
        Background = Brush(0x88, 0x7D, 0x62, 0xC7),
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    public static Button CreateCompactButton(object content) => new()
    {
        Content = content,
        MinWidth = 56,
        MinHeight = 26,
        Padding = new Thickness(9, 2, 9, 2),
        FontSize = 12
    };

    public static SolidColorBrush Brush(byte a, byte r, byte g, byte b) =>
        new(global::Windows.UI.Color.FromArgb(a, r, g, b));
}
