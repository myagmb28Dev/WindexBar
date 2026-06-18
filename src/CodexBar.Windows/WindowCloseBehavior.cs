using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace CodexBar.Windows;

internal static class WindowCloseBehavior
{
    public static void Hide(Window window)
    {
        var handle = WindowNative.GetWindowHandle(window);
        _ = ShowWindow(handle, ShowWindowCommand.Hide);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, ShowWindowCommand command);

    private enum ShowWindowCommand
    {
        Hide = 0
    }
}
