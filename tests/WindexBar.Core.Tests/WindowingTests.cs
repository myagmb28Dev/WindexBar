using WindexBar.Core.Windowing;

namespace WindexBar.Core.Tests;

public sealed class CodexActivityWindowMatcherTests
{
    [Theory]
    [InlineData("ChatGPT")]
    [InlineData("ChatGPT.exe")]
    public void MatchesChatGptDesktopProcess(string processName)
    {
        var window = new CodexActivityWindowSnapshot(processName, "ChatGPT", []);

        Assert.True(CodexActivityWindowMatcher.IsCodexActivity(window));
    }

    [Fact]
    public void MatchesCodexDesktopProcess()
    {
        var window = new CodexActivityWindowSnapshot("Codex", "Codex", []);

        Assert.True(CodexActivityWindowMatcher.IsCodexActivity(window));
    }

    [Fact]
    public void MatchesTerminalWithCodexDescendant()
    {
        var window = new CodexActivityWindowSnapshot("WindowsTerminal", "PowerShell", ["pwsh", "codex"]);

        Assert.True(CodexActivityWindowMatcher.IsCodexActivity(window));
    }

    [Fact]
    public void MatchesTerminalWhenCodexRunsOutsideWindowProcessTree()
    {
        var window = new CodexActivityWindowSnapshot("WindowsTerminal", "PowerShell", [], HasTerminalCodexProcess: true);

        Assert.True(CodexActivityWindowMatcher.IsCodexActivity(window));
    }

    [Fact]
    public void FindsCodexCliWithTerminalShellAncestor()
    {
        CodexActivityProcessSnapshot[] processes =
        [
            new(10, 1, "cmd.exe"),
            new(11, 10, "node.exe"),
            new(12, 11, "codex.exe")
        ];

        Assert.True(CodexActivityWindowMatcher.HasTerminalCodexProcess(processes));
    }

    [Fact]
    public void IgnoresCodexProcessOwnedByDesktopApp()
    {
        CodexActivityProcessSnapshot[] processes =
        [
            new(20, 1, "ChatGPT.exe"),
            new(21, 20, "codex.exe")
        ];

        Assert.False(CodexActivityWindowMatcher.HasTerminalCodexProcess(processes));
    }

    [Fact]
    public void MatchesTerminalTitleFallback()
    {
        var window = new CodexActivityWindowSnapshot("pwsh", "codex app-server", []);

        Assert.True(CodexActivityWindowMatcher.IsCodexActivity(window));
    }

    [Theory]
    [InlineData("pwsh", true)]
    [InlineData("WindowsTerminal.exe", true)]
    [InlineData("chrome", false)]
    [InlineData("Code.exe", false)]
    public void IdentifiesProcessesThatCanOwnCodexTerminalActivity(string processName, bool expected)
    {
        Assert.Equal(expected, CodexActivityWindowMatcher.IsTerminalProcess(processName));
    }

    [Fact]
    public void DoesNotMatchBrowserTitleFallback()
    {
        var window = new CodexActivityWindowSnapshot("chrome", "Codex docs", []);

        Assert.False(CodexActivityWindowMatcher.IsCodexActivity(window));
    }

    [Fact]
    public void DoesNotMatchChatGptBrowserTitleFallback()
    {
        var window = new CodexActivityWindowSnapshot("chrome", "ChatGPT", []);

        Assert.False(CodexActivityWindowMatcher.IsCodexActivity(window));
    }

    [Theory]
    [InlineData("WindexBar.Windows")]
    [InlineData("WindexBar")]
    public void IdentifiesOwnWindexBarWindow(string processName)
    {
        var window = new CodexActivityWindowSnapshot(processName, "WindexBar", []);

        Assert.True(CodexActivityWindowMatcher.IsWindexBarWindow(window));
        Assert.False(CodexActivityWindowMatcher.IsCodexActivity(window));
    }
}

public sealed class AutoVisibilityPolicyTests
{
    [Theory]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, false, true)]
    public void OnlyShowsForEnabledCodexActivityWhenUserDidNotHide(bool enabled, bool codexActivity, bool userHidden, bool expected)
    {
        Assert.Equal(expected, AutoVisibilityPolicy.ShouldShow(enabled, codexActivity, userHidden));
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    public void PreservesOwnWindowFocusOnlyWhilePreviousCodexWindowRemainsAvailable(
        bool hasPreviousCodexWindow,
        bool previousCodexWindowVisible,
        bool previousCodexWindowMinimized,
        bool expected)
    {
        Assert.Equal(
            expected,
            AutoVisibilityPolicy.ShouldPreserveWhileOwnWindowFocused(
                hasPreviousCodexWindow,
                previousCodexWindowVisible,
                previousCodexWindowMinimized));
    }

    [Fact]
    public void KeepsWindowVisibleThroughOneTransientInactiveSample()
    {
        var filter = new AutoVisibilityStabilityFilter(inactiveSamplesBeforeHide: 2);

        Assert.True(filter.ShouldTreatAsActive(true));
        Assert.True(filter.ShouldTreatAsActive(false));
        Assert.False(filter.ShouldTreatAsActive(false));
    }

    [Fact]
    public void DoesNotTreatInitialInactiveStateAsActive()
    {
        var filter = new AutoVisibilityStabilityFilter(inactiveSamplesBeforeHide: 2);

        Assert.False(filter.ShouldTreatAsActive(false));
    }
}

public sealed class WindowPlacementControllerTests
{
    [Fact]
    public void FirstResizeUsesDefaultPositionThenPreservesCurrentPosition()
    {
        var controller = new WindowPlacementController(new WindowPosition(96, 96));

        var initialPosition = controller.PositionForResize(new WindowPosition(320, 220));
        var restoredPosition = controller.PositionForResize(new WindowPosition(320, 220));

        Assert.Equal(new WindowPosition(96, 96), initialPosition);
        Assert.Equal(new WindowPosition(320, 220), restoredPosition);
    }

    [Fact]
    public void ActivationPlanPreservesCurrentBounds()
    {
        var plan = WindowActivationPlan.PreserveCurrentBounds;

        Assert.True(plan.PreservesPosition);
        Assert.True(plan.PreservesSize);
        Assert.Equal(0, plan.X);
        Assert.Equal(0, plan.Y);
        Assert.Equal(0, plan.Width);
        Assert.Equal(0, plan.Height);
    }
}
