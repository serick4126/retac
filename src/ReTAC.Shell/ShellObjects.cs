using System.Runtime.InteropServices;

namespace ReTAC.Shell;

/// <summary>シェルに任せる小物 — プロパティダイアログ（R-56）とショートカットの作成（R-58）。</summary>
public static class ShellObjects
{
    /// <summary>R-56: Windows 標準のプロパティダイアログを開くだけ。独自の情報表示はしない。</summary>
    public static void ShowProperties(IntPtr owner, string path)
    {
        var info = new SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            fMask = SEE_MASK_INVOKEIDLIST | SEE_MASK_NOCLOSEPROCESS,
            hwnd = owner,
            lpVerb = "properties",
            lpFile = path,
            nShow = SW_SHOW,
        };
        if (!ShellExecuteEx(ref info))
            throw new IOException($"{path} のプロパティを開けません。(Win32 {Marshal.GetLastWin32Error()})");
    }

    /// <summary>
    /// R-58: ショートカットファイルを作る。
    /// IShellLink を直に叩くより、WScript.Shell に任せた方がはるかに短い。
    /// </summary>
    public static void CreateShortcut(string linkPath, string targetPath, string workingDirectory)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new IOException("WScript.Shell が使えません。");
        dynamic shell = Activator.CreateInstance(type)!;
        try
        {
            dynamic link = shell.CreateShortcut(linkPath);
            link.TargetPath = targetPath;
            link.WorkingDirectory = workingDirectory;
            link.Save();
            Marshal.FinalReleaseComObject(link);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    private const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;
    private const uint SEE_MASK_NOCLOSEPROCESS = 0x00000040;
    private const int SW_SHOW = 5;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }
}
