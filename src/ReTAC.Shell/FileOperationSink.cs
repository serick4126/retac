using System.Runtime.InteropServices;

namespace ReTAC.Shell;

public enum OperationKind { Rename, Move, Copy }

/// <param name="Source">操作前のパス</param>
/// <param name="Created">操作後にできた項目のパス</param>
public sealed record OperationResult(OperationKind Kind, string Source, string Created);

/// <summary>
/// R-84: IFileOperation の項目ごとの結果を受け取る。頼んだ操作と起きた結果は一致しない
/// （衝突で飛ばした・途中で中止した・フォルダごと移した）ので、元に戻すの記録はここから作る。
/// 成功して項目ができたものだけを残す。中止した操作でも、それまでに成功した分は残る。
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class FileOperationSink : IFileOperationProgressSink
{
    private readonly List<OperationResult> _results = [];
    public IReadOnlyList<OperationResult> Results => _results;

    public int StartOperations() => 0;
    public int FinishOperations(int hrResult) => 0;
    public int PreRenameItem(uint flags, IntPtr item, string? newName) => 0;
    public int PostRenameItem(uint flags, IntPtr item, string? newName, int hrRename, IntPtr created) =>
        Add(OperationKind.Rename, item, hrRename, created);
    public int PreMoveItem(uint flags, IntPtr item, IntPtr destinationFolder, string? newName) => 0;
    public int PostMoveItem(uint flags, IntPtr item, IntPtr destinationFolder, string? newName, int hrMove, IntPtr created) =>
        Add(OperationKind.Move, item, hrMove, created);
    public int PreCopyItem(uint flags, IntPtr item, IntPtr destinationFolder, string? newName) => 0;
    public int PostCopyItem(uint flags, IntPtr item, IntPtr destinationFolder, string? newName, int hrCopy, IntPtr created) =>
        Add(OperationKind.Copy, item, hrCopy, created);
    public int PreDeleteItem(uint flags, IntPtr item) => 0;
    public int PostDeleteItem(uint flags, IntPtr item, int hrDelete, IntPtr created) => 0;   // 削除は第 2 段階
    public int PreNewItem(uint flags, IntPtr destinationFolder, string? newName) => 0;
    public int PostNewItem(uint flags, IntPtr destinationFolder, string? newName, string? templateName,
                           uint fileAttributes, int hrNew, IntPtr created) => 0;
    public int UpdateProgress(uint workTotal, uint workSoFar) => 0;
    public int ResetTimer() => 0;
    public int PauseTimer() => 0;
    public int ResumeTimer() => 0;

    private int Add(OperationKind kind, IntPtr item, int hr, IntPtr created)
    {
        // 通知の中で投げると OS の処理を止めてしまう。取れなかった項目は記録しないだけにする
        try
        {
            if (hr < 0 || created == IntPtr.Zero) return 0;
            if (PathOf(item) is { } source && PathOf(created) is { } target)
                _results.Add(new OperationResult(kind, source, target));
        }
        catch (COMException) { }
        return 0;
    }

    private static string? PathOf(IntPtr unknown)
    {
        if (unknown == IntPtr.Zero) return null;
        var item = (IShellItemName)Marshal.GetObjectForIUnknown(unknown);
        try
        {
            if (item.GetDisplayName(SIGDN_FILESYSPATH, out var name) < 0 || name == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUni(name); }
            finally { Marshal.FreeCoTaskMem(name); }
        }
        finally { Marshal.ReleaseComObject(item); }
    }

    private const uint SIGDN_FILESYSPATH = 0x80058000;

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemName
    {
        [PreserveSig] int BindToHandler(IntPtr bindContext, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetParent(out IntPtr parent);
        [PreserveSig] int GetDisplayName(uint sigdnName, out IntPtr name);
    }
}

/// <summary>メソッドの順序は shobjidl の宣言と同じでなければならない（vtable の並び）。</summary>
[ComImport, Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFileOperationProgressSink
{
    [PreserveSig] int StartOperations();
    [PreserveSig] int FinishOperations(int hrResult);
    [PreserveSig] int PreRenameItem(uint flags, IntPtr item, [MarshalAs(UnmanagedType.LPWStr)] string? newName);
    [PreserveSig] int PostRenameItem(uint flags, IntPtr item, [MarshalAs(UnmanagedType.LPWStr)] string? newName, int hrRename, IntPtr created);
    [PreserveSig] int PreMoveItem(uint flags, IntPtr item, IntPtr destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? newName);
    [PreserveSig] int PostMoveItem(uint flags, IntPtr item, IntPtr destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? newName, int hrMove, IntPtr created);
    [PreserveSig] int PreCopyItem(uint flags, IntPtr item, IntPtr destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? newName);
    [PreserveSig] int PostCopyItem(uint flags, IntPtr item, IntPtr destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? newName, int hrCopy, IntPtr created);
    [PreserveSig] int PreDeleteItem(uint flags, IntPtr item);
    [PreserveSig] int PostDeleteItem(uint flags, IntPtr item, int hrDelete, IntPtr created);
    [PreserveSig] int PreNewItem(uint flags, IntPtr destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? newName);
    [PreserveSig] int PostNewItem(uint flags, IntPtr destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? newName,
        [MarshalAs(UnmanagedType.LPWStr)] string? templateName, uint fileAttributes, int hrNew, IntPtr created);
    [PreserveSig] int UpdateProgress(uint workTotal, uint workSoFar);
    [PreserveSig] int ResetTimer();
    [PreserveSig] int PauseTimer();
    [PreserveSig] int ResumeTimer();
}
