using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace ReTAC.Shell;

/// <summary>
/// R-99（利用者の決定）: プレビューハンドラーが登録されていないファイルの代わりの出し方。
/// Windows 自身のサムネイル（エクスプローラーのプレビューウィンドウと同じ仕組み）があればそれを、
/// 無くて中身がテキストなら TXT のハンドラーを使う。どちらでもなければ「プレビューできません」。
/// </summary>
/// <param name="Text">TXT のハンドラーで出す</param>
/// <param name="Image">サムネイル。受け取った側が Dispose する</param>
public sealed record PreviewFallback(bool Text, Bitmap? Image)
{
    private static readonly PreviewFallback None = new(false, null);

    /// <summary>TXT のハンドラー。拡張子の無いテキストにも使う（Windows が .txt に使うもの）。</summary>
    public static Guid? TextHandler => PreviewSession.FindHandler("a.txt");

    /// <summary>
    /// 専用の STA で調べて、completed をそのスレッドから 1 回だけ呼ぶ。
    /// サムネイルの取得もファイルの読み取りも、応答しないドライブで待たされるので UI スレッドでは行わない。
    /// </summary>
    public static void Resolve(string path, Size size, Action<PreviewFallback> completed)
    {
        var thread = new Thread(() =>
        {
            PreviewFallback result;
            try
            {
                result = Thumbnail(path, size) is { } image ? new(false, image)
                    : LooksLikeText(path) ? new(true, null)
                    : None;
            }
            catch (Exception ex)   // 第三者のサムネイル拡張の境界
            {
                System.Diagnostics.Debug.WriteLine(ex);
                result = None;
            }
            completed(result);
        }) { IsBackground = true, Name = "ReTAC preview fallback" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    /// <summary>
    /// 先頭 8KB に NUL が無ければテキストとみなす（UTF-16 は BOM で見る）。拡張子の一覧は持たない
    /// （.json・.md・設定ファイルなど、登録の無いテキストを広く拾うため）。
    /// </summary>
    public static bool LooksLikeText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[8192];
        var length = stream.Read(buffer, 0, buffer.Length);
        return IsText(buffer.AsSpan(0, length));
    }

    public static bool IsText(ReadOnlySpan<byte> head) =>
        head is [0xFF, 0xFE, ..] or [0xFE, 0xFF, ..] || !head.Contains((byte)0);

    /// <summary>Windows のサムネイル。サムネイルを持たない種類では null（汎用のアイコンは出さない）。</summary>
    private static Bitmap? Thumbnail(string path, Size size)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory) != 0) return null;
        try
        {
            var request = new SIZE { Width = Math.Max(size.Width, 16), Height = Math.Max(size.Height, 16) };
            if (factory.GetImage(request, SIIGBF_THUMBNAILONLY | SIIGBF_BIGGERSIZEOK, out var hbitmap) != 0) return null;
            try { return ToBitmap(hbitmap); }
            finally { DeleteObject(hbitmap); }
        }
        finally { Marshal.ReleaseComObject(factory); }
    }

    /// <summary>
    /// Image.FromHbitmap は透過を捨てる（PNG の透明部分が黒くなる）。DIB の中身を 32bpp のまま写す。
    /// 高さが正なら下から上へ並んだ DIB なので上下を返す。
    /// </summary>
    private static Bitmap ToBitmap(IntPtr hbitmap)
    {
        var section = new DIBSECTION();
        if (GetObject(hbitmap, Marshal.SizeOf<DIBSECTION>(), ref section) == 0 || section.Bits == IntPtr.Zero || section.BitsPixel != 32)
            return System.Drawing.Image.FromHbitmap(hbitmap);
        using var view = new Bitmap(section.Width, section.Height, section.WidthBytes, PixelFormat.Format32bppPArgb, section.Bits);
        var copy = new Bitmap(view);   // DIB は DeleteObject で消えるので、自前のメモリへ写す
        if (section.HeaderHeight > 0) copy.RotateFlip(RotateFlipType.RotateNoneFlipY);
        return copy;
    }

    private const int SIIGBF_BIGGERSIZEOK = 0x01, SIIGBF_THUMBNAILONLY = 0x08;

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int Width, Height;
    }

    /// <summary>BITMAP（bm*）に続けて BITMAPINFOHEADER（biSize・biWidth・biHeight）まで。残りは使わないので省く。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct DIBSECTION
    {
        public int Type, Width, Height, WidthBytes;
        public ushort Planes, BitsPixel;
        public IntPtr Bits;
        public int HeaderSize, HeaderWidth, HeaderHeight;
        public ushort HeaderPlanes, HeaderBitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
        public int BitFields0, BitFields1, BitFields2;
        public IntPtr Section;
        public int Offset;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr hbitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid, out IShellItemImageFactory item);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr handle, int size, ref DIBSECTION section);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);
}
