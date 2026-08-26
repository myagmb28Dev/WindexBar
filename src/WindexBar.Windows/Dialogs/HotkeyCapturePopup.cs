using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;
using WindexBar.Core.Config;
using WindexBar.Windows.UI;
using WindexBar.Windows.Views;

namespace WindexBar.Windows.Dialogs;

internal static class HotkeyCapturePopup
{
    public static Window Show(
        Window ownerWindow,
        ShortcutTarget target,
        Button targetButton,
        Button otherButton,
        double popupScale,
        Func<string, string, string> text,
        Func<byte, byte, byte, byte, SolidColorBrush> brush)
    {
        var candidate = HotkeyShortcut.NormalizeOrDefault(
            targetButton.Content as string,
            target == ShortcutTarget.ToggleWindow
                ? WindexBarConfig.DefaultToggleWindowHotkey
                : WindexBarConfig.DefaultToggleSidebarHotkey);

        var captureButton = new Button
        {
            Content = candidate,
            Height = 44,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            FontSize = 15
        };
        var status = new TextBlock
        {
            Text = text("Press a modifier and key.", "보조 키와 일반 키를 함께 눌러 주세요."),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            FontSize = 11
        };
        var applyButton = FeatureViewHelpers.CreateCompactButton(text("Apply", "적용"));
        var cancelButton = FeatureViewHelpers.CreateCompactButton(text("Close", "닫기"));
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 6
        };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(applyButton);
        var panel = new StackPanel { Width = 260, Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = target == ShortcutTarget.ToggleWindow
                ? text("Toggle shortcut", "토글 단축키")
                : text("Sidebar shortcut", "사이드바 단축키"),
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        panel.Children.Add(status);
        panel.Children.Add(captureButton);
        panel.Children.Add(buttons);

        var popup = OwnedPopupWindow.Create(
            ownerWindow,
            text("Shortcut", "단축키"),
            panel,
            popupScale,
            logicalWidth: 290,
            logicalHeight: 175);

        captureButton.KeyDown += (_, keyArgs) =>
        {
            var keyName = HotkeyKeyMapper.GetKeyName((uint)keyArgs.Key);
            if (keyName is null)
            {
                return;
            }

            var shortcut = new HotkeyShortcut(
                IsKeyDown(VirtualKey.Control),
                IsKeyDown(VirtualKey.Menu),
                IsKeyDown(VirtualKey.Shift),
                IsKeyDown(VirtualKey.LeftWindows) || IsKeyDown(VirtualKey.RightWindows),
                keyName);
            if (!shortcut.HasModifier)
            {
                status.Text = text("Include Ctrl, Alt, Shift, or Win.", "Ctrl, Alt, Shift 또는 Win 키를 포함해 주세요.");
                status.Foreground = brush(0xFF, 0xFF, 0x7B, 0x72);
                keyArgs.Handled = true;
                return;
            }

            candidate = shortcut.DisplayText;
            captureButton.Content = candidate;
            var conflicts = string.Equals(candidate, otherButton.Content as string, StringComparison.OrdinalIgnoreCase);
            status.Text = conflicts
                ? text("That shortcut is already used.", "이미 다른 기능에서 사용하는 단축키예요.")
                : text("Shortcut captured. Apply it below.", "단축키를 인식했어요. 아래에서 적용해 주세요.");
            status.Foreground = conflicts
                ? brush(0xFF, 0xFF, 0x7B, 0x72)
                : brush(0xFF, 0xB9, 0xA7, 0xE8);
            applyButton.IsEnabled = !conflicts;
            keyArgs.Handled = true;
        };

        applyButton.Click += (_, _) =>
        {
            targetButton.Content = candidate;
            popup.Close();
        };
        cancelButton.Click += (_, _) => popup.Close();
        captureButton.Loaded += (_, _) => captureButton.Focus(FocusState.Programmatic);
        popup.Activate();
        return popup;
    }

    private static bool IsKeyDown(VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;
}
