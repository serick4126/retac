using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ReTAC.Shell;

/// <summary>R-117 / R-118: システムのイメージリストとサムネイルの Win32 / COM の宣言。</summary>
internal static class ShellImageInterop
{
    public const int SHIL_LARGE = 0;        // 32px
    public const int SHIL_SMALL = 1;        // 16px
    public const int SHIL_EXTRALARGE = 2;   // 48px
    public const int SHIL_JUMBO = 4;        // 256px
    public const uint SHGFI_ICON = 0x000000100;
    public const uint SHGFI_SMALLICON = 0x000000001;
    public const uint SHGFI_SYSICONINDEX = 0x000004000;
    public const uint SHGFI_OVERLAYINDEX = 0x000000040;
    public const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    public const int ILD_TRANSPARENT = 0x1;

    [Flags]
    public enum SIIGBF
    {
        ResizeToFit = 0x0, BiggerSizeOk = 0x1, MemoryOnly = 0x2, IconOnly = 0x4, ThumbnailOnly = 0x8, InCacheOnly = 0x10,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE { public int cx, cy; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IImageList
    {
        // vtable の順を保つ（Windows SDK の CommonControls.h の宣言順）。使うのは GetIcon・GetIconSize・GetOverlayImage だけ
        [PreserveSig] int Add(IntPtr a, IntPtr b, out int c);
        [PreserveSig] int ReplaceIcon(int a, IntPtr b, out int c);
        [PreserveSig] int SetOverlayImage(int a, int b);
        [PreserveSig] int Replace(int a, IntPtr b, IntPtr c);
        [PreserveSig] int AddMasked(IntPtr a, int b, out int c);
        [PreserveSig] int Draw(IntPtr a);
        [PreserveSig] int Remove(int a);
        [PreserveSig] int GetIcon(int i, int flags, out IntPtr picon);
        [PreserveSig] int GetImageInfo(int a, IntPtr b);
        [PreserveSig] int Copy(int a, IntPtr b, int c, int d);
        [PreserveSig] int Merge(int a, IntPtr b, int c, int d, int e, ref Guid f, out IntPtr g);
        [PreserveSig] int Clone(ref Guid a, out IntPtr b);
        [PreserveSig] int GetImageRect(int a, IntPtr b);
        [PreserveSig] int GetIconSize(out int cx, out int cy);
        [PreserveSig] int SetIconSize(int a, int b);
        [PreserveSig] int GetImageCount(out int a);
        [PreserveSig] int SetImageCount(int a);
        [PreserveSig] int SetBkColor(int a, out int b);
        [PreserveSig] int GetBkColor(out int a);
        [PreserveSig] int BeginDrag(int a, int b, int c);
        [PreserveSig] int EndDrag();
        [PreserveSig] int DragEnter(IntPtr a, int b, int c);
        [PreserveSig] int DragLeave(IntPtr a);
        [PreserveSig] int DragMove(int a, int b);
        [PreserveSig] int SetDragCursorImage(IntPtr a, int b, int c, int d);
        [PreserveSig] int DragShowNolock(int a);
        [PreserveSig] int GetDragImage(IntPtr a, IntPtr b, ref Guid c, out IntPtr d);
        [PreserveSig] int GetItemFlags(int a, out int b);
        [PreserveSig] int GetOverlayImage(int overlay, out int index);
    }

    [DllImport("shell32.dll", EntryPoint = "#727")]
    private static extern int SHGetImageListRaw(int iImageList, ref Guid riid, out IImageList ppv);

    /// <summary>
    /// システムのイメージリストを取り、使い終わったら必ず COM の参照を解放する（解放しないと取得のたびに参照が残る）。
    /// 取れなければ use を呼ばずに fallback を返す。
    /// </summary>
    public static T WithImageList<T>(int list, Func<IImageList, T> use, T fallback)
    {
        var iid = typeof(IImageList).GUID;
        if (SHGetImageListRaw(list, ref iid, out var images) != 0 || images is null) return fallback;
        try { return use(images); }
        finally { Marshal.ReleaseComObject(images); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    public static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public short bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DIBSECTION
    {
        public BITMAP dsBm;
        public BITMAPINFOHEADER dsBmih;
        public int dsBitfields0, dsBitfields1, dsBitfields2;
        public IntPtr dshSection;
        public int dsOffset;
    }

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr h, int c, out DIBSECTION pv);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bmi, uint usage);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    /// <summary>
    /// R-117: GetImage の HBITMAP（32bpp の DIB セクション。α は乗算済み）を、透明度を保った ARGB の Bitmap にする。
    /// Image.FromHbitmap は透明度を捨て、その後で写しても戻らないので使わない。DIB セクションの画素を直接読む。
    /// GetObject の biHeight は、作られたときが top-down でも正で返る（実機で確かめた）ので向きを判断できない。
    /// そこで GetDIBits に負の高さ（top-down）を頼み、元の向きに関わらず上の行から並んだ画素を受け取る。
    /// DIB セクションでない・32bpp でないときだけ FromHbitmap に落とす。HBITMAP は必ずここで解放する。
    /// </summary>
    public static Bitmap ToArgb(IntPtr hbitmap)
    {
        try
        {
            if (GetObject(hbitmap, Marshal.SizeOf<DIBSECTION>(), out var dib) == 0 || dib.dsBm.bmBits == IntPtr.Zero || dib.dsBm.bmBitsPixel != 32)
                return Image.FromHbitmap(hbitmap);
            var (width, height) = (dib.dsBm.bmWidth, dib.dsBm.bmHeight);
            var header = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = width, biHeight = -height, biPlanes = 1, biBitCount = 32,
            };
            var pixels = new byte[width * height * 4];
            var hdc = GetDC(IntPtr.Zero);
            try
            {
                if (GetDIBits(hdc, hbitmap, 0, (uint)height, pixels, ref header, 0) == 0) return Image.FromHbitmap(hbitmap);
            }
            finally { ReleaseDC(IntPtr.Zero, hdc); }
            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                using var view = new Bitmap(width, height, width * 4, PixelFormat.Format32bppPArgb, handle.AddrOfPinnedObject());
                var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(result);
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;   // 乗算済みを戻して写す（重ねない）
                g.DrawImage(view, 0, 0, width, height);
                return result;
            }
            finally { handle.Free(); }
        }
        finally { DeleteObject(hbitmap); }
    }

    /// <summary>テスト用: 画素を与えた 32bpp の DIB セクションを作る。</summary>
    internal static IntPtr CreateTestDib(int width, int height, bool bottomUp, byte[] bits)
    {
        var header = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = width, biHeight = bottomUp ? height : -height,
            biPlanes = 1, biBitCount = 32,
        };
        var hbitmap = CreateDIBSection(IntPtr.Zero, ref header, 0, out var pixels, IntPtr.Zero, 0);
        Marshal.Copy(bits, 0, pixels, bits.Length);
        return hbitmap;
    }
}
