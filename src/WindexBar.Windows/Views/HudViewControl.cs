using WindexBar.Core.Presentation;
using WindexBar.Windows.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using static WindexBar.Windows.Views.FeatureViewHelpers;

namespace WindexBar.Windows.Views;

internal sealed class HudViewControl : UserControl
{
    public HudViewControl(Button quitButton, string versionText)
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
        for (var index = 0; index < 6; index++)
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

        VersionText = new TextBlock
        {
            Text = versionText,
            FontSize = 11,
            Foreground = Brush(0xFF, 0xB9, 0xA7, 0xE8),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        Grid.SetColumn(VersionText, 1);
        header.Children.Add(VersionText);

        var titleDivider = FeatureViewHelpers.CreateDivider();
        Grid.SetRow(titleDivider, 1);
        content.Children.Add(titleDivider);

        MetaText = new TextBlock
        {
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
        Grid.SetRow(MetaText, 2);
        content.Children.Add(MetaText);

        ModelContentPanel = new StackPanel { Spacing = 7 };
        Grid.SetRow(ModelContentPanel, 3);
        content.Children.Add(ModelContentPanel);

        CurrentGauge = AddWindowSection(ModelContentPanel, "current", out _currentSection, out var currentLabel, out var currentFastIndicator, out var currentFastGlow, out var currentPercent, out var currentDetail);
        CurrentLabelText = currentLabel;
        CurrentPercentText = currentPercent;
        CurrentDetailText = currentDetail;
        WeeklyGauge = AddWindowSection(ModelContentPanel, "weekly", out _weeklySection, out var weeklyLabel, out var weeklyFastIndicator, out var weeklyFastGlow, out var weeklyPercent, out var weeklyDetail);
        WeeklyLabelText = weeklyLabel;
        WeeklyPercentText = weeklyPercent;
        WeeklyDetailText = weeklyDetail;
        _fastIndicatorHosts = [currentFastIndicator, weeklyFastIndicator];
        _fastIndicatorGlows = [currentFastGlow, weeklyFastGlow];
        _fastIndicatorPulse = CreateFastIndicatorPulse(_fastIndicatorHosts, _fastIndicatorGlows);

        AccountText = AddLabelValueRow(content, 4, "Account", out var accountLabel);
        AccountLabelText = accountLabel;
        ErrorText = new TextBlock
        {
            Foreground = Brush(0xFF, 0xFF, 0x5F, 0x57),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(ErrorText, 5);
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
    public StackPanel ModelContentPanel { get; }
    public TextBlock HeaderText { get; }
    public TextBlock VersionText { get; }
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
    private readonly Grid[] _fastIndicatorHosts;
    private readonly TextBlock[] _fastIndicatorGlows;
    private readonly Grid _currentSection;
    private readonly Grid _weeklySection;
    private readonly Storyboard _fastIndicatorPulse;
    private bool _isFastTierAppearanceActive;

    public void Bind(HudDisplayModel model, string currentLabel, string weeklyLabel)
    {
        HeaderText.Text = model.Header;
        MetaText.Text = model.Meta;
        MetaText.Visibility = string.IsNullOrWhiteSpace(model.Meta) ? Visibility.Collapsed : Visibility.Visible;
        AccountText.Text = model.Account;
        ErrorText.Text = model.Error;
        SetWindowLabels(currentLabel, weeklyLabel);
        CurrentPercentText.Text = model.Current.Percent;
        CurrentDetailText.Text = model.Current.Detail;
        _currentSection.Visibility = model.Current.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        WeeklyPercentText.Text = model.Weekly.Percent;
        WeeklyDetailText.Text = model.Weekly.Detail;
        _weeklySection.Visibility = model.Weekly.IsVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetWindowLabels(string currentLabel, string weeklyLabel)
    {
        CurrentLabelText.Text = currentLabel;
        WeeklyLabelText.Text = weeklyLabel;
    }

    public void SetFastTierAppearance(bool isFastTier)
    {
        if (_isFastTierAppearanceActive == isFastTier)
        {
            return;
        }

        _isFastTierAppearanceActive = isFastTier;
        foreach (var indicator in _fastIndicatorHosts)
        {
            indicator.Visibility = isFastTier ? Visibility.Visible : Visibility.Collapsed;
        }

        if (isFastTier)
        {
            _fastIndicatorPulse.Begin();
            return;
        }

        _fastIndicatorPulse.Stop();
        foreach (var indicator in _fastIndicatorHosts)
        {
            indicator.Opacity = 0.58;
        }
        foreach (var glow in _fastIndicatorGlows)
        {
            glow.Opacity = 0.14;
        }
    }

    private static GaugeBar AddWindowSection(
        StackPanel root,
        string key,
        out Grid section,
        out TextBlock label,
        out Grid fastIndicator,
        out TextBlock fastGlow,
        out TextBlock percent,
        out TextBlock detail)
    {
        section = new Grid { RowSpacing = 4 };
        for (var index = 0; index < 3; index++)
        {
            section.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        root.Children.Add(section);

        var header = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        section.Children.Add(header);

        var labelHost = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4
        };
        header.Children.Add(labelHost);
        label = new TextBlock
        {
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Brush(0xFF, 0xED, 0xE7, 0xFF)
        };
        labelHost.Children.Add(label);
        fastIndicator = new Grid
        {
            Visibility = Visibility.Collapsed,
            Opacity = 0.58,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center
        };
        fastGlow = new TextBlock
        {
            Text = "\u26A1",
            FontSize = 14,
            Foreground = Brush(0xFF, 0xFF, 0xD6, 0x67),
            Opacity = 0.14,
            RenderTransformOrigin = new global::Windows.Foundation.Point(0.5, 0.5),
            RenderTransform = new ScaleTransform { ScaleX = 1.28, ScaleY = 1.28 }
        };
        fastIndicator.Children.Add(fastGlow);
        fastIndicator.Children.Add(new TextBlock
        {
            Text = "\u26A1",
            FontSize = 14,
            Foreground = Brush(0xFF, 0xFF, 0xB5, 0x3D)
        });
        labelHost.Children.Add(fastIndicator);
        percent = new TextBlock { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        Grid.SetColumn(percent, 1);
        header.Children.Add(percent);

        var gauge = CreateGauge(key);
        Grid.SetRow(gauge.Track, 1);
        section.Children.Add(gauge.Track);
        detail = new TextBlock
        {
            Foreground = Brush(0xFF, 0xC9, 0xC4, 0xD2),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(detail, 2);
        section.Children.Add(detail);
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

    private static Storyboard CreateFastIndicatorPulse(
        IReadOnlyList<Grid> indicators,
        IReadOnlyList<TextBlock> glows)
    {
        var pulse = new Storyboard
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };

        foreach (var indicator in indicators)
        {
            var fade = new DoubleAnimation
            {
                From = 0.58,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(1150)),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(fade, indicator);
            Storyboard.SetTargetProperty(fade, nameof(UIElement.Opacity));
            pulse.Children.Add(fade);
        }

        foreach (var glow in glows)
        {
            var bloom = new DoubleAnimation
            {
                From = 0.14,
                To = 0.52,
                Duration = new Duration(TimeSpan.FromMilliseconds(1150)),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(bloom, glow);
            Storyboard.SetTargetProperty(bloom, nameof(UIElement.Opacity));
            pulse.Children.Add(bloom);
        }

        return pulse;
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

}
