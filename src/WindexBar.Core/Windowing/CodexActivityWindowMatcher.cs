namespace WindexBar.Core.Windowing;

public sealed record CodexActivityWindowSnapshot(
    string? ProcessName,
    string? WindowTitle,
    IReadOnlyCollection<string> DescendantProcessNames,
    bool HasTerminalCodexProcess = false);

public sealed record CodexActivityProcessSnapshot(
    int ProcessId,
    int ParentProcessId,
    string? ProcessName);

public static class CodexActivityWindowMatcher
{
    private static readonly HashSet<string> CodexDesktopProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "chatgpt",
        "codex",
        "codex desktop",
        "codex-desktop"
    };

    private static readonly HashSet<string> TerminalProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd",
        "conhost",
        "openconsole",
        "powershell",
        "pwsh",
        "terminal",
        "windowsterminal",
        "wt"
    };

    public static bool IsCodexActivity(CodexActivityWindowSnapshot? window)
    {
        if (window is null)
        {
            return false;
        }

        var processName = NormalizeProcessName(window.ProcessName);
        if (processName is not null && CodexDesktopProcessNames.Contains(processName))
        {
            return true;
        }

        if (!IsTerminalProcess(processName))
        {
            return false;
        }

        if (window.DescendantProcessNames.Any(name => string.Equals(NormalizeProcessName(name), "codex", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (window.HasTerminalCodexProcess)
        {
            return true;
        }

        return window.WindowTitle?.Contains("codex", StringComparison.OrdinalIgnoreCase) == true;
    }

    public static bool HasTerminalCodexProcess(IReadOnlyCollection<CodexActivityProcessSnapshot> processes)
    {
        var processesById = processes
            .GroupBy(process => process.ProcessId)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var process in processes)
        {
            if (!string.Equals(NormalizeProcessName(process.ProcessName), "codex", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var visited = new HashSet<int> { process.ProcessId };
            var parentProcessId = process.ParentProcessId;
            while (parentProcessId > 0 && visited.Add(parentProcessId) && processesById.TryGetValue(parentProcessId, out var parent))
            {
                if (IsTerminalProcess(NormalizeProcessName(parent.ProcessName)))
                {
                    return true;
                }

                parentProcessId = parent.ParentProcessId;
            }
        }

        return false;
    }

    public static bool IsWindexBarWindow(CodexActivityWindowSnapshot? window)
    {
        var processName = NormalizeProcessName(window?.ProcessName);
        return string.Equals(processName, "WindexBar.Windows", StringComparison.OrdinalIgnoreCase)
            || string.Equals(processName, "WindexBar", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTerminalProcess(string? processName) =>
        processName is not null && TerminalProcessNames.Contains(processName);

    private static string? NormalizeProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return null;
        }

        var normalized = processName.Trim();
        return normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^4]
            : normalized;
    }
}
