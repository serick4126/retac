using System.IO;
using System.Runtime.InteropServices;
using ReTAC.Domain.FileOps;

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
    /// <summary>R-125: 最初の項目の通知が来た時刻（Stopwatch の値）。転送が実際に始まるまでの時間を測る。</summary>
    public long? FirstItemTimestamp { get; private set; }

    /// <summary>
    /// R-125: 転送先のパス → 計画の時点の状態（登録した順）。登録の無い宛先（フォルダごと渡した中身など）は確かめない。
    /// 列で持つのは、同じ宛先へ 2 件送る計画（別のフォルダの同じ名前のファイル）があるため。
    /// 後の項目の「計画の時点の状態」は、先の項目を転送したあとの状態であり、1 件目の通知で 2 件目の状態と比べてはならない。
    /// </summary>
    private readonly Dictionary<string, Queue<DestinationState>> _expected = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>R-125: 計画の後に変わっていた宛先。見つけたら、以降の項目はすべて止める。</summary>
    public string? ChangedDestination { get; private set; }

    public void Expect(string target, DestinationState state)
    {
        if (!_expected.TryGetValue(target, out var queue)) _expected[target] = queue = new Queue<DestinationState>();
        queue.Enqueue(state);
    }

    public int PreMoveItem(uint flags, IntPtr item, IntPtr destinationFolder, string? newName) => Guard(item, destinationFolder, newName);
    public int PreCopyItem(uint flags, IntPtr item, IntPtr destinationFolder, string? newName) => Guard(item, destinationFolder, newName);

    /// <summary>
    /// R-125: OS が項目を転送する直前に、宛先の今の状態を計画の時点の状態と比べる。違えば失敗を返す。
    /// 失敗を返すと OS は残りの操作をすべて取り消す（1 件だけを飛ばす手段は無い）。取り消さない場合に備えて、
    /// 1 件見つけたあとは残りの通知でも失敗を返し続ける。返す値は「利用者の中止」（OS はエラーの画面を出さない）。
    /// </summary>
    private int Guard(IntPtr item, IntPtr destinationFolder, string? newName)
    {
        FirstItemTimestamp ??= System.Diagnostics.Stopwatch.GetTimestamp();
        if (ChangedDestination is not null) return Cancelled;
        if (_expected.Count == 0) return 0;
        // 通知の中で投げると OS の処理を止めてしまう。確かめられなかった項目は、そのまま通す（OS が自分の失敗として知らせる）
        try
        {
            if (PathOf(destinationFolder) is not { } folder) return 0;
            var name = newName is { Length: > 0 } ? newName : Path.GetFileName(PathOf(item));
            if (string.IsNullOrEmpty(name)) return 0;
            var target = Path.Combine(folder, name);
            if (!_expected.TryGetValue(target, out var queue) || queue.Count == 0) return 0;
            if (DestinationState.Of(target) == queue.Dequeue()) return 0;
            ChangedDestination = target;
            return Cancelled;
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return 0;
        }
    }

    /// <summary>COPYENGINE_E_USER_CANCELLED。</summary>
    private const int Cancelled = unchecked((int)0x80270000);

    public int PostMoveItem(uint flags, IntPtr item, IntPtr destinationFolder, string? newName, int hrMove, IntPtr created) =>
        Add(OperationKind.Move, item, hrMove, created);
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
