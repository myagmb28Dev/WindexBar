using WindexBar.Core.Config;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WindexBar.Windows.Views;

internal sealed class SettingsViewControl : UserControl
{
    public SettingsViewControl(Button quitButton)
    {
        var root = new Grid { RowSpacing = 8 };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = FeatureViewHelpers.CreateCard(root);
        ScrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        root.Children.Add(ScrollViewer);

        var grid = new Grid { RowSpacing = 9 };
        for (var row = 0; row < 9; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        ScrollViewer.Content = grid;

        TitleText = new TextBlock
        {
            Text = "Settings",
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

        var intervalGrid = CreateTwoColumnRow(grid, 2, new GridLength(72), includeSuffix: true);
        RefreshIntervalLabelText = AddLabel(intervalGrid, "Refresh interval");
        RefreshIntervalSecondsTextBox = new TextBox { TextAlignment = TextAlignment.Right };
        Grid.SetColumn(RefreshIntervalSecondsTextBox, 1);
        intervalGrid.Children.Add(RefreshIntervalSecondsTextBox);
        SecondsLabelText = new TextBlock { Text = "s", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(SecondsLabelText, 2);
        intervalGrid.Children.Add(SecondsLabelText);

        var languageGrid = CreateTwoColumnRow(grid, 3, new GridLength(124));
        LanguageLabelText = AddLabel(languageGrid, "Language");
        LanguageComboBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 124 };
        LanguageComboBox.Items.Add(new ComboBoxItem { Content = "English", Tag = "en" });
        LanguageComboBox.Items.Add(new ComboBoxItem { Content = "한국어", Tag = "ko" });
        Grid.SetColumn(LanguageComboBox, 1);
        languageGrid.Children.Add(LanguageComboBox);

        var hotkeyGrid = CreateTwoColumnRow(grid, 4, new GridLength(124));
        ToggleHotkeyLabelText = AddLabel(hotkeyGrid, "Toggle shortcut");
        ToggleHotkeyButton = CreateShortcutButton();
        ToggleHotkeyButton.Click += (_, _) => ShortcutEditRequested?.Invoke(
            this,
            new ShortcutEditRequestedEventArgs(ShortcutTarget.ToggleWindow));
        Grid.SetColumn(ToggleHotkeyButton, 1);
        hotkeyGrid.Children.Add(ToggleHotkeyButton);

        var sidebarHotkeyGrid = CreateTwoColumnRow(grid, 5, new GridLength(124));
        ToggleSidebarHotkeyLabelText = AddLabel(sidebarHotkeyGrid, "Sidebar shortcut");
        ToggleSidebarHotkeyButton = CreateShortcutButton();
        ToggleSidebarHotkeyButton.Click += (_, _) => ShortcutEditRequested?.Invoke(
            this,
            new ShortcutEditRequestedEventArgs(ShortcutTarget.ToggleSidebar));
        Grid.SetColumn(ToggleSidebarHotkeyButton, 1);
        sidebarHotkeyGrid.Children.Add(ToggleSidebarHotkeyButton);

        StartWithWindowsCheckBox = new CheckBox { Content = "Start with Windows" };
        Grid.SetRow(StartWithWindowsCheckBox, 6);
        grid.Children.Add(StartWithWindowsCheckBox);
        AutoShowWithCodexCheckBox = new CheckBox { Content = "Show only while using Codex" };
        Grid.SetRow(AutoShowWithCodexCheckBox, 7);
        grid.Children.Add(AutoShowWithCodexCheckBox);

        var versionGrid = CreateTwoColumnRow(grid, 8, GridLength.Auto);
        CurrentCodexVersionText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        versionGrid.Children.Add(CurrentCodexVersionText);
        UpdateDetailsButton = FeatureViewHelpers.CreateCompactButton("Details");
        UpdateDetailsButton.Click += (_, _) => UpdateDetailsRequested?.Invoke(this, EventArgs.Empty);
        Grid.SetColumn(UpdateDetailsButton, 1);
        versionGrid.Children.Add(UpdateDetailsButton);

        var updateDetails = new StackPanel { Width = 270, Spacing = 10 };
        UpdateDetailsContent = updateDetails;
        UpdateDetailsVersionText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = FeatureViewHelpers.Brush(0xFF, 0xED, 0xE7, 0xFF)
        };
        updateDetails.Children.Add(UpdateDetailsVersionText);
        CheckCodexVersionButton = FeatureViewHelpers.CreateCompactButton("Check now");
        CheckCodexVersionButton.HorizontalAlignment = HorizontalAlignment.Left;
        updateDetails.Children.Add(CheckCodexVersionButton);

        var installGrid = new Grid { ColumnSpacing = 8 };
        installGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        installGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(124) });
        updateDetails.Children.Add(installGrid);
        CodexInstallMethodLabelText = AddLabel(installGrid, "Codex install method");
        CodexInstallMethodComboBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 124 };
        AddInstallMethod("Auto-detect", CodexInstallMethodNames.Auto);
        AddInstallMethod("PowerShell", CodexInstallMethodNames.PowerShell);
        AddInstallMethod("npm", CodexInstallMethodNames.Npm);
        AddInstallMethod("Bun", CodexInstallMethodNames.Bun);
        AddInstallMethod("Homebrew", CodexInstallMethodNames.Homebrew);
        AddInstallMethod("WinGet", CodexInstallMethodNames.WinGet);
        AddInstallMethod("Custom", CodexInstallMethodNames.Custom);
        Grid.SetColumn(CodexInstallMethodComboBox, 1);
        installGrid.Children.Add(CodexInstallMethodComboBox);

        CodexUpdateCommandPreviewText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72,
            FontSize = 11
        };
        updateDetails.Children.Add(CodexUpdateCommandPreviewText);
        CustomCodexUpdateCommandLabelText = new TextBlock
        {
            Text = "Custom update command",
            TextWrapping = TextWrapping.Wrap
        };
        updateDetails.Children.Add(CustomCodexUpdateCommandLabelText);
        CustomCodexUpdateCommandTextBox = new TextBox
        {
            PlaceholderText = "Enter a Codex CLI update command.",
            TextWrapping = TextWrapping.Wrap
        };
        updateDetails.Children.Add(CustomCodexUpdateCommandTextBox);
        UpdateDetailsSaveHintText = new TextBlock
        {
            Text = "Apply returns changes to Settings. Save stores them permanently.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72,
            FontSize = 11
        };
        updateDetails.Children.Add(UpdateDetailsSaveHintText);
        UpdateDetailsApplyButton = FeatureViewHelpers.CreateCompactButton("Apply");
        UpdateDetailsCloseButton = FeatureViewHelpers.CreateCompactButton("Close");
        var updateButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 6
        };
        updateButtons.Children.Add(UpdateDetailsApplyButton);
        updateButtons.Children.Add(UpdateDetailsCloseButton);
        updateDetails.Children.Add(updateButtons);

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
    public event EventHandler<ShortcutEditRequestedEventArgs>? ShortcutEditRequested;
    public event EventHandler? UpdateDetailsRequested;
    public ScrollViewer ScrollViewer { get; }
    public TextBlock TitleText { get; }
    public TextBlock RefreshIntervalLabelText { get; }
    public TextBlock SecondsLabelText { get; }
    public TextBlock LanguageLabelText { get; }
    public TextBlock ToggleHotkeyLabelText { get; }
    public TextBlock ToggleSidebarHotkeyLabelText { get; }
    public TextBox RefreshIntervalSecondsTextBox { get; }
    public Button ToggleHotkeyButton { get; }
    public Button ToggleSidebarHotkeyButton { get; }
    public ComboBox LanguageComboBox { get; }
    public CheckBox StartWithWindowsCheckBox { get; }
    public CheckBox AutoShowWithCodexCheckBox { get; }
    public TextBlock CurrentCodexVersionText { get; }
    public Button UpdateDetailsButton { get; }
    public Button CheckCodexVersionButton { get; }
    public TextBlock CodexInstallMethodLabelText { get; }
    public ComboBox CodexInstallMethodComboBox { get; }
    public TextBlock CodexUpdateCommandPreviewText { get; }
    public TextBlock CustomCodexUpdateCommandLabelText { get; }
    public TextBox CustomCodexUpdateCommandTextBox { get; }
    public StackPanel UpdateDetailsContent { get; }
    public TextBlock UpdateDetailsVersionText { get; }
    public TextBlock UpdateDetailsSaveHintText { get; }
    public Button UpdateDetailsApplyButton { get; }
    public Button UpdateDetailsCloseButton { get; }
    public Button SaveButton { get; }

    private void AddInstallMethod(string label, string value) =>
        CodexInstallMethodComboBox.Items.Add(new ComboBoxItem { Content = label, Tag = value });

    private static Grid CreateTwoColumnRow(Grid parent, int row, GridLength secondWidth, bool includeSuffix = false)
    {
        var result = new Grid { ColumnSpacing = 8 };
        result.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        result.ColumnDefinitions.Add(new ColumnDefinition { Width = secondWidth });
        if (includeSuffix)
        {
            result.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }
        Grid.SetRow(result, row);
        parent.Children.Add(result);
        return result;
    }

    private static TextBlock AddLabel(Grid row, string text)
    {
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(label);
        return label;
    }

    private static Button CreateShortcutButton() => new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Right,
        MinWidth = 124,
        Padding = new Thickness(8, 3, 8, 3)
    };
}

internal enum ShortcutTarget
{
    ToggleWindow,
    ToggleSidebar
}

internal sealed class ShortcutEditRequestedEventArgs(ShortcutTarget target) : EventArgs
{
    public ShortcutTarget Target { get; } = target;
}
