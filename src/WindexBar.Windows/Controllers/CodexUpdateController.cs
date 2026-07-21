using WindexBar.Core.Config;
using WindexBar.Core.Refresh;
using WindexBar.Core.Updates;
using WindexBar.Windows.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WindexBar.Windows.Controllers;

internal sealed class CodexUpdateController
{
    private readonly SettingsViewControl _view;
    private readonly SettingsStore _settingsStore;
    private readonly UsageStore _usageStore;
    private readonly CodexCliUpdateService _updateService;
    private readonly Func<XamlRoot?> _xamlRootProvider;
    private readonly Func<string, string, string> _text;
    private readonly CancellationToken _cancellationToken;

    public CodexUpdateController(
        SettingsViewControl view,
        SettingsStore settingsStore,
        UsageStore usageStore,
        CodexCliUpdateService updateService,
        Func<XamlRoot?> xamlRootProvider,
        Func<string, string, string> text,
        CancellationToken cancellationToken)
    {
        _view = view;
        _settingsStore = settingsStore;
        _usageStore = usageStore;
        _updateService = updateService;
        _xamlRootProvider = xamlRootProvider;
        _text = text;
        _cancellationToken = cancellationToken;
        _view.CheckCodexVersionButton.Click += OnCheckClicked;
        _view.CodexInstallMethodComboBox.SelectionChanged += (_, _) => ApplyCustomCommandVisibility();
        _view.CustomCodexUpdateCommandTextBox.TextChanged += (_, _) => UpdateCommandPreview();
    }

    public CodexVersionCheckResult? LastCheck { get; private set; }

    public bool CanRefreshUsage => LastCheck?.Status is CodexVersionStatus.Current
        or CodexVersionStatus.CompatibleWithoutLatestVersion
        or CodexVersionStatus.RecommendedUpdate;

    public void Load()
    {
        _view.CustomCodexUpdateCommandTextBox.Text = _settingsStore.Config.CodexUpdates.CustomCommand ?? string.Empty;
        SelectInstallMethod(_settingsStore.Config.CodexUpdates.InstallMethod);
        ApplyCustomCommandVisibility();
        UpdateStatus(LastCheck);
    }

    public void ApplyLanguage()
    {
        _view.CodexInstallMethodLabelText.Text = _text("Codex install method", "Codex 설치 방식");
        _view.CustomCodexUpdateCommandLabelText.Text = _text("Custom update command", "사용자 지정 업데이트 명령");
        _view.CustomCodexUpdateCommandTextBox.PlaceholderText = _text(
            "Enter a Codex CLI update command.",
            "Codex CLI 업데이트 명령을 입력하세요.");
        _view.CheckCodexVersionButton.Content = _text("Check now", "지금 확인");
        UpdateStatus(LastCheck);
    }

    public void ApplySettings(WindexBarConfig config)
    {
        config.CodexUpdates.InstallMethod = ReadSelectedInstallMethod();
        config.CodexUpdates.CustomCommand = _view.CustomCodexUpdateCommandTextBox.Text;
    }

    public async Task CheckAsync(bool forceLatestVersionRefresh, bool showCurrentResult = false)
    {
        _view.CheckCodexVersionButton.IsEnabled = false;
        UpdateStatus(null, checking: true);
        var cachedVersion = _settingsStore.Config.CodexUpdates.LatestVersion;
        var cachedCheckedAt = _settingsStore.Config.CodexUpdates.LastCheckedAt;
        try
        {
            var result = await _updateService.CheckAsync(
                _settingsStore.Config.CodexUpdates,
                forceLatestVersionRefresh,
                _cancellationToken);
            LastCheck = result;
            UpdateStatus(result);
            if (CanRefreshUsage)
            {
                _usageStore.StartBackgroundRefresh();
            }

            if (!string.Equals(cachedVersion, _settingsStore.Config.CodexUpdates.LatestVersion, StringComparison.Ordinal)
                || cachedCheckedAt != _settingsStore.Config.CodexUpdates.LastCheckedAt)
            {
                _settingsStore.Save();
            }

            if (result.Status is CodexVersionStatus.Missing
                or CodexVersionStatus.RequiredUpdate
                or CodexVersionStatus.RecommendedUpdate)
            {
                await RunUpdateAsync(result);
            }
            else if (showCurrentResult)
            {
                var message = result.Status == CodexVersionStatus.Current
                    ? _text("Codex CLI is up to date.", "Codex CLI가 최신 버전이에요.")
                    : _text(
                        "The installed Codex CLI is compatible. The latest version could not be checked.",
                        "설치된 Codex CLI는 호환돼요. 최신 버전은 확인하지 못했어요.");
                await ShowMessageAsync(_text("Codex CLI", "Codex CLI"), message);
            }
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            UpdateStatus(null, checkError: error.Message);
            if (showCurrentResult)
            {
                await ShowMessageAsync(
                    _text("Codex version check failed", "Codex 버전 확인 실패"),
                    error.Message);
            }
        }
        finally
        {
            _view.CheckCodexVersionButton.IsEnabled = true;
        }
    }

    private async void OnCheckClicked(object sender, RoutedEventArgs args) =>
        await CheckAsync(forceLatestVersionRefresh: true, showCurrentResult: true);

    private async Task RunUpdateAsync(CodexVersionCheckResult check)
    {
        var target = check.LatestVersion ?? check.RequiredVersion;
        var content = new StackPanel { Spacing = 12, MinWidth = 220 };
        content.Children.Add(new TextBlock
        {
            Text = _text($"Updating Codex CLI to {target}...", $"Codex CLI를 {target}(으)로 업데이트하는 중이에요..."),
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new ProgressBar
        {
            IsIndeterminate = true,
            HorizontalAlignment = HorizontalAlignment.Stretch
        });
        content.Children.Add(new TextBlock
        {
            Text = _text(
                "WindexBar will verify the installed version when the update finishes.",
                "업데이트가 끝나면 WindexBar가 설치 버전을 다시 확인해요."),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72,
            FontSize = 11
        });
        var progress = CreateDialog(_text("Updating Codex CLI", "Codex CLI 업데이트 중"), content);
        var progressOperation = progress.ShowAsync();
        await Task.Yield();

        CodexUpdateResult? result = null;
        Exception? updateError = null;
        try
        {
            result = await _updateService.UpdateAsync(
                _settingsStore.Config.CodexUpdates.InstallMethod,
                _settingsStore.Config.CodexUpdates.CustomCommand,
                target,
                _cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            updateError = error;
        }
        finally
        {
            progress.Hide();
            await progressOperation;
        }

        if (updateError is not null)
        {
            await ShowMessageAsync(_text("Codex CLI update failed", "Codex CLI 업데이트 실패"), updateError.Message);
            return;
        }

        if (result!.IsSuccess)
        {
            LastCheck = new CodexVersionCheckResult(
                CodexVersionStatus.Current,
                result.InstalledVersion,
                check.RequiredVersion,
                check.LatestVersion,
                check.DetectedInstallMethod,
                check.UsedCachedLatestVersion,
                null);
            UpdateStatus(LastCheck);
            await ShowMessageAsync(
                _text("Codex CLI updated", "Codex CLI 업데이트 완료"),
                _text($"Codex CLI {result.InstalledVersion} is ready.", $"Codex CLI {result.InstalledVersion}을 사용할 수 있어요."));
            _usageStore.StartBackgroundRefresh();
            return;
        }

        var details = string.IsNullOrWhiteSpace(result.Command.CombinedOutput)
            ? result.ErrorDescription ?? _text("Unknown error", "알 수 없는 오류")
            : $"{result.ErrorDescription}\n\n{result.Command.CombinedOutput}";
        await ShowMessageAsync(_text("Codex CLI update failed", "Codex CLI 업데이트 실패"), details);
    }

    private string ReadSelectedInstallMethod() =>
        _view.CodexInstallMethodComboBox.SelectedItem is ComboBoxItem { Tag: string method }
            ? CodexInstallMethodNames.Normalize(method)
            : CodexInstallMethodNames.Auto;

    private void SelectInstallMethod(string? installMethod)
    {
        var normalized = CodexInstallMethodNames.Normalize(installMethod);
        _view.CodexInstallMethodComboBox.SelectedItem = _view.CodexInstallMethodComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag is string value
                && string.Equals(value, normalized, StringComparison.OrdinalIgnoreCase));
        _view.CodexInstallMethodComboBox.SelectedIndex = _view.CodexInstallMethodComboBox.SelectedItem is null
            ? 0
            : _view.CodexInstallMethodComboBox.SelectedIndex;
    }

    private void ApplyCustomCommandVisibility()
    {
        var visibility = ReadSelectedInstallMethod() == CodexInstallMethodNames.Custom
            ? Visibility.Visible
            : Visibility.Collapsed;
        _view.CustomCodexUpdateCommandLabelText.Visibility = visibility;
        _view.CustomCodexUpdateCommandTextBox.Visibility = visibility;
        UpdateCommandPreview();
    }

    private void UpdateCommandPreview()
    {
        var selectedMethod = ReadSelectedInstallMethod();
        var effectiveMethod = selectedMethod == CodexInstallMethodNames.Auto
            ? LastCheck?.DetectedInstallMethod ?? CodexInstallMethodNames.PowerShell
            : selectedMethod;
        var target = LastCheck?.LatestVersion
            ?? LastCheck?.RequiredVersion
            ?? CodexVersionPolicy.MinimumRequiredVersion;
        try
        {
            var command = CodexCliUpdateService.BuildUpdateCommand(
                effectiveMethod,
                _view.CustomCodexUpdateCommandTextBox.Text,
                target);
            _view.CodexUpdateCommandPreviewText.Text = _text("Command: ", "명령: ") + command.Arguments[^1];
        }
        catch (InvalidOperationException)
        {
            _view.CodexUpdateCommandPreviewText.Text = _text(
                "Enter a custom update command.",
                "사용자 지정 업데이트 명령을 입력하세요.");
        }
    }

    private void UpdateStatus(CodexVersionCheckResult? result, bool checking = false, string? checkError = null)
    {
        var current = checking
            ? _text("Checking...", "확인 중...")
            : checkError is not null
                ? _text("Check failed", "확인 실패")
                : result?.Status == CodexVersionStatus.Missing
                    ? _text("Not installed", "설치되지 않음")
                    : result?.InstalledVersion?.ToString()
                        ?? _text("Waiting for startup check", "시작 확인 대기 중");
        _view.CurrentCodexVersionText.Text = $"{_text("Current Codex CLI", "현재 Codex CLI")}\n{current}";
        var latest = result?.LatestVersion?.ToString() ?? _text("unknown", "알 수 없음");
        var method = result?.DetectedInstallMethod ?? ReadSelectedInstallMethod();
        _view.UpdateDetailsVersionText.Text = string.Join(
            Environment.NewLine,
            $"{_text("Installed", "설치됨")}: {current}",
            $"{_text("Latest", "최신")}: {latest}",
            $"{_text("Install method", "설치 방식")}: {method}");
        UpdateCommandPreview();
    }

    private ContentDialog CreateDialog(string title, UIElement content) => new()
    {
        Title = title,
        Content = content,
        XamlRoot = _xamlRootProvider()
    };

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = CreateDialog(title, new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        dialog.CloseButtonText = _text("Close", "닫기");
        await dialog.ShowAsync();
    }
}
