using System.Runtime.InteropServices;
using static ReTAC.Shell.NameSpaceTreeInterop;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace ReTAC.Shell;

/// <summary>R-146-1: パスを持たない項目（書庫の中など）1 件。IdList は VirtualItems が持つ独立した複製で、Dispose で解放する。</summary>
public sealed record VirtualItem(IntPtr IdList, string Name, string DisplayPath);

/// <summary>
/// R-146-1: ドロップ・貼り付けの中で、パスを持たない項目を覚える。
/// ドラッグのデータから作ったシェルの項目の一覧はデータを参照し続け、ドロップが終わるとデータが使えなくなりうる。
/// そのため、各項目の絶対の識別子の複製（SHGetIDListFromObject。呼び出し側が解放する独立したメモリ）を取り、一覧はその場で手放す。
/// </summary>
public sealed class VirtualItems : IDisposable
{
    /// <summary>CFSTR_SHELLIDLIST。FileDrop が無くこれがあれば、仮想項目の候補（ドラッグ中の表示はこれだけで判断する）。</summary>
    public const string ShellIdListFormat = "Shell IDList Array";

    private const uint SFGAO_FOLDER = 0x20000000, SFGAO_FILESYSTEM = 0x40000000, SFGAO_STREAM = 0x00400000;
    private const uint SIGDN_PARENTRELATIVEPARSING = 0x80018001, SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000;
    private static readonly Guid IID_IShellItemArray = new("B63EA76D-1F85-456F-A19C-48159EFA858B");

    private readonly List<VirtualItem> _items;
    private bool _disposed;

    private VirtualItems(List<VirtualItem> items) => _items = items;

    public IReadOnlyList<VirtualItem> Items => _items;

    /// <param name="comDataObject">COM の IDataObject（WinForms の DataObject も実装している）</param>
    /// <returns>全項目がファイルシステムの外で、ファイルかフォルダの中身を持つときだけ。1 件でも外れたら null（混在は扱わない）</returns>
    public static VirtualItems? FromDataObject(object comDataObject)
    {
        if (comDataObject is not ComDataObject data) return null;
        var iid = IID_IShellItemArray;
        if (SHCreateShellItemArrayFromDataObject(data, ref iid, out var array) != 0 || array == IntPtr.Zero) return null;
        try { return FromShellItemArray(array); }
        finally { Marshal.Release(array); }
    }

    /// <summary>ツリー（NSTC）はドロップでシェルの項目の一覧を渡す。一覧は呼び出し側のもので、ここでは参照を増やして読み、戻す。</summary>
    public static VirtualItems? FromShellItemArray(IntPtr arrayPointer)
    {
        if (arrayPointer == IntPtr.Zero) return null;
        var array = (IShellItemArray)Marshal.GetObjectForIUnknown(arrayPointer);
        var items = new List<VirtualItem>();
        try
        {
            if (array.GetCount(out var count) < 0 || count == 0) return null;
            for (uint i = 0; i < count; i++)
            {
                if (array.GetItemAt(i, out var item) < 0 || item is null) { Free(items); return null; }
                try
                {
                    if (Capture(item) is not { } captured) { Free(items); return null; }
                    items.Add(captured);
                }
                finally { Marshal.ReleaseComObject(item); }
            }
            return new VirtualItems(items);
        }
        catch { Free(items); throw; }
        finally { Marshal.ReleaseComObject(array); }
    }

    /// <summary>ツリーのドラッグ中の表示用。識別子の複製は作らず、属性だけを見る。</summary>
    public static bool IsVirtualArray(IntPtr arrayPointer)
    {
        if (arrayPointer == IntPtr.Zero) return false;
        var array = (IShellItemArray)Marshal.GetObjectForIUnknown(arrayPointer);
        try
        {
            if (array.GetCount(out var count) < 0 || count == 0) return false;
            for (uint i = 0; i < count; i++)
            {
                if (array.GetItemAt(i, out var item) < 0 || item is null) return false;
                try { if (!IsVirtual(item)) return false; }
                finally { Marshal.ReleaseComObject(item); }
            }
            return true;
        }
        finally { Marshal.ReleaseComObject(array); }
    }

    private static bool IsVirtual(IShellItem item)
    {
        // ファイルシステムの項目は今までどおりパスで扱う。中身を持たないもの（PC・コントロールパネルなど）は取り出せない
        if (item.GetAttributes(SFGAO_FILESYSTEM | SFGAO_STREAM | SFGAO_FOLDER, out var attributes) < 0) return false;
        return (attributes & SFGAO_FILESYSTEM) == 0 && (attributes & (SFGAO_STREAM | SFGAO_FOLDER)) != 0;
    }

    private static VirtualItem? Capture(IShellItem item)
    {
        if (!IsVirtual(item)) return null;
        if (DisplayName(item, SIGDN_PARENTRELATIVEPARSING) is not { Length: > 0 } name) return null;
        if (DisplayName(item, SIGDN_DESKTOPABSOLUTEPARSING) is not { Length: > 0 } display) return null;
        if (SHGetIDListFromObject(item, out var idList) != 0 || idList == IntPtr.Zero) return null;
        return new VirtualItem(idList, name, display);
    }

    private static string? DisplayName(IShellItem item, uint kind)
    {
        if (item.GetDisplayName(kind, out var pointer) < 0 || pointer == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(pointer); }
        finally { Marshal.FreeCoTaskMem(pointer); }
    }

    private static void Free(List<VirtualItem> items)
    {
        foreach (var item in items) Marshal.FreeCoTaskMem(item.IdList);
        items.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Free(_items);
    }

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHCreateShellItemArrayFromDataObject(ComDataObject dataObject, ref Guid riid, out IntPtr array);

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHGetIDListFromObject([MarshalAs(UnmanagedType.IUnknown)] object item, out IntPtr idList);
}
