using System.Runtime.InteropServices;
using WindexBar.Core.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace WindexBar.Windows;

internal static class WindowCloseBehavior
{
    private static readonly IntPtr HwndTopMost = new(-1);
    private const uint SetWindowPosNoActivate = 0x0010;

    public static string Show(Window window)
    {
        window.Activate();
        var handle = WindowNative.GetWindowHandle(window);
        var showResult = false;
        var positionResult = false;
        var restoreResult = false;
        var foregroundResult = false;
        if (handle != IntPtr.Zero)
        {
            showResult = ShowWindow(handle, ShowWindowCommand.Show);
            var plan = WindowActivationPlan.PreserveCurrentBounds;
            positionResult = SetWindowPos(handle, HwndTopMost, plan.X, plan.Y, plan.Width, plan.Height, plan.Flags);
            restoreResult = ShowWindow(handle, ShowWindowCommand.Restore);
            foregroundResult = SetForegroundWindow(handle);
        }

        return Describe(handle, showResult, positionResult, restoreResult, foregroundResult);
    }

    public static string ShowPassive(Window window)
    {
        var handle = WindowNative.GetWindowHandle(window);
        var showResult = false;
        var positionResult = false;
        if (handle != IntPtr.Zero)
        {
            showResult = ShowWindow(handle, ShowWindowCommand.ShowNoActivate);
            var plan = WindowActivationPlan.PreserveCurrentBounds;
            positionResult = SetWindowPos(handle, HwndTopMost, plan.X, plan.Y, plan.Width, plan.Height, plan.Flags | SetWindowPosNoActivate);
        }

        return Describe(handle, showResult, positionResult, restoreResult: false, foregroundResult: false);
    }

    public static void Hide(Window window)
    {
        var handle = WindowNative.GetWindowHandle(window);
        _ = ShowWindow(handle, ShowWindowCommand.Hide);
    }

    public static bool IsVisible(Window window)
    {
        var handle = WindowNative.GetWindowHandle(window);
        return handle != IntPtr.Zero && IsWindowVisible(handle) && !IsIconic(handle);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, ShowWindowCommand command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    private static string Describe(IntPtr handle, bool showResult, bool positionResult, bool restoreResult, bool foregroundResult)
    {
        var rectText = "rect=unavailable";
        if (handle != IntPtr.Zero && GetWindowRect(handle, out var rect))
        {
            rectText = $"rect={rect.Left},{rect.Top},{rect.Right - rect.Left}x{rect.Bottom - rect.Top}";
        }

        return $"HWND 0x{handle.ToInt64():X}; isWindow={IsWindow(handle)}; visible={IsWindowVisible(handle)}; {rectText}; show={showResult}; pos={positionResult}; restore={restoreResult}; foreground={foregroundResult}";
    }

    private readonly struct Rect
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
    }

    private enum ShowWindowCommand
    {
        Hide = 0,
        ShowNoActivate = 4,
        Show = 5,
        Restore = 9
    }
}
