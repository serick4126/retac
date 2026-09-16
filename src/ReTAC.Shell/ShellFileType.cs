using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;

namespace ReTAC.Shell;

/// <summary>ステータスバー ④ 区画の「種別」（R-34）。シェルの種類名を拡張子単位でキャッシュする。</summary>
public static class ShellFileType
{
    // 列挙は Task.Run 上でも走る（MainForm の非同期の folder 展開）。素の Dictionary だと壊れる
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string TypeName(string fullPath, bool isFolder)
    {
        var key = isFolder ? "__folder__" : Path.GetExtension(fullPath);
        if (Cache.TryGetValue(key, out var cached)) return cached;

        // SHGFI_USEFILEATTRIBUTES: 実ファイルに触れないので、応答しないドライブでも固まらない（N-05）
        var attributes = isFolder ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
        var name = SHGetFileInfo(isFolder ? @"C:\x" : "x" + key, attributes,
            out var info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_TYPENAME | SHGFI_USEFILEATTRIBUTES) == IntPtr.Zero
            ? ""
            : info.szTypeName;

        Cache[key] = name;
        return name;
    }

    /// <summary>
    /// 16.4 節「関連付けファイル」の判定。HKCR に拡張子の既定値があれば関連付けありとみなす。
    /// 種類名から推測するより確実で、レジストリを読むだけなので応答しないドライブの影響も受けない。
    /// </summary>
    public static bool HasAssociation(string extension)
    {
        if (string.IsNullOrEmpty(extension)) return false;
        if (AssociationCache.TryGetValue(extension, out var cached)) return cached;

        using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(extension);
        var associated = key?.GetValue(null) is string progId && progId.Length > 0;
        AssociationCache[extension] = associated;
        return associated;
    }

    private static readonly ConcurrentDictionary<string, bool> AssociationCache = new(StringComparer.OrdinalIgnoreCase);

    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint SHGFI_TYPENAME = 0x000000400;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        out SHFILEINFO psfi, uint cbFileInfo, uint uFlags);
}
