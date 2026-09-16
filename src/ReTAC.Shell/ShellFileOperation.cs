using System.IO;
using System.Runtime.InteropServices;

namespace ReTAC.Shell;

/// <summary>
/// R-41 ③: 実際の転送・進捗・エラー処理は OS（IFileOperation）に委ねる。
/// 進捗表示と中断、再試行／スキップ／中止、ごみ箱、長いパス、権限昇格、
/// 他プロセスによるロックの検出（R-41-2）がこれで手に入る。
/// <b>どのファイルを渡すかの判定は呼び出し側（R-41-4 の複写条件）が済ませていること。</b>
/// </summary>
public sealed class ShellFileOperation : IDisposable
{
    private readonly IFileOperation _operation;
    private bool _hasWork;

    /// <param name="owner">進捗・確認ダイアログの親</param>
    /// <param name="silentOverwrite">
    /// 衝突の確認を OS に出させない。複写条件を自前で判定済みの転送で使う（R-41-4）。
    /// </param>
    public ShellFileOperation(IntPtr owner, bool silentOverwrite)
    {
        var type = Type.GetTypeFromCLSID(CLSID_FileOperation)
            ?? throw new InvalidOperationException("IFileOperation を作成できません。");
        _operation = (IFileOperation)Activator.CreateInstance(type)!;
        Check(_operation.SetOwnerWindow(owner));

        var flags = FOF_ALLOWUNDO;                       // R-19: 削除はごみ箱経由
        if (silentOverwrite) flags |= FOF_NOCONFIRMATION;
        Check(_operation.SetOperationFlags(flags));
    }

    /// <param name="newName">別名で複写する場合の名前。元の名前のままなら null</param>
    public void Copy(string source, string destinationFolder, string? newName = null)
    {
        Check(_operation.CopyItem(Item(source), Item(destinationFolder), newName, IntPtr.Zero));
        _hasWork = true;
    }

    public void Move(string source, string destinationFolder, string? newName = null)
    {
        Check(_operation.MoveItem(Item(source), Item(destinationFolder), newName, IntPtr.Zero));
        _hasWork = true;
    }

    /// <summary>R-19 / R-44: ごみ箱へ送る。確認は Windows 標準のものが出る。</summary>
    public void Delete(string path)
    {
        Check(_operation.DeleteItem(Item(path), IntPtr.Zero));
        _hasWork = true;
    }

    public void Rename(string path, string newName)
    {
        Check(_operation.RenameItem(Item(path), newName, IntPtr.Zero));
        _hasWork = true;
    }

    /// <returns>すべて転送できたら true。利用者の中断・OS 側の失敗はいずれも false。</returns>
    public bool Execute()
    {
        if (!_hasWork) return true;
        var hr = _operation.PerformOperations();
        _operation.GetAnyOperationsAborted(out var aborted);

        // 利用者の中断は失敗ではない。戻り値 false で伝えるだけにする
        if (IsCancelled(hr)) return false;
        Check(hr);
        return !aborted;
    }

    /// <summary>
    /// 中止を表す HRESULT。コピーエンジンは ERROR_CANCELLED ではなく
    /// COPYENGINE_E_USER_CANCELLED を返す。Esc で閉じたときはこちらが来る
    /// （実機で 0x80270000 を確認）。
    /// </summary>
    private static bool IsCancelled(int hr) => hr is HRESULT_CANCELLED or COPYENGINE_E_USER_CANCELLED;

    /// <summary>
    /// IFileOperation は [PreserveSig] なので、投げずに HRESULT を返す。
    /// 見ないと「1 件も転送されていないのに成功した」ことになる。
    /// 呼び出し側は IOException を catch しているので、そこへ寄せる。
    /// </summary>
    private static void Check(int hr)
    {
        if (hr >= 0) return;
        var reason = Marshal.GetExceptionForHR(hr)?.Message;
        throw new IOException(reason ?? $"ファイル操作に失敗しました。(0x{hr:X8})", hr);
    }

    public void Dispose() => Marshal.ReleaseComObject(_operation);

    private static IShellItem Item(string path)
    {
        var guid = IID_IShellItem;
        // P-14 の教訓: 戻りのインタフェースは out IntPtr で受けてから包み直す
        var hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref guid, out var unknown);
        if (hr != 0 || unknown == IntPtr.Zero) throw new IOException($"{path} を解決できません。(0x{hr:X})");

        var item = (IShellItem)Marshal.GetObjectForIUnknown(unknown);
        Marshal.Release(unknown);
        return item;
    }

    private static readonly Guid CLSID_FileOperation = new("3AD05575-8857-4850-9277-11B85BDB8E09");
    private static Guid IID_IShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    /// <summary>HRESULT_FROM_WIN32(ERROR_CANCELLED)。利用者が中断したときに返る。</summary>
    private const int HRESULT_CANCELLED = unchecked((int)0x800704C7);

    /// <summary>コピーエンジンが返す中止。OS の確認ダイアログを Esc で閉じるとこれ。</summary>
    private const int COPYENGINE_E_USER_CANCELLED = unchecked((int)0x80270000);

    private const uint FOF_NOCONFIRMATION = 0x0010;
    private const uint FOF_ALLOWUNDO = 0x0040;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext,
        ref Guid riid, out IntPtr item);

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr bindContext, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetParent(out IShellItem parent);
        [PreserveSig] int GetDisplayName(uint sigdnName, out IntPtr name);
        [PreserveSig] int GetAttributes(uint mask, out uint attributes);
        [PreserveSig] int Compare(IShellItem other, uint hint, out int order);
    }

    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        [PreserveSig] int Advise(IntPtr sink, out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int SetOperationFlags(uint flags);
        [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        [PreserveSig] int SetProgressDialog(IntPtr popupDialog);
        [PreserveSig] int SetProperties(IntPtr propertyChangeArray);
        [PreserveSig] int SetOwnerWindow(IntPtr owner);
        [PreserveSig] int ApplyPropertiesToItem(IShellItem item);
        [PreserveSig] int ApplyPropertiesToItems(IntPtr items);
        [PreserveSig] int RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string newName, IntPtr sink);
        [PreserveSig] int RenameItems(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string newName);
        [PreserveSig] int MoveItem(IShellItem item, IShellItem destinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string? newName, IntPtr sink);
        [PreserveSig] int MoveItems(IntPtr items, IShellItem destinationFolder);
        [PreserveSig] int CopyItem(IShellItem item, IShellItem destinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string? copyName, IntPtr sink);
        [PreserveSig] int CopyItems(IntPtr items, IShellItem destinationFolder);
        [PreserveSig] int DeleteItem(IShellItem item, IntPtr sink);
        [PreserveSig] int DeleteItems(IntPtr items);
        [PreserveSig] int NewItem(IShellItem destinationFolder, uint fileAttributes,
            [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string? templateName, IntPtr sink);
        [PreserveSig] int PerformOperations();
        [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
}
