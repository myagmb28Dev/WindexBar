using WindexBar.Core.Presentation;
using WindexBar.Windows.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WindexBar.Windows.Views;

internal sealed class HudViewControl : UserControl
{
    public HudViewControl(Button quitButton)
    {
        var rootBorder = new Border
        {
            Padding = new Thickness(11, 9, 11, 9),
            Background = Brush(0xFF, 0x1F, 0x1C, 0x24),
            BorderBrush = Brush(0x99, 0x7D, 0x62, 0xC7),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14)
        };
        Content = rootBorder;

        var root = new Grid { RowSpacing = 7 };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootBorder.Child = root;

        ScrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Auto
        };
        root.Children.Add(ScrollViewer);

        var content = new Grid { RowSpacing = 8 };
        for (var index = 0; index < 5; index++)
        {
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        ScrollViewer.Content = content;

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(header);

        HeaderText = new TextBlock
        {
            Text = "Codex",
            FontSize = 17,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            MaxLines = 2,
            TextWrapping = TextWrapping.WrapWholeWords
        };
        header.Children.Add(HeaderText);

        ModelPageText = new TextBlock
        {
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush(0xFF, 0xB9, 0xA7, 0xE8),
            FontSize = 11,
            TextAlignment = TextAlignment.Right,
            Visibility = Visibility.Collapsed
        };
        Grid.SetColumn(ModelPageText, 1);
        header.Children.Add(ModelPageText);

        MetaText = new TextBlock
        {
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
        Grid.SetRow(MetaText, 1);
        content.Children.Add(MetaText);

        ModelContentPanel = new Grid { RowSpacing = 4 };
        ModelContentTransform = new TranslateTransform();
        ModelContentPanel.RenderTransform = ModelContentTransform;
        for (var index = 0; index < 6; index++)
        {
            ModelContentPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        Grid.SetRow(ModelContentPanel, 2);
        content.Children.Add(ModelContentPanel);

        CurrentGauge = AddWindowSection(ModelContentPanel, 0, "current", out var currentLabel, out var currentPercent, out var currentDetail);
        CurrentLabelText = currentLabel;
        CurrentPercentText = currentPercent;
        CurrentDetailText = currentDetail;
        WeeklyGauge = AddWindowSection(ModelContentPanel, 3, "weekly", out var weeklyLabel, out var weeklyPercent, out var weeklyDetail);
        WeeklyLabelText = weeklyLabel;
        WeeklyPercentText = weeklyPercent;
        WeeklyDetailText = weeklyDetail;

        AccountText = AddLabelValueRow(content, 3, "Account", out var accountLabel);
        AccountLabelText = accountLabel;
        ErrorText = new TextBlock
        {
            Foreground = Brush(0xFF, 0xFF, 0x5F, 0x57),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(ErrorText, 4);
        content.Children.Add(ErrorText);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Spacing = 6
        };
        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);
        buttons.Children.Add(quitButton);
    }

    public ScrollViewer ScrollViewer { get; }
    public Grid ModelContentPanel { get; }
    public TranslateTransform ModelContentTransform { get; }
    public TextBlock HeaderText { get; }
    public TextBlock ModelPageText { get; }
    public TextBlock MetaText { get; }
    public TextBlock CurrentLabelText { get; }
    public TextBlock CurrentPercentText { get; }
    public TextBlock CurrentDetailText { get; }
    public TextBlock WeeklyLabelText { get; }
    public TextBlock WeeklyPercentText { get; }
    public TextBlock WeeklyDetailText { get; }
    public TextBlock AccountLabelText { get; }
    public TextBlock AccountText { get; }
    public TextBlock ErrorText { get; }
    public GaugeBar CurrentGauge { get; }
    public GaugeBar WeeklyGauge { get; }

    public void Bind(HudDisplayModel model, string currentLabel, string weeklyLabel)
    {
        HeaderText.Text = model.Header;
        MetaText.Text = model.Meta;
        MetaText.Visibility = string.IsNullOrWhiteSpace(model.Meta) ? Visibility.Collapsed : Visibility.Visible;
        AccountText.Text = model.Account;
        ErrorText.Text = model.Error;
        CurrentLabelText.Text = currentLabel;
        WeeklyLabelText.Text = weeklyLabel;
        CurrentPercentText.Text = model.Current.Percent;
        CurrentDetailText.Text = model.Current.Detail;
        WeeklyPercentText.Text = model.Weekly.Percent;
        WeeklyDetailText.Text = model.Weekly.Detail;
    }

    private static GaugeBar AddWindowSection(
        Grid root,
        int row,
        string key,
        out TextBlock label,
        out TextBlock percent,
        out TextBlock detail)
    {
        var header = new Grid { Margin = row == 0 ? new Thickness(0, 2, 0, 0) : new Thickness(0, 3, 0, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(header, row);
        root.Children.Add(header);

        label = new TextBlock
        {
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Brush(0xFF, 0xED, 0xE7, 0xFF)
        };
        header.Children.Add(label);
        percent = new TextBlock { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        Grid.SetColumn(percent, 1);
        header.Children.Add(percent);

        var gauge = CreateGauge(key);
        Grid.SetRow(gauge.Track, row + 1);
        root.Children.Add(gauge.Track);
        detail = new TextBlock
        {
            Foreground = Brush(0xFF, 0xC9, 0xC4, 0xD2),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(detail, row + 2);
        root.Children.Add(detail);
        return gauge;
    }

    internal static GaugeBar CreateGauge(string key, double target = 0)
    {
        var track = new Grid { Height = 6 };
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
        return new GaugeBar(key, track, fill, sweep, target);
    }

    private static TextBlock AddLabelValueRow(Grid root, int row, string label, out TextBlock labelText)
    {
        var grid = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(grid, row);
        root.Children.Add(grid);
        labelText = new TextBlock { Text = label, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        grid.Children.Add(labelText);
        var value = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        return value;
    }

    private static SolidColorBrush Brush(byte a, byte r, byte g, byte b) =>
        new(global::Windows.UI.Color.FromArgb(a, r, g, b));
}
