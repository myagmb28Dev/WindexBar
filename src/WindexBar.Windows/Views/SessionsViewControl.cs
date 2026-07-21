using WindexBar.Core.Config;
using WindexBar.Core.Presentation;
using WindexBar.Windows.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace WindexBar.Windows.Views;

internal sealed class SessionsViewControl : UserControl
{
    private readonly Dictionary<string, bool> _collapsedProjects = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<GaugeBar> _gauges = [];
    private bool _suppressSortEvent;

    public SessionsViewControl(Button quitButton)
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

        var content = new StackPanel { Spacing = 8 };
        ScrollViewer.Content = content;
        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TitleText = new TextBlock
        {
            Text = "Sessions",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        TitleText.PointerPressed += (_, args) =>
        {
            HomeRequested?.Invoke(this, EventArgs.Empty);
            args.Handled = true;
        };
        titleRow.Children.Add(TitleText);

        SortToggle = new ToggleButton
        {
            Width = 38,
            Height = 24,
            MinWidth = 38,
            MinHeight = 24,
            Padding = new Thickness(4, 0, 4, 0),
            FontSize = 10,
            IsChecked = true,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        SortToggle.Checked += (_, _) => RaiseSortChanged(true);
        SortToggle.Unchecked += (_, _) => RaiseSortChanged(false);
        Grid.SetColumn(SortToggle, 1);
        titleRow.Children.Add(SortToggle);
        content.Children.Add(titleRow);
        content.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(0, 0, 0, 2),
            Background = Brush(0x88, 0x7D, 0x62, 0xC7),
            HorizontalAlignment = HorizontalAlignment.Stretch
        });

        SessionPanel = new StackPanel { Spacing = 7 };
        content.Children.Add(SessionPanel);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        Grid.SetRow(buttons, 1);
        root.Children.Add(buttons);
        buttons.Children.Add(quitButton);
    }

    public event EventHandler? HomeRequested;
    public event EventHandler<bool>? SortPreferenceChanged;
    public event EventHandler<SessionDetailsRequestedEventArgs>? SessionDetailsRequested;

    public ScrollViewer ScrollViewer { get; }
    public TextBlock TitleText { get; }
    public ToggleButton SortToggle { get; }
    public StackPanel SessionPanel { get; }
    public IReadOnlyList<GaugeBar> Gauges => _gauges;

    public void SetLanguage(string title, bool projectSessionsFirst, string projectFirstTip, string nonProjectFirstTip)
    {
        TitleText.Text = title;
        _suppressSortEvent = true;
        SortToggle.IsChecked = projectSessionsFirst;
        _suppressSortEvent = false;
        SortToggle.Content = projectSessionsFirst ? "P↑" : "N↑";
        ToolTipService.SetToolTip(SortToggle, projectSessionsFirst ? projectFirstTip : nonProjectFirstTip);
    }

    public void Render(SessionListViewModel model, StyleConfig style)
    {
        SessionPanel.Children.Clear();
        _gauges.Clear();
        if (model.Projects.Count == 0)
        {
            SessionPanel.Children.Add(new TextBlock
            {
                Text = model.EmptyMessage,
                Foreground = Brush(0xFF, 0xB9, 0xA7, 0xE8),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        foreach (var project in model.Projects)
        {
            AddProject(project, model.ContextLabel, style);
        }
    }

    private void AddProject(SessionProjectViewModel project, string contextLabel, StyleConfig style)
    {
        var collapsed = _collapsedProjects.TryGetValue(project.Key, out var saved) ? saved : true;
        var headerContent = new Grid();
        headerContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerContent.Children.Add(new TextBlock
        {
            Text = $"{project.ProjectName} ({project.Sessions.Count})",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var chevron = new TextBlock
        {
            Text = collapsed ? "▸" : "▾",
            FontSize = 14,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush(0xFF, 0xB9, 0xA7, 0xE8)
        };
        Grid.SetColumn(chevron, 1);
        headerContent.Children.Add(chevron);

        var header = new Border
        {
            Padding = new Thickness(8, 6, 8, 6),
            Background = Brush(0x66, 0x35, 0x2E, 0x40),
            BorderBrush = Brush(0x55, 0x7D, 0x62, 0xC7),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Child = headerContent
        };
        var cards = new StackPanel
        {
            Spacing = 7,
            Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible
        };
        header.PointerPressed += (_, args) =>
        {
            var nextCollapsed = cards.Visibility == Visibility.Visible;
            cards.Visibility = nextCollapsed ? Visibility.Collapsed : Visibility.Visible;
            chevron.Text = nextCollapsed ? "▸" : "▾";
            _collapsedProjects[project.Key] = nextCollapsed;
            args.Handled = true;
        };

        var section = new StackPanel { Spacing = 6 };
        section.Children.Add(header);
        section.Children.Add(cards);
        SessionPanel.Children.Add(section);
        foreach (var session in project.Sessions)
        {
            cards.Children.Add(CreateSessionCard(project.ProjectName, session, contextLabel, style));
        }
    }

    private Border CreateSessionCard(
        string projectName,
        SessionCardViewModel session,
        string contextLabel,
        StyleConfig style)
    {
        var content = new StackPanel { Spacing = 3 };
        content.Children.Add(new TextBlock
        {
            Text = $"[{session.DisplayName}]",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.WrapWholeWords
        });
        var contextHeader = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        contextHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        contextHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        contextHeader.Children.Add(new TextBlock
        {
            Text = contextLabel,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        var percent = new TextBlock
        {
            Text = session.ContextPercentText,
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        Grid.SetColumn(percent, 1);
        contextHeader.Children.Add(percent);
        content.Children.Add(contextHeader);

        var gauge = HudViewControl.CreateGauge(session.SessionId, session.ContextPercent);
        GaugeAnimator.ApplyAppearance(gauge, style);
        _gauges.Add(gauge);
        content.Children.Add(gauge.Track);
        content.Children.Add(new TextBlock
        {
            Text = session.TokenDetails,
            FontSize = 11,
            Foreground = Brush(0xFF, 0xED, 0xE7, 0xFF),
            TextWrapping = TextWrapping.Wrap
        });
        var card = new Border
        {
            Padding = new Thickness(8, 6, 8, 6),
            Background = Brush(0x55, 0x45, 0x3A, 0x56),
            BorderBrush = Brush(0x66, 0x7D, 0x62, 0xC7),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = content
        };
        card.Tapped += (_, args) =>
        {
            SessionDetailsRequested?.Invoke(this, new SessionDetailsRequestedEventArgs(projectName, session));
            args.Handled = true;
        };
        return card;
    }

    private void RaiseSortChanged(bool projectFirst)
    {
        if (!_suppressSortEvent)
        {
            SortPreferenceChanged?.Invoke(this, projectFirst);
        }
    }

    private static SolidColorBrush Brush(byte a, byte r, byte g, byte b) =>
        new(global::Windows.UI.Color.FromArgb(a, r, g, b));
}

internal sealed class SessionDetailsRequestedEventArgs(
    string projectName,
    SessionCardViewModel session) : EventArgs
{
    public string ProjectName { get; } = projectName;
    public SessionCardViewModel Session { get; } = session;
}
