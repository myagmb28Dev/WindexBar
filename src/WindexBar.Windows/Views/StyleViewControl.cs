using WindexBar.Core.Config;
using WindexBar.Windows.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WindexBar.Windows.Views;

internal sealed class StyleViewControl : UserControl
{
    public StyleViewControl(Button quitButton)
    {
        var root = new Grid { RowSpacing = 8 };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = FeatureViewHelpers.CreateCard(root);

        ScrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Auto
        };
        root.Children.Add(ScrollViewer);

        var content = new StackPanel { Spacing = 10 };
        ScrollViewer.Content = content;

        TitleText = new TextBlock
        {
            Text = "Style",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        TitleText.PointerPressed += (_, args) =>
        {
            HomeRequested?.Invoke(this, EventArgs.Empty);
            args.Handled = true;
        };
        content.Children.Add(TitleText);
        content.Children.Add(FeatureViewHelpers.CreateDivider());

        PreviewGauge = HudViewControl.CreateGauge("style-preview", 68);
        PreviewGauge.Track.Margin = new Thickness(0, 4, 0, 5);
        content.Children.Add(PreviewGauge.Track);

        ThicknessComboBox = CreateComboBox(
            ("Thin", "thin"),
            ("Default", StyleConfig.DefaultGaugeThickness),
            ("Thick", "thick"));
        ThicknessLabelText = AddOptionRow(content, "Gauge thickness", ThicknessComboBox);
        ColorButton = new Button
        {
            MinWidth = 112,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        ColorLabelText = AddOptionRow(content, "Gauge color", ColorButton);
        AnimationComboBox = CreateComboBox(
            ("Smooth", StyleConfig.DefaultGaugeAnimation),
            ("Fast", "fast"),
            ("Off", "off"));
        AnimationLabelText = AddOptionRow(content, "Animation", AnimationComboBox);

        var buttons = new Grid { ColumnSpacing = 6 };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);
        SaveButton = FeatureViewHelpers.CreateCompactButton("Save");
        SaveButton.HorizontalAlignment = HorizontalAlignment.Left;
        buttons.Children.Add(SaveButton);
        quitButton.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(quitButton, 1);
        buttons.Children.Add(quitButton);
    }

    public event EventHandler? HomeRequested;
    public ScrollViewer ScrollViewer { get; }
    public TextBlock TitleText { get; }
    public TextBlock ThicknessLabelText { get; }
    public TextBlock ColorLabelText { get; }
    public TextBlock AnimationLabelText { get; }
    public ComboBox ThicknessComboBox { get; }
    public ComboBox AnimationComboBox { get; }
    public Button ColorButton { get; }
    public Button SaveButton { get; }
    public GaugeBar PreviewGauge { get; }

    private static ComboBox CreateComboBox(params (string Label, string Value)[] options)
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

    private static TextBlock AddOptionRow(StackPanel root, string label, FrameworkElement control)
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var labelText = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(labelText);
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        root.Children.Add(row);
        return labelText;
    }
}
