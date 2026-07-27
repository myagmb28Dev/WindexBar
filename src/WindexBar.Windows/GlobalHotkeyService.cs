using System.ComponentModel;
using System.Runtime.InteropServices;
using WindexBar.Core.Config;
using Forms = System.Windows.Forms;

namespace WindexBar.Windows;

internal sealed class GlobalHotkeyService : IDisposable
{
    private const int WindowHotkeyMessage = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private readonly HotkeyMessageWindow _window;
    private readonly Dictionary<int, Action> _actions = [];
    private readonly HashSet<int> _registeredIds = [];
    private bool _disposed;

    public GlobalHotkeyService()
    {
        _window = new HotkeyMessageWindow(id =>
        {
            if (!_disposed && _actions.TryGetValue(id, out var action))
            {
                action();
            }
        });
    }

    public bool Register(int id, string shortcutText, Action onPressed, out string? error)
    {
        error = null;
        Unregister(id);
        _actions.Remove(id);

        if (!HotkeyShortcut.TryParse(shortcutText, out var shortcut) || shortcut is null)
        {
            error = "Invalid shortcut.";
            return false;
        }

        if (!HotkeyKeyMapper.TryGetVirtualKey(shortcut.Key, out var virtualKey))
        {
            error = "Unsupported shortcut key.";
            return false;
        }

        var modifiers = ModNoRepeat;
        if (shortcut.Alt)
        {
            modifiers |= ModAlt;
        }

        if (shortcut.Control)
        {
            modifiers |= ModControl;
        }

        if (shortcut.Shift)
        {
            modifiers |= ModShift;
        }

        if (shortcut.Windows)
        {
            modifiers |= ModWin;
        }

        if (!RegisterHotKey(_window.Handle, id, modifiers, virtualKey))
        {
            error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return false;
        }

        _registeredIds.Add(id);
        _actions[id] = onPressed;
        return true;
    }

    public void Unregister(int id)
    {
        if (!_registeredIds.Remove(id))
        {
            return;
        }

        _ = UnregisterHotKey(_window.Handle, id);
        _actions.Remove(id);
    }

    public void UnregisterAll()
    {
        foreach (var id in _registeredIds.ToArray())
        {
            Unregister(id);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterAll();
        _window.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private sealed class HotkeyMessageWindow : Forms.NativeWindow, IDisposable
    {
        private readonly Action<int> _onPressed;

        public HotkeyMessageWindow(Action<int> onPressed)
        {
            _onPressed = onPressed;
            CreateHandle(new Forms.CreateParams { Caption = "WindexBarHotkey" });
        }

        protected override void WndProc(ref Forms.Message message)
        {
            if (message.Msg == WindowHotkeyMessage)
            {
                _onPressed(message.WParam.ToInt32());
                return;
            }

            base.WndProc(ref message);
        }

        public void Dispose() => DestroyHandle();
    }
}
