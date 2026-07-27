using WindexBar.Core.Config;
using WindexBar.Core.Models;
using WindexBar.Core.Refresh;
using WindexBar.Windows.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static WindexBar.Windows.Views.FeatureViewHelpers;

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
        _view.SidebarHoverRevealCheckBox.Checked += OnSidebarHoverRevealChanged;
        _view.SidebarHoverRevealCheckBox.Unchecked += OnSidebarHoverRevealChanged;
        _view.SaveButton.Click += OnSaveClicked;
    }

    public void Load()
    {
        _view.RefreshIntervalSecondsTextBox.Text = _settingsStore.Codex.RefreshIntervalSeconds.ToString();
        _view.ToggleHotkeyButton.Content = _settingsStore.Config.Hotkeys.ToggleWindow;
        _view.ToggleSidebarHotkeyButton.Content = _settingsStore.Config.Hotkeys.ToggleSidebar;
        _view.StartWithWindowsCheckBox.IsChecked = _settingsStore.Config.StartWithWindows;
        _view.AutoShowWithCodexCheckBox.IsChecked = _settingsStore.Config.AutoShowWithCodex;
        _view.RateLimitAlertsCheckBox.IsChecked = _settingsStore.Config.RateLimitAlerts.Enabled;
        _view.SidebarHoverRevealCheckBox.IsChecked = _settingsStore.Config.Sidebar.ShowOnHover;
        SelectLanguage(_settingsStore.Config.Language);
        ApplyAutoShowShortcutState();
        ApplySidebarHoverShortcutState();
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
        _view.RateLimitAlertsCheckBox.Content = text(
            "Alert at 80% and 90% usage",
            "사용량 80%와 90%에서 알림");
        _view.SidebarHoverRevealCheckBox.Content = text(
            "Show sidebar on hover",
            "마우스 호버 시 사이드바 표시");
        _view.UpdateDetailsButton.Content = text("Details", "상세");
        _view.UpdateDetailsApplyButton.Content = text("Apply", "적용");
        _view.UpdateDetailsCloseButton.Content = text("Close", "닫기");
        _view.UpdateDetailsSaveHintText.Text = text(
            "Apply returns changes to Settings. Save stores them permanently.",
            "적용은 설정 화면에 반영하고, 저장은 영구 저장해요.");
        _view.SaveButton.Content = text("Save", "저장");
        _codexUpdateController.ApplyLanguage();
        ApplyOptionTooltips(text);
    }

    private void ApplyOptionTooltips(Func<string, string, string> text)
    {
        SetToolTip(text(
            "Return to the usage overview.",
            "사용량 화면으로 돌아가요."), _view.TitleText);
        SetToolTip(text(
            "How often WindexBar refreshes Codex usage.",
            "Codex 사용량을 새로고침하는 주기예요."),
            _view.RefreshIntervalLabelText,
            _view.RefreshIntervalSecondsTextBox);
        SetToolTip(text(
            "Choose the language used by WindexBar.",
            "WindexBar에 표시할 언어를 선택해요."),
            _view.LanguageLabelText,
            _view.LanguageComboBox);
        SetToolTip(text(
            "Show or hide WindexBar manually. Locked while automatic Codex visibility is enabled.",
            "WindexBar를 직접 표시하거나 숨기는 단축키예요. Codex 자동 표시 중에는 잠겨요."),
            _view.ToggleHotkeyLabelText,
            _view.ToggleHotkeyButton);
        SetToolTip(text(
            "Show or hide the pinned sidebar. Locked while sidebar hover reveal is enabled.",
            "고정 사이드바를 표시하거나 숨기는 단축키예요. 호버 표시 중에는 잠겨요."),
            _view.ToggleSidebarHotkeyLabelText,
            _view.ToggleSidebarHotkeyButton);
        SetToolTip(text(
            "Launch WindexBar when you sign in to Windows.",
            "Windows에 로그인할 때 WindexBar를 실행해요."), _view.StartWithWindowsCheckBox);
        SetToolTip(text(
            "Show WindexBar only while ChatGPT Desktop or Codex is active. This locks the window toggle.",
            "ChatGPT Desktop 또는 Codex 사용 중에만 WindexBar를 표시해요. 창 토글은 잠겨요."),
            _view.AutoShowWithCodexCheckBox);
        SetToolTip(text(
            "Show a tray alert at 80% and 90% current or weekly usage.",
            "현재 또는 주간 사용량이 80%, 90%에 도달하면 트레이 알림을 표시해요."),
            _view.RateLimitAlertsCheckBox);
        SetToolTip(text(
            "Reveal the sidebar while the pointer is over its edge. This locks the sidebar toggle.",
            "마우스를 사이드바 가장자리에 올리면 표시해요. 사이드바 토글은 잠겨요."),
            _view.SidebarHoverRevealCheckBox);
        SetToolTip(text(
            "Shows the detected Codex CLI version and update status.",
            "감지한 Codex CLI 버전과 업데이트 상태를 표시해요."), _view.CurrentCodexVersionText);
        SetToolTip(text(
            "View the Codex update method and check for updates.",
            "Codex 업데이트 방법을 확인하고 업데이트를 검사해요."), _view.UpdateDetailsButton);
        SetToolTip(text(
            "Check the installed and latest Codex CLI versions.",
            "설치된 Codex CLI와 최신 버전을 확인해요."), _view.CheckCodexVersionButton);
        SetToolTip(text(
            "Choose how WindexBar runs Codex CLI updates.",
            "WindexBar가 Codex CLI 업데이트를 실행하는 방법을 선택해요."),
            _view.CodexInstallMethodLabelText,
            _view.CodexInstallMethodComboBox);
        SetToolTip(text(
            "Used only when Custom is selected as the install method.",
            "설치 방법에서 사용자 지정을 선택했을 때만 사용해요."),
            _view.CustomCodexUpdateCommandLabelText,
            _view.CustomCodexUpdateCommandTextBox);
        SetToolTip(text(
            "Apply these update settings before saving the main Settings screen.",
            "이 업데이트 설정을 적용한 뒤 메인 설정 화면에서 저장해요."), _view.UpdateDetailsApplyButton);
        SetToolTip(text(
            "Save all current WindexBar settings.",
            "현재 WindexBar 설정을 모두 저장해요."), _view.SaveButton);
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
            config.RateLimitAlerts.Enabled = _view.RateLimitAlertsCheckBox.IsChecked == true;
            config.Sidebar.ShowOnHover = _view.SidebarHoverRevealCheckBox.IsChecked == true;
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

    private void OnSidebarHoverRevealChanged(object sender, RoutedEventArgs args) => ApplySidebarHoverShortcutState();

    private void ApplyAutoShowShortcutState()
    {
        var enabled = _view.AutoShowWithCodexCheckBox.IsChecked == true;
        _view.ToggleHotkeyButton.IsEnabled = !enabled;
        _view.ToggleHotkeyButton.Opacity = enabled ? 0.45 : 1;
        _view.ToggleHotkeyLabelText.Opacity = enabled ? 0.65 : 1;
    }

    private void ApplySidebarHoverShortcutState()
    {
        var locked = _view.SidebarHoverRevealCheckBox.IsChecked == true;
        _view.ToggleSidebarHotkeyButton.IsEnabled = !locked;
        _view.ToggleSidebarHotkeyButton.Opacity = locked ? 0.45 : 1;
        _view.ToggleSidebarHotkeyLabelText.Opacity = locked ? 0.65 : 1;
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
