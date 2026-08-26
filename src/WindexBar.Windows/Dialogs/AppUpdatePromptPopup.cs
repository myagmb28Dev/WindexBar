using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindexBar.Core.Updates;
using WindexBar.Windows.UI;
using WindexBar.Windows.Views;

namespace WindexBar.Windows.Dialogs;

public static class AppUpdatePromptPopup
{
    public static Task<bool> PromptAsync(
        Window ownerWindow,
        AppVersion version,
        double popupScale,
        Func<string, string, string> text,
        Action<Window?> onWindowCreated,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = text(
                $"WindexBar {version} is ready to install.",
                $"WindexBar {version} \uC5C5\uB370\uC774\uD2B8\uB97C \uC124\uCE58\uD560 \uC218 \uC788\uC5B4\uC694."),
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = text(
                "WindexBar will close, install the update, and restart. Your settings will be preserved.",
                "\uC9C0\uAE08 \uC5C5\uB370\uC774\uD2B8\uD558\uBA74 WindexBar\uAC00 \uC885\uB8CC\uB41C \uB4A4 \uC124\uCE58\uB418\uACE0 \uB2E4\uC2DC \uC2DC\uC791\uB3FC\uC694. \uC124\uC815\uC740 \uADF8\uB300\uB85C \uC720\uC9C0\uB3FC\uC694."),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            FontSize = 12
        });

        var updateButton = FeatureViewHelpers.CreateCompactButton(text("Update now", "\uC9C0\uAE08 \uC5C5\uB370\uC774\uD2B8"));
        var laterButton = FeatureViewHelpers.CreateCompactButton(text("Later", "\uB098\uC911\uC5D0"));
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 6
        };
        buttons.Children.Add(updateButton);
        buttons.Children.Add(laterButton);
        panel.Children.Add(buttons);

        var popup = OwnedPopupWindow.Create(
            ownerWindow,
            text("WindexBar update available", "WindexBar \uC5C5\uB370\uC774\uD2B8"),
            panel,
            popupScale,
            logicalWidth: 330,
            logicalHeight: 205);

        onWindowCreated(popup);

        updateButton.Click += (_, _) =>
        {
            completion.TrySetResult(true);
            popup.Close();
        };
        laterButton.Click += (_, _) => popup.Close();
        popup.Closed += (_, _) =>
        {
            completion.TrySetResult(false);
            onWindowCreated(null);
        };

        var dispatcher = ownerWindow.DispatcherQueue;
        var cancellationRegistration = cancellationToken.Register(() =>
            dispatcher.TryEnqueue(() => popup.Close()));
        _ = completion.Task.ContinueWith(
            _ => cancellationRegistration.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        popup.Activate();
        return completion.Task;
    }
}
