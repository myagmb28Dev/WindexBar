using WindexBar.Core.Config;
using WindexBar.Core.Models;
using WindexBar.Core.Refresh;
using WindexBar.Windows.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WindexBar.Windows.Controllers;

internal sealed class SettingsController
{
    private readonly SettingsViewControl _view;
    private readonly SettingsStore _settingsStore;
    private readonly UsageStore _usageStore;
    private readonly CodexUpdateController _codexUpdateController;
    private readonly Action _showHud;

    public SettingsController(
        SettingsViewControl view,
        SettingsStore settingsStore,
        UsageStore usageStore,
        CodexUpdateController codexUpdateController,
        Action showHud)
    {
        _view = view;
        _settingsStore = settingsStore;
        _usageStore = usageStore;
        _codexUpdateController = codexUpdateController;
        _showHud = showHud;
        _view.AutoShowWithCodexCheckBox.Checked += OnAutoShowChanged;
        _view.AutoShowWithCodexCheckBox.Unchecked += OnAutoShowChanged;
        _view.SaveButton.Click += OnSaveClicked;
    }

    public void Load()
    {
        _view.RefreshIntervalSecondsTextBox.Text = _settingsStore.Codex.RefreshIntervalSeconds.ToString();
        _view.ToggleHotkeyButton.Content = _settingsStore.Config.Hotkeys.ToggleWindow;
        _view.ToggleSidebarHotkeyButton.Content = _settingsStore.Config.Hotkeys.ToggleSidebar;
        _view.StartWithWindowsCheckBox.IsChecked = _settingsStore.Config.StartWithWindows;
        _view.AutoShowWithCodexCheckBox.IsChecked = _settingsStore.Config.AutoShowWithCodex;
        SelectLanguage(_settingsStore.Config.Language);
        ApplyAutoShowShortcutState();
        _codexUpdateController.Load();
    }

    public void ApplyLanguage(Func<string, string, string> text)
    {
        _view.TitleText.Text = text("Settings", "설정");
        _view.RefreshIntervalLabelText.Text = text("Refresh interval", "새로고침 간격");
        _view.SecondsLabelText.Text = text("s", "초");
        _view.LanguageLabelText.Text = text("Language", "언어");
        _view.ToggleHotkeyLabelText.Text = text("Toggle shortcut", "토글 단축키");
        _view.ToggleSidebarHotkeyLabelText.Text = text("Sidebar shortcut", "사이드바 단축키");
        _view.StartWithWindowsCheckBox.Content = text("Start with Windows", "Windows 시작 시 실행");
        _view.AutoShowWithCodexCheckBox.Content = text(
            "Show only while using ChatGPT or Codex",
            "ChatGPT 또는 Codex 사용 중에만 표시");
        _view.UpdateDetailsButton.Content = text("Details", "상세");
        _view.UpdateDetailsApplyButton.Content = text("Apply", "적용");
        _view.UpdateDetailsCloseButton.Content = text("Close", "닫기");
        _view.UpdateDetailsSaveHintText.Text = text(
            "Apply returns changes to Settings. Save stores them permanently.",
            "적용은 설정 화면에 반영하고, 저장은 영구 저장해요.");
        _view.SaveButton.Content = text("Save", "저장");
        _codexUpdateController.ApplyLanguage();
    }

    private void OnSaveClicked(object sender, RoutedEventArgs args)
    {
        _settingsStore.Update(config =>
        {
            var codex = config.GetProviderConfig(UsageProvider.Codex);
            codex.RefreshIntervalSeconds = ReadRefreshIntervalSeconds();
            config.SetProviderConfig(codex);
            config.Language = ReadSelectedLanguage();
            config.Hotkeys.ToggleWindow = HotkeyShortcut.NormalizeOrDefault(
                _view.ToggleHotkeyButton.Content as string,
                WindexBarConfig.DefaultToggleWindowHotkey);
            config.Hotkeys.ToggleSidebar = HotkeyShortcut.NormalizeOrDefault(
                _view.ToggleSidebarHotkeyButton.Content as string,
                WindexBarConfig.DefaultToggleSidebarHotkey);
            config.StartWithWindows = _view.StartWithWindowsCheckBox.IsChecked == true;
            config.AutoShowWithCodex = _view.AutoShowWithCodexCheckBox.IsChecked == true;
            _codexUpdateController.ApplySettings(config);
        });
        StartupShortcutService.Apply(_settingsStore.Config.StartWithWindows);
        if (_codexUpdateController.CanRefreshUsage)
        {
            _usageStore.StartBackgroundRefresh();
        }
        _showHud();
    }

    private void OnAutoShowChanged(object sender, RoutedEventArgs args) => ApplyAutoShowShortcutState();

    private void ApplyAutoShowShortcutState()
    {
        var enabled = _view.AutoShowWithCodexCheckBox.IsChecked == true;
        _view.ToggleHotkeyButton.IsEnabled = !enabled;
        _view.ToggleHotkeyButton.Opacity = enabled ? 0.45 : 1;
        _view.ToggleHotkeyLabelText.Opacity = enabled ? 0.65 : 1;
    }

    private int ReadRefreshIntervalSeconds()
    {
        if (!int.TryParse(_view.RefreshIntervalSecondsTextBox.Text, out var value))
        {
            return WindexBarConfig.DefaultRefreshIntervalSeconds;
        }
        return Math.Clamp(value, WindexBarConfig.MinRefreshIntervalSeconds, WindexBarConfig.MaxRefreshIntervalSeconds);
    }

    private string ReadSelectedLanguage() =>
        _view.LanguageComboBox.SelectedItem is ComboBoxItem { Tag: string language }
            ? WindexBarConfig.NormalizeLanguage(language)
            : WindexBarConfig.DefaultLanguage;

    private void SelectLanguage(string? language)
    {
        var normalized = WindexBarConfig.NormalizeLanguage(language);
        _view.LanguageComboBox.SelectedItem = _view.LanguageComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag is string value
                && WindexBarConfig.NormalizeLanguage(value) == normalized);
        _view.LanguageComboBox.SelectedIndex = _view.LanguageComboBox.SelectedItem is null
            ? 0
            : _view.LanguageComboBox.SelectedIndex;
    }
}
