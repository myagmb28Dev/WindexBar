using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WindexBar.Windows.UI;
using WindexBar.Windows.Views;

namespace WindexBar.Windows.Dialogs;

public sealed class GaugeColorPickerPopup
{
    private Window? _window;
    private bool _isClosing;

    public Window? Window => _window;

    public void Show(
        Window ownerWindow,
        global::Windows.UI.Color currentColor,
        double popupScale,
        Func<string, string, string> text,
        Func<byte, byte, byte, byte, SolidColorBrush> brush,
        Action<global::Windows.UI.Color> onColorChanged,
        Action<global::Windows.UI.Color> onApply,
        Action onClosed)
    {
        if (_window is not null)
        {
            _window.Activate();
            return;
        }

        var brightnessValue = Math.Max(currentColor.R, Math.Max(currentColor.G, currentColor.B)) / 255d;
        var baseColor = NormalizeGaugeColorBrightness(currentColor);
        var candidateColor = currentColor;
        var initialPreviewColor = currentColor;
        _isClosing = false;

        void PreviewCandidateColor()
        {
            onColorChanged(candidateColor);
        }

        void CloseCandidateColor(bool keepCandidate)
        {
            if (_isClosing)
            {
                return;
            }

            _isClosing = true;
            if (keepCandidate)
            {
                PreviewCandidateColor();
                onApply(candidateColor);
            }
            else
            {
                onColorChanged(initialPreviewColor);
            }

            Close();
        }

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
                    ? brush(0xFF, 0xFF, 0xFF, 0xFF)
                    : brush(0x66, 0xFF, 0xFF, 0xFF);
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
                PreviewCandidateColor();
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
            PreviewCandidateColor();
        };

        var panel = new StackPanel { Width = 210, Spacing = 8 };
        panel.Children.Add(paletteGrid);
        panel.Children.Add(new TextBlock
        {
            Text = text("Brightness", "\uBC1D\uAE30"),
            FontSize = 11,
            Opacity = 0.75
        });
        panel.Children.Add(brightness);
        var applyButton = FeatureViewHelpers.CreateCompactButton(text("Apply", "\uC801\uC6A9"));
        applyButton.Click += (_, _) => CloseCandidateColor(keepCandidate: true);
        var closeButton = FeatureViewHelpers.CreateCompactButton(text("Close", "\uB2EB\uAE30"));
        closeButton.Click += (_, _) => CloseCandidateColor(keepCandidate: false);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 6
        };
        buttons.Children.Add(applyButton);
        buttons.Children.Add(closeButton);
        panel.Children.Add(buttons);

        var popup = OwnedPopupWindow.Create(
            ownerWindow,
            text("Gauge color", "\uAC8C\uC774\uC9C0 \uC0C9\uC0C1"),
            panel,
            popupScale,
            logicalWidth: 240,
            logicalHeight: 190,
            verticalOffset: 88,
            onDeactivated: () => CloseCandidateColor(keepCandidate: false));
        _window = popup;

        popup.Closed += (_, _) =>
        {
            var closedWithoutAction = ReferenceEquals(_window, popup) && !_isClosing;
            _window = null;
            if (closedWithoutAction)
            {
                onColorChanged(initialPreviewColor);
            }
            onClosed();
        };

        popup.Activate();
    }

    public void Close()
    {
        var popup = _window;
        _window = null;
        popup?.Close();
    }

    public static global::Windows.UI.Color NormalizeGaugeColorBrightness(global::Windows.UI.Color color)
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

    public static global::Windows.UI.Color ApplyGaugeBrightness(global::Windows.UI.Color color, double brightness)
    {
        var value = Math.Clamp(brightness, 0, 1);
        return global::Windows.UI.Color.FromArgb(
            0xFF,
            (byte)Math.Clamp(Math.Round(color.R * value), 0, 255),
            (byte)Math.Clamp(Math.Round(color.G * value), 0, 255),
            (byte)Math.Clamp(Math.Round(color.B * value), 0, 255));
    }
}
