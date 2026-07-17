using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace WindexBar.Windows;

internal static class OwnedWindowBehavior
{
    private const int OwnerWindowIndex = -8;

    public static void Attach(Window child, Window owner)
    {
        var childHandle = WindowNative.GetWindowHandle(child);
        var ownerHandle = WindowNative.GetWindowHandle(owner);
        _ = SetWindowLongPtr(childHandle, OwnerWindowIndex, ownerHandle);
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint windowHandle, int index, nint value);
}
