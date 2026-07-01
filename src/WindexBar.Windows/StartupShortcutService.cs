using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WindexBar.Windows;

internal static class StartupShortcutService
{
    private const string ShortcutName = "WindexBar.lnk";
    private static readonly Guid ShellLinkClsid = new("00021401-0000-0000-C000-000000000046");

    public static void Apply(bool enabled)
    {
        try
        {
            if (enabled)
            {
                EnsureShortcut();
            }
            else
            {
                RemoveShortcut();
            }
        }
        catch
        {
        }
    }

    public static void RemoveIfDisabled(bool enabled)
    {
        if (!enabled)
        {
            Apply(false);
        }
    }

    private static void EnsureShortcut()
    {
        var targetPath = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath))
        {
            return;
        }

        var shortcutPath = StartupShortcutPath();
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);

        var shellLinkType = Type.GetTypeFromCLSID(ShellLinkClsid, throwOnError: true)!;
        var shellLink = (IShellLinkW)Activator.CreateInstance(shellLinkType)!;
        shellLink.SetPath(targetPath);
        shellLink.SetWorkingDirectory(Path.GetDirectoryName(targetPath) ?? AppContext.BaseDirectory);
        shellLink.SetIconLocation(targetPath, 0);

        var persistFile = (IPersistFile)shellLink;
        persistFile.Save(shortcutPath, true);
    }

    private static void RemoveShortcut()
    {
        var shortcutPath = StartupShortcutPath();
        if (File.Exists(shortcutPath))
        {
            File.Delete(shortcutPath);
        }
    }

    private static string StartupShortcutPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        ShortcutName);

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath(IntPtr pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription(IntPtr pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory(IntPtr pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments(IntPtr pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation(IntPtr pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}
