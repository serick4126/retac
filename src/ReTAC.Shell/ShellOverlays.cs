using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.InteropServices;

namespace ReTAC.Shell;

/// <summary>
/// R-118: 同期状態・ショートカットの矢印などの OS の印。印の番号はファイルごとに OS に問い合わせる（実ファイルの属性を読む）ので、
/// UI のスレッドから呼ばない（N-05）。印の絵は番号と大きさで覚える（アプリ全体で 1 つ。OS の印は 15 個まで）。
/// INV-THUMBNAIL-NO-CLOUD-DOWNLOAD: 中身は読まない。取り込みを起こさないことは CloudHydrationTests で確かめる。
/// </summary>
public static class ShellOverlays
{
    /// <summary>取れた絵だけを覚える（ConcurrentDictionary は null を値に持てない。取れなかったものは次に呼ばれたときに取り直す）。</summary>
    private static readonly ConcurrentDictionary<(int, int), Bitmap> Images = new();

    /// <returns>印の番号（1〜15）。印が無ければ 0</returns>
    public static int IndexOf(string fullPath)
    {
        // SHGFI_OVERLAYINDEX は SHGFI_ICON と一緒でないと番号が入らない（Microsoft の仕様）。返ったアイコンは使わないが必ず解放する
        var result = SHGetFileInfo(fullPath, 0, out var info, (uint)Marshal.SizeOf<SHFILEINFO>(),
            ShellImageInterop.SHGFI_ICON | ShellImageInterop.SHGFI_SMALLICON | ShellImageInterop.SHGFI_OVERLAYINDEX);
        if (info.hIcon != IntPtr.Zero) ShellImageInterop.DestroyIcon(info.hIcon);
        return result == IntPtr.Zero ? 0 : (int)(((uint)info.iIcon >> 24) & 0xFF);
    }

    /// <summary>呼び出し側は Dispose しない（覚えておいて使い回す）。番号が無効・取れなければ例外ではなく null。</summary>
    public static Bitmap? Image(int overlayIndex, int size)
    {
        if (overlayIndex <= 0 || size <= 0) return null;
        var key = (overlayIndex, size);
        if (Images.TryGetValue(key, out var cached)) return cached;
        Bitmap? loaded;
        try { loaded = Load(overlayIndex, size); }
        catch (Exception) { return null; }   // COM の失敗で描画を止めない（印を描かないだけ）
        if (loaded is null) return null;
        var stored = Images.GetOrAdd(key, loaded);
        if (!ReferenceEquals(stored, loaded)) loaded.Dispose();   // ほかのスレッドが先に入れた
        return stored;
    }

    private static Bitmap? Load(int overlayIndex, int size)
    {
        var list = size <= 16 ? ShellImageInterop.SHIL_SMALL : size <= 32 ? ShellImageInterop.SHIL_LARGE
            : size <= 48 ? ShellImageInterop.SHIL_EXTRALARGE : ShellImageInterop.SHIL_JUMBO;
        return ShellImageInterop.WithImageList(list, images =>
        {
            if (images.GetOverlayImage(overlayIndex, out var imageIndex) != 0) return null;
            if (images.GetIcon(imageIndex, ShellImageInterop.ILD_TRANSPARENT, out var hicon) != 0 || hicon == IntPtr.Zero) return null;
            try
            {
                using var icon = Icon.FromHandle(hicon);
                using var source = icon.ToBitmap();
                var bitmap = new Bitmap(size, size);
                using var g = Graphics.FromImage(bitmap);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(source, 0, 0, size, size);
                return bitmap;
            }
            finally { ShellImageInterop.DestroyIcon(hicon); }
        }, (Bitmap?)null);
    }

    // SHFILEINFO と SHGetFileInfo の宣言は ShellIcons と同じ（ByValTStr を含むので DllImport）
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
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, out SHFILEINFO psfi, uint cbFileInfo, uint uFlags);
}
