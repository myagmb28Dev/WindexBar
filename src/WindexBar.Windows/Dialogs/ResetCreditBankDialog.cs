using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WindexBar.Core.Formatting;
using WindexBar.Core.Models;
using WindexBar.Core.Presentation;
using WindexBar.Core.Providers.Codex;
using WindexBar.Core.Refresh;
using WindexBar.Windows.UI;
using WindexBar.Windows.Views;

namespace WindexBar.Windows.Dialogs;

public sealed class ResetCreditBankDialog
{
    private readonly Window _ownerWindow;
    private readonly UsageStore _usageStore;
    private readonly RateLimitResetCreditRedemptionCoordinator _redemptionCoordinator;
    private readonly Func<double> _popupScale;
    private readonly Func<string, string, string> _text;
    private readonly Func<string> _currentLanguage;
    private readonly Func<byte, byte, byte, byte, SolidColorBrush> _brush;
    private readonly Func<bool> _isCodexEnabled;
    private readonly CancellationToken _cancellationToken;

    private Window? _window;
    private Button? _closeButton;
    private StackPanel? _redemptionRows;
    private RateLimitResetCredit? _pendingResetCredit;
    private bool _isRedemptionInProgress;
    private bool _isDialogOpen;

    public ResetCreditBankDialog(
        Window ownerWindow,
        UsageStore usageStore,
        RateLimitResetCreditRedemptionCoordinator redemptionCoordinator,
        Func<double> popupScale,
        Func<string, string, string> text,
        Func<string> currentLanguage,
        Func<byte, byte, byte, byte, SolidColorBrush> brush,
        Func<bool> isCodexEnabled,
        CancellationToken cancellationToken)
    {
        _ownerWindow = ownerWindow;
        _usageStore = usageStore;
        _redemptionCoordinator = redemptionCoordinator;
        _popupScale = popupScale;
        _text = text;
        _currentLanguage = currentLanguage;
        _brush = brush;
        _isCodexEnabled = isCodexEnabled;
        _cancellationToken = cancellationToken;
    }

    public Window? Window => _window;

    public void Show(string detailsText)
    {
        if (_window is not null)
        {
            _window.Activate();
            return;
        }

        var creditRows = new StackPanel { Spacing = 8 };
        _redemptionRows = creditRows;
        var scrollViewer = new ScrollViewer
        {
            Height = 245,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = creditRows
        };
        var panel = new StackPanel { Width = 330, Spacing = 9 };
        panel.Children.Add(new TextBlock
        {
            Text = _text("Reset credit bank", "\uB9AC\uC14B \uD06C\uB808\uB527 \uBC45\uD06C"),
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        panel.Children.Add(FeatureViewHelpers.CreateDivider());
        panel.Children.Add(scrollViewer);
        var copyButton = FeatureViewHelpers.CreateCompactButton(_text("Copy details", "\uC0C1\uC138 \uBCF5\uC0AC"));
        copyButton.Click += (_, _) =>
        {
            SessionDetailsPopup.CopyText(detailsText);
            SessionDetailsPopup.ShowCopiedFeedback(copyButton, _text);
        };
        var closeButton = FeatureViewHelpers.CreateCompactButton(_text("Close", "\uB2EB\uAE30"));
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 6
        };
        buttons.Children.Add(copyButton);
        buttons.Children.Add(closeButton);
        _closeButton = closeButton;
        panel.Children.Add(buttons);
        TransientScrollBarManager.AttachPopupScrollInput(panel, scrollViewer);

        Window popup = null!;
        popup = OwnedPopupWindow.Create(
            _ownerWindow,
            _text("Reset credit bank", "\uB9AC\uC14B \uD06C\uB808\uB527 \uBC45\uD06C"),
            panel,
            _popupScale(),
            logicalWidth: 360,
            logicalHeight: 370,
            onDeactivated: () =>
            {
                if (!_isRedemptionInProgress && !_isDialogOpen)
                {
                    popup.Close();
                }
            });
        _window = popup;
        RenderRows(_usageStore.Snapshot?.RateLimitResetCredits);
        closeButton.Click += (_, _) => popup.Close();
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, popup))
            {
                _window = null;
                _closeButton = null;
                _redemptionRows = null;
            }
        };
        popup.Activate();
    }

    public void Close()
    {
        var popup = _window;
        _window = null;
        _closeButton = null;
        _redemptionRows = null;
        popup?.Close();
    }

    public void RenderRows(RateLimitResetCreditsSnapshot? resetCredits)
    {
        if (_redemptionRows is null)
        {
            return;
        }

        _redemptionRows.Children.Clear();
        var credits = resetCredits?.Credits ?? [];
        var hasPendingAttempt = _redemptionCoordinator.HasPendingAttempt;
        var pendingCreditId = _pendingResetCredit?.Id;

        for (var index = 0; index < credits.Count; index++)
        {
            AddResetCreditRedemptionRow(
                credits[index],
                index + 1,
                hasPendingAttempt,
                pendingCreditId);
        }

        if (credits.Count == 0 && resetCredits is { AvailableCount: > 0 })
        {
            AddResetCreditRedemptionRow(
                credit: null,
                ordinal: 1,
                hasPendingAttempt,
                pendingCreditId);
        }
        else if (resetCredits is null)
        {
            _redemptionRows.Children.Add(new TextBlock
            {
                Text = _text("unknown", "\uC54C \uC218 \uC5C6\uC74C"),
                Foreground = _brush(0xFF, 0xED, 0xE7, 0xFF)
            });
        }
        else if (resetCredits.AvailableCount <= 0)
        {
            _redemptionRows.Children.Add(new TextBlock
            {
                Text = _text("No reset credits held", "\uBCF4\uC720 \uB9AC\uC14B \uD06C\uB808\uB527 \uC5C6\uC74C"),
                Foreground = _brush(0xFF, 0xED, 0xE7, 0xFF)
            });
        }

        if (_closeButton is not null)
        {
            _closeButton.IsEnabled = !_isRedemptionInProgress;
        }
    }

    private void AddResetCreditRedemptionRow(
        RateLimitResetCredit? credit,
        int ordinal,
        bool hasPendingAttempt,
        string? pendingCreditId)
    {
        if (_redemptionRows is null)
        {
            return;
        }

        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = credit is null
            ? _text("Next available reset credit", "\uB2E4\uC74C \uC0AC\uC6A9 \uAC00\uB2A5 \uB9AC\uC14B \uD06C\uB808\uB527")
            : _text($"Reset credit {ordinal}", $"\uB9AC\uC14B \uD06C\uB808\uB527 {ordinal}");
        var description = RateLimitResetCreditFormatter.FormatRedemptionTarget(credit, _currentLanguage());
        row.Children.Add(new TextBlock
        {
            Text = string.Join(Environment.NewLine, title, description),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Consolas"),
            Foreground = _brush(0xFF, 0xED, 0xE7, 0xFF)
        });

        var isPendingCredit = hasPendingAttempt
            && string.Equals(credit?.Id, pendingCreditId, StringComparison.Ordinal);
        var isActiveCredit = string.Equals(credit?.Id, _pendingResetCredit?.Id, StringComparison.Ordinal);
        var useButton = FeatureViewHelpers.CreateCompactButton(
            _isRedemptionInProgress && isActiveCredit
                ? _text("Using...", "\uC0AC\uC6A9 \uC911...")
                : isPendingCredit
                    ? _text("Retry", "\uB2E4\uC2DC \uC2DC\uB3C4")
                    : _text("Use", "\uC0AC\uC6A9"));
        useButton.HorizontalAlignment = HorizontalAlignment.Right;
        useButton.VerticalAlignment = VerticalAlignment.Center;
        useButton.IsEnabled = !_isRedemptionInProgress
            && _isCodexEnabled()
            && resetCreditsAvailable()
            && (!hasPendingAttempt || isPendingCredit);
        useButton.Click += async (_, _) => await ConfirmAndRedeemResetCreditAsync(credit);
        FeatureViewHelpers.SetToolTip(
            _text(
                "Uses this credit only when Codex has an eligible rate limit to reset.",
                "Codex\uC5D0 \uCD08\uAE30\uD654 \uAC00\uB2A5\uD55C \uC0AC\uC6A9 \uC81C\uD55C\uC774 \uC788\uC744 \uB54C\uB9CC \uC774 \uD06C\uB808\uB527\uC744 \uC0AC\uC6A9\uD574."),
            useButton);
        Grid.SetColumn(useButton, 1);
        row.Children.Add(useButton);
        _redemptionRows.Children.Add(row);

        bool resetCreditsAvailable() =>
            _usageStore.Snapshot?.RateLimitResetCredits is { AvailableCount: > 0 };
    }

    private async Task ConfirmAndRedeemResetCreditAsync(RateLimitResetCredit? requestedCredit)
    {
        if (_isRedemptionInProgress)
        {
            return;
        }

        var resetCredits = _usageStore.Snapshot?.RateLimitResetCredits;
        if (resetCredits is null || resetCredits.AvailableCount <= 0)
        {
            RenderRows(resetCredits);
            return;
        }

        var isRetry = _redemptionCoordinator.HasPendingAttempt;
        var targetCredit = isRetry
            ? _pendingResetCredit
            : requestedCredit;
        var targetDescription = RateLimitResetCreditFormatter.FormatRedemptionTarget(targetCredit, _currentLanguage());
        if (!await ShowConfirmationAsync(isRetry, targetDescription))
        {
            return;
        }

        _isRedemptionInProgress = true;
        _pendingResetCredit = targetCredit;
        RenderRows(resetCredits);
        RateLimitResetCreditRedemptionAttempt? attempt = null;
        try
        {
            attempt = await _redemptionCoordinator.RedeemAsync(
                creditId: targetCredit?.Id,
                _cancellationToken);
            if (attempt.IsCompleted)
            {
                _pendingResetCredit = null;
                await _usageStore.RefreshAsync(_cancellationToken);
            }
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
            return;
        }
        finally
        {
            _isRedemptionInProgress = false;
            RenderRows(_usageStore.Snapshot?.RateLimitResetCredits);
        }

        if (attempt is null || _window is null)
        {
            return;
        }

        var display = RateLimitResetCreditRedemptionDisplayModelFactory.Create(attempt, _currentLanguage());
        await ShowResultAsync(display);
    }

    private async Task<bool> ShowConfirmationAsync(bool isRetry, string targetDescription)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var panel = new StackPanel { Width = 300, Spacing = 12 };
        panel.Children.Add(CreateDialogHeader(
            isRetry ? "\u21bb" : "\u26a1",
            isRetry
                ? _text("Retry reset credit", "\uB9AC\uC14B \uD06C\uB808\uB527 \uB2E4\uC2DC \uC2DC\uB3C4")
                : _text("Use reset credit", "\uB9AC\uC14B \uD06C\uB808\uB527 \uC0AC\uC6A9")));
        panel.Children.Add(FeatureViewHelpers.CreateDivider());
        panel.Children.Add(CreateTargetCard(targetDescription));
        panel.Children.Add(new TextBlock
        {
            Text = isRetry
                ? _text(
                    "The same credit will be retried safely. Another credit cannot be consumed.",
                    "\uAC19\uC740 \uD06C\uB808\uB527\uB9CC \uC548\uC804\uD558\uAC8C \uB2E4\uC2DC \uC2DC\uB3C4\uD574. \uB2E4\uB978 \uD06C\uB808\uB527\uC774 \uC18C\uBE44\uB418\uC9C0\uB294 \uC54A\uC544.")
                : _text(
                    "This resets an eligible Codex rate limit and cannot be undone.",
                    "\uC801\uC6A9 \uAC00\uB2A5\uD55C Codex \uC0AC\uC6A9 \uC81C\uD55C\uC744 \uCD08\uAE30\uD654\uD558\uBA70, \uC774 \uC791\uC5C5\uC740 \uB418\uB3CC\uB9AD \uC218 \uC5C6\uC544."),
            TextWrapping = TextWrapping.Wrap,
            Foreground = _brush(0xFF, 0xC9, 0xC1, 0xD5),
            FontSize = 12
        });

        var cancelButton = FeatureViewHelpers.CreateCompactButton(_text("Cancel", "\uCDE8\uC18C"));
        var confirmButton = FeatureViewHelpers.CreateCompactButton(
            isRetry ? _text("Retry", "\uB2E4\uC2DC \uC2DC\uB3C4") : _text("Use credit", "\uD06C\uB808\uB527 \uC0AC\uC6A9"));
        confirmButton.Background = _brush(0xFF, 0x6F, 0x55, 0xB5);
        confirmButton.Foreground = _brush(0xFF, 0xFF, 0xFF, 0xFF);
        confirmButton.BorderBrush = _brush(0xFF, 0x9B, 0x84, 0xD6);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 7
        };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(confirmButton);
        panel.Children.Add(buttons);

        Window popup = null!;
        void Complete(bool confirmed)
        {
            if (completion.TrySetResult(confirmed))
            {
                popup.Close();
            }
        }

        _isDialogOpen = true;
        popup = OwnedPopupWindow.Create(
            _ownerWindow,
            _text("Use reset credit", "\uB9AC\uC14B \uD06C\uB808\uB527 \uC0AC\uC6A9"),
            panel,
            _popupScale(),
            logicalWidth: 330,
            logicalHeight: 245,
            verticalOffset: 102,
            onDeactivated: () => Complete(false));
        cancelButton.Click += (_, _) => Complete(false);
        confirmButton.Click += (_, _) => Complete(true);
        popup.Closed += (_, _) => completion.TrySetResult(false);
        popup.Activate();
        try
        {
            return await completion.Task;
        }
        finally
        {
            _isDialogOpen = false;
        }
    }

    private async Task ShowResultAsync(RateLimitResetCreditRedemptionDisplayModel display)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var panel = new StackPanel { Width = 300, Spacing = 12 };
        panel.Children.Add(CreateDialogHeader(display.IsSuccess ? "\u2713" : "!", display.Title));
        panel.Children.Add(FeatureViewHelpers.CreateDivider());
        panel.Children.Add(new Border
        {
            Padding = new Thickness(12),
            Background = _brush(0xFF, 0x29, 0x25, 0x31),
            BorderBrush = display.IsSuccess
                ? _brush(0xAA, 0x65, 0xB5, 0x91)
                : _brush(0xAA, 0xB6, 0x83, 0x63),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = new ScrollViewer
            {
                MaxHeight = 118,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new TextBlock
                {
                    Text = display.Message,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = _brush(0xFF, 0xED, 0xE7, 0xFF)
                }
            }
        });
        var closeButton = FeatureViewHelpers.CreateCompactButton(_text("Close", "\uB2EB\uAE30"));
        closeButton.HorizontalAlignment = HorizontalAlignment.Right;
        panel.Children.Add(closeButton);

        Window popup = null!;
        void Complete()
        {
            if (completion.TrySetResult())
            {
                popup.Close();
            }
        }

        _isDialogOpen = true;
        popup = OwnedPopupWindow.Create(
            _ownerWindow,
            display.Title,
            panel,
            _popupScale(),
            logicalWidth: 330,
            logicalHeight: 255,
            verticalOffset: 112,
            onDeactivated: Complete);
        closeButton.Click += (_, _) => Complete();
        popup.Closed += (_, _) => completion.TrySetResult();
        popup.Activate();
        try
        {
            await completion.Task;
        }
        finally
        {
            _isDialogOpen = false;
        }
    }

    private FrameworkElement CreateDialogHeader(string glyph, string title)
    {
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 9
        };
        header.Children.Add(new Border
        {
            Width = 30,
            Height = 30,
            Background = _brush(0xFF, 0x38, 0x2F, 0x4A),
            BorderBrush = _brush(0xFF, 0x8D, 0x72, 0xD2),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(15),
            Child = new TextBlock
            {
                Text = glyph,
                FontSize = 15,
                Foreground = _brush(0xFF, 0xC7, 0xB6, 0xFF),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = _brush(0xFF, 0xF7, 0xF3, 0xFF)
        });
        return header;
    }

    private FrameworkElement CreateTargetCard(string targetDescription)
    {
        var content = new StackPanel { Spacing = 5 };
        content.Children.Add(new TextBlock
        {
            Text = _text("SELECTED RESET CREDIT", "\uC120\uD0DD\uD55C \uB9AC\uC14B \uD06C\uB808\uB527"),
            FontSize = 10,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = _brush(0xFF, 0xAD, 0x96, 0xEA)
        });
        content.Children.Add(new TextBlock
        {
            Text = targetDescription,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"),
            Foreground = _brush(0xFF, 0xF2, 0xED, 0xFF)
        });
        return new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            Background = _brush(0xFF, 0x29, 0x25, 0x31),
            BorderBrush = _brush(0x99, 0x7D, 0x62, 0xC7),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = content
        };
    }
}
