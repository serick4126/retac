using System.Runtime.InteropServices;
using static ReTAC.Shell.NameSpaceTreeInterop;

namespace ReTAC.Shell;

/// <summary>R-97: ファイルシステムパスとShell項目の境界。</summary>
public static class ShellItemPath
{
    private const uint FileSystemPath = 0x80058000;
    private static readonly Guid ShellItemIid = typeof(IShellItem).GUID;

    public static string RootOf(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || File.Exists(path))
            throw new ArgumentException("実フォルダの絶対パスが必要です。", nameof(path));

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var parts = path[2..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) throw new ArgumentException("UNC共有名まで指定してください。", nameof(path));
            return $@"\\{parts[0]}\{parts[1]}";
        }

        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root) || root.Length < 3 || root[1] != ':')
            throw new ArgumentException("ドライブの絶対パスが必要です。", nameof(path));
        return $"{char.ToUpperInvariant(root[0])}:\\";
    }

    internal static IShellItem Create(string path)
    {
        var iid = ShellItemIid;
        var hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var unknown);
        if (hr < 0 || unknown == IntPtr.Zero) throw Error(hr, $"Shell項目を解決できません: {path}");
        try { return (IShellItem)Marshal.GetObjectForIUnknown(unknown); }
        finally { Marshal.Release(unknown); }
    }

    internal static string? FileSystemPathOf(IShellItem item)
    {
        var hr = item.GetDisplayName(FileSystemPath, out var pointer);
        try { return hr < 0 || pointer == IntPtr.Zero ? null : Marshal.PtrToStringUni(pointer); }
        finally { if (pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pointer); }
    }

    internal static string? FileSystemPathOf(IntPtr itemPointer)
    {
        if (itemPointer == IntPtr.Zero) return null;
        var item = (IShellItem)Marshal.GetObjectForIUnknown(itemPointer);
        try { return FileSystemPathOf(item); }
        finally { Marshal.ReleaseComObject(item); }
    }

    internal static string[] FileSystemPathsOf(IntPtr arrayPointer)
    {
        if (arrayPointer == IntPtr.Zero) return [];
        var array = (IShellItemArray)Marshal.GetObjectForIUnknown(arrayPointer);
        try
        {
            if (array.GetCount(out var count) < 0) return [];
            var paths = new List<string>((int)count);
            for (uint i = 0; i < count; i++)
            {
                if (array.GetItemAt(i, out var item) < 0) continue;
                try { if (FileSystemPathOf(item) is { } path) paths.Add(path); }
                finally { Marshal.ReleaseComObject(item); }
            }
            return [.. paths];
        }
        finally { Marshal.ReleaseComObject(array); }
    }

    internal static Exception Error(int hr, string message) =>
        new IOException($"{message} (0x{hr:X8})", Marshal.GetExceptionForHR(hr));

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid, out IntPtr item);
}
