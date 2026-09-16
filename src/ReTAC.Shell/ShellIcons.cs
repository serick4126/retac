using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;

namespace ReTAC.Shell;

/// <summary>
/// R-66-2: 表示する寸法に対応した解像度のアイコンをシェルから取得する。小さなアイコンを引き伸ばさない。
/// 取得結果は拡張子（＝アイコンを共有できる単位）でキャッシュする。
/// </summary>
public sealed class ShellIcons : IDisposable
{
    /// <summary>実行ファイル自身がアイコンを持つため、拡張子ではなくパスでキャッシュする種別。</summary>
    private static readonly HashSet<string> SelfIconed =
        new(StringComparer.OrdinalIgnoreCase) { ".exe", ".ico", ".lnk", ".cur", ".ani", ".scr", ".dll" };

    private readonly Dictionary<string, Bitmap> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _size;

    /// <param name="size">描画する 1 辺のピクセル数。DPI から算出した値を渡す。</param>
    public ShellIcons(int size) => _size = size;

    public int Size => _size;

    /// <summary>
    /// 汎用のフォルダアイコン（黄色いフォルダ）。
    /// ドライブルート（<c>C:\</c>）を渡すとシェルがドライブとして解決してディスクのアイコンを返すため、
    /// ドライブルートではないパスを渡す。SHGFI_USEFILEATTRIBUTES 付きなので実在しなくてよい。
    /// </summary>
    public Bitmap? ForFolder() => Get("__folder__", @"C:\__retac_folder__", FILE_ATTRIBUTE_DIRECTORY, useFileAttributes: true);

    /// <summary>
    /// ドライブバー用。実パスをシェルに渡し、そのパス固有のアイコンを得る
    /// （ドライブルートならディスク、デスクトップフォルダならデスクトップの絵。B-15）。
    /// 応答しないドライブで待たされることがあるので、UI スレッドから呼ばないこと（N-05）。
    /// </summary>
    public Bitmap? ForPath(string path) => Get("path:" + path, path, 0, useFileAttributes: false);

    public Bitmap? ForFile(string fullPath)
    {
        var extension = Path.GetExtension(fullPath);
        var key = SelfIconed.Contains(extension) ? fullPath : extension;
        // SHGFI_USEFILEATTRIBUTES を使うため、拡張子キーのときは実ファイルに触れない
        // （応答しないネットワークドライブで固まらせないため。N-05）
        return Get(key, key == fullPath ? fullPath : "x" + extension, FILE_ATTRIBUTE_NORMAL, useFileAttributes: true);
    }

    /// <summary>
    /// パス単位で覚えるものの上限（R-12）。実行ファイルだらけのフォルダを渡り歩くと際限なく増える。
    /// ponytail: 超えたらパス単位のものを丸ごと捨てる。LRU にする価値が出るのは
    /// 上限を超える枚数を何度も往復して使う場合だけ
    /// </summary>
    private const int PathKeyLimit = 512;

    private Bitmap? Get(string cacheKey, string pathForShell, uint attributes, bool useFileAttributes)
    {
        if (_cache.TryGetValue(cacheKey, out var cached)) return cached;

        // 上限を超えるなら、今回の分を「入れる前に」捨てる。入れた後に捨てると、
        // パスでキャッシュする種別（.dll / .exe など）では今追加したキーも
        // DropPathKeyed の対象になり、これから返す Bitmap ごと Dispose してしまう。
        // 呼び出し側が描くと GDI+ が "Parameter is not valid." を投げる（System32 で実機再現）
        if (_cache.Count >= PathKeyLimit) DropPathKeyed();

        var bitmap = Load(pathForShell, attributes, useFileAttributes);
        _cache[cacheKey] = bitmap!;
        return bitmap;
    }

    /// <summary>拡張子で共有できるものは残し、パスで覚えたものだけ捨てる。</summary>
    private void DropPathKeyed()
    {
        foreach (var key in _cache.Keys.Where(key => key.Contains(Path.DirectorySeparatorChar)).ToList())
        {
            _cache[key]?.Dispose();
            _cache.Remove(key);
        }
    }

    private Bitmap? Load(string path, uint attributes, bool useFileAttributes)
    {
        // R-66-2: 表示寸法に対応した解像度を選ぶ。16px 表示に 32px を縮小するとにじむため、
        // 16px までは SMALLICON（等倍）、それより大きい表示は LARGEICON（32px）から縮小する。
        // ponytail: 元解像度の上限は 32px。拡大率 250% 超（=40px 以上）を実用にするなら
        // SHGetImageList(SHIL_EXTRALARGE/JUMBO) へ差し替える。
        var flags = SHGFI_ICON
                  | (useFileAttributes ? SHGFI_USEFILEATTRIBUTES : 0)
                  | (_size <= 16 ? SHGFI_SMALLICON : SHGFI_LARGEICON);
        if (SHGetFileInfo(path, attributes, out var info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags) == IntPtr.Zero)
            return null;
        if (info.hIcon == IntPtr.Zero) return null;

        try
        {
            using var icon = Icon.FromHandle(info.hIcon);
            using var source = icon.ToBitmap();
            // 等倍ならリサンプルせずにそのまま返す（にじみを出さない）
            if (source.Width == _size && source.Height == _size) return new Bitmap(source);

            var bitmap = new Bitmap(_size, _size);
            using var g = Graphics.FromImage(bitmap);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(source, 0, 0, _size, _size);
            return bitmap;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    public void Dispose()
    {
        foreach (var bitmap in _cache.Values) bitmap?.Dispose();
        _cache.Clear();
    }

    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_SMALLICON = 0x000000001;
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

    // ByValTStr を含む構造体は LibraryImport のソース生成が扱えないため DllImport を使う
    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        out SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
