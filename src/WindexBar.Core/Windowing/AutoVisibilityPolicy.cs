namespace WindexBar.Core.Windowing;

public static class AutoVisibilityPolicy
{
    public static bool ShouldShow(bool enabled, bool codexActivity, bool userHidden) =>
        enabled && codexActivity && !userHidden;

    public static bool ShouldPreserveWhileOwnWindowFocused(
        bool hasPreviousCodexWindow,
        bool previousCodexWindowVisible,
        bool previousCodexWindowMinimized) =>
        hasPreviousCodexWindow
        && previousCodexWindowVisible
        && !previousCodexWindowMinimized;
}
