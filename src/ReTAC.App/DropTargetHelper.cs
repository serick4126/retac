using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace ReTAC.App;

/// <summary>
/// ドラッグ元が付けた画像（ファイルリスト・エクスプローラーの「名前の付いた画像」）を、落とす側から OS へ知らせる。
/// 普通のコントロールは WinForms が代わりに知らせるが、ToolStrip は独自の受け口を使うので知らせない。
/// 知らせないと画像がカーソルに付いてこず、落とした後もその場に残り続ける（実機指摘）。
/// </summary>
internal static class DropTargetHelper
{
    private static IDropTargetHelper? s_helper;

    private static IDropTargetHelper? Helper
    {
        get
        {
            try { return s_helper ??= (IDropTargetHelper)new DragDropHelper(); }
            catch (COMException) { return null; }   // 画像が出ないだけ。ドロップは続ける
        }
    }

    public static void Enter(Control target, DragEventArgs e)
    {
        if (e.Data is not ComDataObject data) return;
        var point = new Point(e.X, e.Y);
        Helper?.DragEnter(target.Handle, data, ref point, (int)e.Effect);
    }

    public static void Over(DragEventArgs e)
    {
        var point = new Point(e.X, e.Y);
        Helper?.DragOver(ref point, (int)e.Effect);
    }

    public static void Leave() => Helper?.DragLeave();

    public static void Drop(DragEventArgs e)
    {
        if (e.Data is not ComDataObject data) { Leave(); return; }
        var point = new Point(e.X, e.Y);
        Helper?.Drop(data, ref point, (int)e.Effect);
    }

    [ComImport, Guid("4657278A-411B-11D2-839A-00C04FD918D0")]
    private class DragDropHelper;

    [ComImport, Guid("4657278B-411B-11D2-839A-00C04FD918D0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDropTargetHelper
    {
        void DragEnter(IntPtr hwndTarget, ComDataObject dataObject, ref Point point, int effect);
        void DragLeave();
        void DragOver(ref Point point, int effect);
        void Drop(ComDataObject dataObject, ref Point point, int effect);
        void Show([MarshalAs(UnmanagedType.Bool)] bool show);
    }
}
