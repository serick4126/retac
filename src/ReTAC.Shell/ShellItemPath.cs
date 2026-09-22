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

    private static readonly Guid FolderIdDesktop = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");
    private static readonly Guid FolderIdComputerFolder = new("0AC0837C-BBF8-452A-850D-79D08E667CA7");

    internal static IShellItem Create(string path)
    {
        var iid = ShellItemIid;
        var hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var unknown);
        if (hr < 0 || unknown == IntPtr.Zero) throw Error(hr, $"Shell項目を解決できません: {path}");
        try { return (IShellItem)Marshal.GetObjectForIUnknown(unknown); }
        finally { Marshal.Release(unknown); }
    }

    /// <summary>R-97: デスクトップツリーのルート。実機ゲートで確認済みの取得順（SHGetKnownFolderItem）。
    /// パス指定の SHCreateItemFromParsingName では「PC」「ネットワーク」等の仮想項目が展開されない。</summary>
    internal static IShellItem CreateDesktopRoot() => CreateKnownFolderItem(FolderIdDesktop);

    /// <summary>R-97: デスクトップ直下の仮想項目「PC」。既知フォルダ GUID から生成し、ツリー内の項目と
    /// IShellItem.Compare(SICHINT_CANONICAL) で同一性を確かめて辿る（パスでは見つからない）。</summary>
    internal static IShellItem CreateComputerFolder() => CreateKnownFolderItem(FolderIdComputerFolder);

    private static IShellItem CreateKnownFolderItem(Guid folderId)
    {
        var iid = ShellItemIid;
        var hr = SHGetKnownFolderItem(ref folderId, 0, IntPtr.Zero, ref iid, out var unknown);
        if (hr < 0 || unknown == IntPtr.Zero) throw Error(hr, $"既知フォルダーを解決できません: {folderId}");
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

    internal static string[] ParentPathsFromRoot(string path) => ParentPathsFrom(RootOf(path), path);

    /// <summary>R-97: デスクトップツリーは Desktop → This PC(仮想) → ドライブ文字 と辿った後、
    /// そのドライブ項目を起点に降りる。起点は RootOf(path) と限らないので、起点を引数で受ける形にする。</summary>
    internal static string[] ParentPathsFrom(string ancestor, string path)
    {
        var stop = Path.TrimEndingDirectorySeparator(ancestor);
        var current = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));
        var parents = new List<string>();
        while (!string.IsNullOrEmpty(current)
               && !string.Equals(Path.TrimEndingDirectorySeparator(current), stop, StringComparison.OrdinalIgnoreCase))
        {
            parents.Add(current);
            current = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(current));
        }
        parents.Reverse();
        return [.. parents];
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
                var hr = array.GetItemAt(i, out var item);
                if (hr < 0)
                {
                    if (item is not null) Marshal.ReleaseComObject(item);
                    continue;
                }
                if (item is null) continue;
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

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderItem(ref Guid folderId, uint flags, IntPtr token, ref Guid iid, out IntPtr item);
}
