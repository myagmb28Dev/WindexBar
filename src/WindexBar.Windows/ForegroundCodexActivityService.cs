using System.Diagnostics;
using System.Runtime.InteropServices;
using WindexBar.Core.Windowing;
using Microsoft.UI.Xaml;

namespace WindexBar.Windows;

internal sealed class ForegroundCodexActivityService : IDisposable
{
    private readonly DispatcherTimer _timer = new();
    private bool _isActive;
    private bool _disposed;

    public ForegroundCodexActivityService()
    {
        _timer.Interval = TimeSpan.FromMilliseconds(500);
        _timer.Tick += (_, _) => Poll();
    }

    public event EventHandler<bool>? ActivityChanged;

    public bool IsActive => _isActive;

    public void Start()
    {
        if (_disposed || _timer.IsEnabled)
        {
            return;
        }

        Poll();
        _timer.Start();
    }

    public void Stop()
    {
        if (!_timer.IsEnabled)
        {
            return;
        }

        _timer.Stop();
        SetActive(false);
    }

    private void Poll()
    {
        if (_disposed)
        {
            return;
        }

        SetActive(CodexActivityWindowMatcher.IsCodexActivity(ReadForegroundWindow()));
    }

    private void SetActive(bool value)
    {
        if (_isActive == value)
        {
            return;
        }

        _isActive = value;
        ActivityChanged?.Invoke(this, value);
    }

    private static CodexActivityWindowSnapshot? ReadForegroundWindow()
    {
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        _ = GetWindowThreadProcessId(handle, out var processId);
        if (processId == 0)
        {
            return null;
        }

        try
        {
            var process = Process.GetProcessById((int)processId);
            return new CodexActivityWindowSnapshot(
                process.ProcessName,
                ReadWindowTitle(handle),
                ReadDescendantProcessNames(process.Id));
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadWindowTitle(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return null;
        }

        var buffer = new char[length + 1];
        var copied = GetWindowText(handle, buffer, buffer.Length);
        return copied <= 0 ? null : new string(buffer, 0, copied);
    }

    private static IReadOnlyCollection<string> ReadDescendantProcessNames(int rootProcessId)
    {
        var processes = Process.GetProcesses()
            .Select(process => TryReadProcessInfo(process))
            .Where(info => info is not null)
            .Cast<ProcessInfo>()
            .ToArray();
        var childrenByParent = processes
            .GroupBy(info => info.ParentProcessId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var names = new List<string>();
        var queue = new Queue<int>();
        queue.Enqueue(rootProcessId);
        while (queue.TryDequeue(out var parentId))
        {
            if (!childrenByParent.TryGetValue(parentId, out var children))
            {
                continue;
            }

            foreach (var child in children)
            {
                names.Add(child.Name);
                queue.Enqueue(child.ProcessId);
            }
        }

        return names;
    }

    private static ProcessInfo? TryReadProcessInfo(Process process)
    {
        try
        {
            return new ProcessInfo(process.Id, ReadParentProcessId(process), process.ProcessName);
        }
        catch
        {
            return null;
        }
        finally
        {
            process.Dispose();
        }
    }

    private static int ReadParentProcessId(Process process)
    {
        var status = NtQueryInformationProcess(
            process.Handle,
            processInformationClass: 0,
            out var information,
            Marshal.SizeOf<ProcessBasicInformation>(),
            out _);
        return status == 0 ? information.InheritedFromUniqueProcessId.ToInt32() : 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, [Out] char[] text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        out ProcessBasicInformation processInformation,
        int processInformationLength,
        out int returnLength);

    private sealed record ProcessInfo(int ProcessId, int ParentProcessId, string Name);

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2;
        public IntPtr Reserved3;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }
}
