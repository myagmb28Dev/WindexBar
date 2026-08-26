using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using WindexBar.Windows.UI;
using WindexBar.Windows.Views;

namespace WindexBar.Windows.Dialogs;

internal static class SessionDetailsPopup
{
    public static Window Show(
        Window ownerWindow,
        SessionDetailsRequestedEventArgs args,
        double popupScale,
        Func<string, string, string> text,
        string unknownText)
    {
        var session = args.Session;
        var detailsPanel = new StackPanel { Spacing = 9 };
        detailsPanel.Children.Add(new TextBlock
        {
            Text = session.DisplayName,
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        detailsPanel.Children.Add(FeatureViewHelpers.CreateDivider());
        AddPopupDetail(detailsPanel, text("Project", "\uD504\uB85C\uC81D\uD2B8"), args.ProjectName);
        AddPopupDetail(detailsPanel, text("Path", "\uACBD\uB85C"), session.ProjectPath ?? unknownText);
        AddPopupDetail(detailsPanel, text("Session ID", "\uC138\uC158 ID"), session.SessionId);
        AddPopupDetail(detailsPanel, text("Context", "\uCEE8\uD14D\uC2A4\uD2B8"), session.ContextPercentText);
        AddPopupDetail(detailsPanel, text("Token usage", "\uD1A0\uD070 \uC0AC\uC6A9\uB7C9"), session.DetailedTokenDetails);
        AddPopupDetail(
            detailsPanel,
            text("Last activity", "\uB9C8\uC9C0\uB9C9 \uD65C\uB3D9"),
            session.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));

        var scrollViewer = new ScrollViewer
        {
            Height = 260,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Auto,
            Content = detailsPanel
        };

        var copyButton = FeatureViewHelpers.CreateCompactButton(text("Copy details", "\uC0C1\uC138 \uBCF5\uC0AC"));
        copyButton.Click += (_, _) =>
        {
            CopyText(string.Join(
                Environment.NewLine,
                $"{text("Project", "\uD504\uB85C\uC81D\uD2B8")}: {args.ProjectName}",
                $"{text("Path", "\uACBD\uB85C")}: {session.ProjectPath ?? unknownText}",
                $"{text("Session ID", "\uC138\uC158 ID")}: {session.SessionId}",
                $"{text("Context", "\uCEE8\uD14D\uC2A4\uD2B8")}: {session.ContextPercentText}",
                session.DetailedTokenDetails,
                $"{text("Last activity", "\uB9C8\uC9C0\uB9C9 \uD65C\uB3D9")}: {session.UpdatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}"));
            ShowCopiedFeedback(copyButton, text);
        };
        var closeButton = FeatureViewHelpers.CreateCompactButton(text("Close", "\uB2EB\uAE30"));
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 6
        };
        buttons.Children.Add(copyButton);
        buttons.Children.Add(closeButton);
        var panel = new Grid { Width = 300, RowSpacing = 9 };
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.Children.Add(scrollViewer);
        Grid.SetRow(buttons, 1);
        panel.Children.Add(buttons);
        TransientScrollBarManager.AttachPopupScrollInput(panel, scrollViewer);

        var popup = OwnedPopupWindow.Create(
            ownerWindow,
            text("Session details", "\uC138\uC158 \uC0C1\uC138"),
            panel,
            popupScale,
            logicalWidth: 330,
            logicalHeight: 340);

        closeButton.Click += (_, _) => popup.Close();
        popup.Activate();
        return popup;
    }

    private static void AddPopupDetail(StackPanel panel, string label, string value)
    {
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Opacity = 0.7
        });
        panel.Children.Add(new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        });
    }

    public static void CopyText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    public static void ShowCopiedFeedback(Button button, Func<string, string, string> text)
    {
        var originalContent = button.Content;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        button.Content = text("Copied!", "\uBCF5\uC0AC\uB428!");
        button.IsEnabled = false;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            button.Content = originalContent;
            button.IsEnabled = true;
        };
        timer.Start();
    }
}
