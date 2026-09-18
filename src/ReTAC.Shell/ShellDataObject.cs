using System.Runtime.InteropServices;

namespace ReTAC.Shell;

/// <summary>
/// シェルが作るドラッグ用のデータ（エクスプローラー自身がドラッグで渡すものと同じ）。
/// ファイルの一覧（FileDrop）だけでは、エクスプローラーはリンクの作成（ショートカット）を受け付けない。
/// </summary>
public static class ShellDataObject
{
    /// <returns>WinForms の DoDragDrop にそのまま渡せる COM の IDataObject。作れなければ null</returns>
    public static object? For(string path)
    {
        var bhid = BHID_DataObject;
        var iid = IID_IDataObject;
        if (SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItem).GUID, out var item) != 0) return null;
        try
        {
            return item.BindToHandler(IntPtr.Zero, ref bhid, ref iid, out var data) == 0 ? data : null;
        }
        finally
        {
            Marshal.ReleaseComObject(item);
        }
    }

    private static readonly Guid BHID_DataObject = new("B8C0BD9F-ED24-455C-83E6-D5390C4FE8C4");
    private static readonly Guid IID_IDataObject = new("0000010E-0000-0000-C000-000000000046");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItem item);

    /// <summary>使うのは先頭の BindToHandler だけなので、後ろのメソッドは宣言しない（並びの先頭が合っていればよい）。</summary>
    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig]
        int BindToHandler(IntPtr bindContext, ref Guid bhid, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out object handler);
    }
}
