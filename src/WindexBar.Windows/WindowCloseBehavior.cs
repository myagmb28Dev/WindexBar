using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace WindexBar.Windows;

internal static class WindowCloseBehavior
{
    private static readonly IntPtr HwndTopMost = new(-1);
    private const uint SetWindowPosShowWindow = 0x0040;

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
            positionResult = SetWindowPos(handle, HwndTopMost, 96, 96, 420, 360, SetWindowPosShowWindow);
            restoreResult = ShowWindow(handle, ShowWindowCommand.Restore);
            foregroundResult = SetForegroundWindow(handle);
        }

        return Describe(handle, showResult, positionResult, restoreResult, foregroundResult);
    }

    public static void Hide(Window window)
    {
        var handle = WindowNative.GetWindowHandle(window);
        _ = ShowWindow(handle, ShowWindowCommand.Hide);
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
        Show = 5,
        Restore = 9
    }
}
