using System.Runtime.InteropServices;

namespace WindexBar.Windows;

internal static class NativeWindowState
{
    public static IntPtr ForegroundHandle => GetForegroundWindow();

    public static bool Exists(IntPtr handle) => handle != IntPtr.Zero && IsWindow(handle);

    public static bool IsVisible(IntPtr handle) => handle != IntPtr.Zero && IsWindowVisible(handle);

    public static bool IsMinimized(IntPtr handle) => handle != IntPtr.Zero && IsIconic(handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);
}
