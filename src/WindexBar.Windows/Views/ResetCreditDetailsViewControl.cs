using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WindexBar.Windows.Views;

internal sealed class ResetCreditDetailsViewControl : UserControl
{
    public ResetCreditDetailsViewControl(Button quitButton)
    {
        var root = new Grid { RowSpacing = 10 };
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

        var grid = new Grid { RowSpacing = 10 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ScrollViewer.Content = grid;

        TitleText = new TextBlock
        {
            Text = "Credit details",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        TitleText.PointerPressed += (_, args) =>
        {
            HomeRequested?.Invoke(this, EventArgs.Empty);
            args.Handled = true;
        };
        grid.Children.Add(TitleText);
        var divider = FeatureViewHelpers.CreateDivider();
        Grid.SetRow(divider, 1);
        grid.Children.Add(divider);

        SummaryText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = FeatureViewHelpers.Brush(0xFF, 0xED, 0xE7, 0xFF)
        };
        Grid.SetRow(SummaryText, 2);
        grid.Children.Add(SummaryText);

        DetailsButton = FeatureViewHelpers.CreateCompactButton("View details");
        DetailsButton.HorizontalAlignment = HorizontalAlignment.Left;
        DetailsButton.Click += (_, _) => DetailsRequested?.Invoke(this, EventArgs.Empty);
        Grid.SetRow(DetailsButton, 3);
        grid.Children.Add(DetailsButton);

        DetailText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = FeatureViewHelpers.Brush(0xFF, 0xED, 0xE7, 0xFF)
        };
        quitButton.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetRow(quitButton, 1);
        root.Children.Add(quitButton);
    }

    public event EventHandler? HomeRequested;
    public event EventHandler? DetailsRequested;
    public ScrollViewer ScrollViewer { get; }
    public TextBlock TitleText { get; }
    public TextBlock SummaryText { get; }
    public TextBlock DetailText { get; }
    public Button DetailsButton { get; }
}
