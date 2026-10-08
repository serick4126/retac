using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ReTAC.App;

/// <summary>
/// R-146-1: 書庫の中の項目を、新しく作った空の一時フォルダへ取り出し、続きの処理（今までの転送の本体）を済ませてから片付ける。
/// <b>続きの処理は同期で最後まで行うこと。</b>中で後に回すと、転送が始まる前に一時フォルダが消える。後に回すなら、Run の呼び出し全体を回す。
/// </summary>
internal static class ArchiveExtraction
{
    public const string FolderPrefix = "ReTAC-";

    /// <param name="tempRoot">一時フォルダを作る場所（製品では Path.GetTempPath()）</param>
    /// <param name="names">落とした項目の名前。取り出す前に確かめ、取り出した直下の項目と突き合わせる（S6）</param>
    /// <param name="displayPaths">衝突の確認に出す書庫の中のパス。names と同じ順</param>
    /// <param name="extract">一時フォルダへ取り出す。すべて終われば true、中止なら false。失敗は例外</param>
    /// <param name="next">取り出した項目を転送元にした続きの処理</param>
    /// <returns>続きの処理まで進んだら true。中断したら false（宛先には何も渡していない。S3）</returns>
    public static bool Run(string tempRoot, IReadOnlyList<string> names, IReadOnlyList<string> displayPaths,
        Func<string, bool> extract, Action<ExtractedFolder> next) =>
        Run(tempRoot, names, displayPaths, extract, next, ExtractedFolder.Match);

    /// <summary>テストの入口: 照合の関数を差し替える（結果を読む所の失敗が、Run を通して中断になることを確かめるため）。製品は ExtractedFolder.Match を渡す。</summary>
    internal static bool Run(string tempRoot, IReadOnlyList<string> names, IReadOnlyList<string> displayPaths,
        Func<string, bool> extract, Action<ExtractedFolder> next,
        Func<string, IReadOnlyList<string>, IReadOnlyList<string>, ExtractedFolder?> match,
        Action<OwnedTempFolder>? afterCreate = null)
    {
        // S6: 取り出す前に名前を確かめる。書庫の中の名前で外へ書き出さないことは Windows に頼る（取り出した後の検査では防げない）
        if (names.Count == 0 || names.Count != displayPaths.Count || !names.All(IsSingleName)) return false;
        // S6: 名前の重複（大小文字だけの違いを含む）は取り出す前に断る。重複を許すと、実際の結果が別名（x (2).txt）になっても
        // 数だけ合って照合が通り、同じ転送元が 2 回並ぶ（R-146-1）
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count) return false;
        if (OwnedTempFolder.TryCreate(tempRoot) is not { } owned) return false;   // S4

        try
        {
            afterCreate?.Invoke(owned);   // テストの入口: 取り出しの前に、パスの途中のリンクを切り替える（製品では使わない）
            ExtractedFolder? extracted;
            try
            {
                if (!extract(owned.FullPath)) return false;   // S3: 1 件でも終わらなければ何も送らない
                // S4・S8: 保存したパスが今も固定したフォルダを指していることを、取り出しの後・照合の前に確かめる。パスの途中（%TEMP% など）のリンクが
                // 切り替わっていると、パスは別の場所の同名のフォルダを指す。そこを照合して転送すると、別の場所のファイルを送ってしまう。
                // この確認は送る物の取り違えを防ぐだけで、削除には使わない（片付けはパスを使わず、固定したハンドルだけで行う）。
                // 確認の後で切り替わる窓は残るが、その場合も消えるのは固定したフォルダの中だけで、別の場所は変わらない。
                // 取り出し（IFileOperation）自体はパスへ書くので、切り替わっていれば別の場所へ書き出しうる。それは新しく書くだけで、消さずにそのまま残す（S8）
                if (!owned.PathStillPointsHere()) return false;
                // S6: 送るものの取り違えを防ぐ照合。結果を読む所の失敗も、取り出しの失敗と同じく中断にする（R-146-1）
                extracted = match(owned.FullPath, names, displayPaths);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or COMException)
            {
                return false;   // R-146-1: 普通のファイル操作の失敗。知らせは Windows の画面に任せる（R-126 に載せない）
            }
            if (extracted is null) return false;
            next(extracted);   // ここの例外は ReTAC の不具合なので受け止めない（片付けは finally で行う）
            return true;
        }
        finally { owned.Delete(); }   // 安全: S4。この処理が作ったフォルダだけ（作ったときの識別子と照合して消す）
    }

    /// <summary>S6: 1 つの名前として正しいか（区切り・ドライブ・「.」「..」を含まない）。</summary>
    internal static bool IsSingleName(string name) =>
        name.Length > 0 && name is not ("." or "..") && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
}

/// <summary>
/// S4: この処理が新しく作った一時フォルダ。作成と同時に得たハンドルを、片付けが終わるまで持ち続ける。
/// 持っている間は、ほかからこのフォルダを移す・名前を変えることができない（共有に DELETE を許さない）ので、
/// 確認したフォルダと消すフォルダが別の物になる窓が無い。パスや名前の形だけでは消さない（同じ形の、ほかが作ったフォルダを消さないため）。
/// </summary>
internal sealed class OwnedTempFolder
{
    /// <summary>テストの入口: 片付けの途中で、置き換えを試みる時点</summary>
    internal enum DeletePhase { AfterOwnership, DuringScan, BeforeRemove }

    private readonly (uint Volume, ulong Index) _id;
    private readonly SafeFileHandle _handle;

    private OwnedTempFolder(string fullPath, (uint Volume, ulong Index) id, SafeFileHandle handle) => (FullPath, _id, _handle) = (fullPath, id, handle);

    public string FullPath { get; }

    /// <summary>テストの確認用: 片付けの各時点で呼ばれる（製品では使わない）</summary>
    internal Action<DeletePhase>? Hook { get; set; }

    public static OwnedTempFolder? TryCreate(string tempRoot) =>
        TryCreate(tempRoot, ArchiveExtraction.FolderPrefix + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 名前を決めて作る（テストの入口）。作成とハンドルの取得を NtCreateFile の 1 回の呼び出しで行う（FILE_CREATE。既にあれば失敗）。
    /// S4・S8: 所有の根拠は「この呼び出しが作り、そのハンドルを持っている」こと。作ってから別の呼び出しで開くと、その間にほかの
    /// フォルダへ置き換わったとき、置き換わった先の識別子を保存して所有扱いしてしまう（空かどうかは自分が作った証明にならない）。
    /// 識別子は作成で得たハンドルから取る。tempRoot が無いときは親を開けず失敗し、親は作らない。
    /// 識別子が使えない・リンクなら使わない（ハンドルは閉じるが、作ったフォルダは消さない。S8）。
    /// </summary>
    /// <param name="afterCreate">テストの入口: 作成の直後、識別子を取る前に呼ばれる（引数は作ったフォルダのパス。製品では使わない）</param>
    internal static OwnedTempFolder? TryCreate(string tempRoot, string name, Action<string>? afterCreate = null)
    {
        SafeFileHandle? handle = null;
        try
        {
            var root = Path.GetFullPath(tempRoot);
            var fullPath = Path.Combine(root, name);
            handle = CreateNew(root, name);
            if (handle is null) return null;
            afterCreate?.Invoke(fullPath);
            if (InfoOf(handle) is not { } info || (info.Attributes & (uint)FileAttributes.ReparsePoint) != 0) return null;
            var id = (info.Volume, info.Index);
            if (!IsUsable(id)) return null;
            var owned = new OwnedTempFolder(fullPath, id, handle);
            handle = null;   // 持ち主が OwnedTempFolder に移った（Delete が閉じる）
            return owned;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
        finally { handle?.Dispose(); }
    }

    /// <summary>
    /// tempRoot の直下に、名前のフォルダを新しく作って、そのハンドルを返す（既にあれば null）。
    /// 親は CreateFile で開いて RootDirectory に渡し、ObjectName には相対の名前だけを渡す（DOS のパスを NT のパスへ変える処理が要らず、UNC でも動く）。
    /// 権限は DELETE（自分で消すため）・FILE_READ_ATTRIBUTES・SYNCHRONIZE、共有は READ・WRITE だけ。DELETE を許さないので、
    /// 持っている間はほかから移す・名前を変える・消すことができない（S4）。
    /// </summary>
    private static SafeFileHandle? CreateNew(string root, string name)
    {
        using var parent = CreateFile(root, FILE_LIST_DIRECTORY | FILE_ADD_SUBDIRECTORY, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (parent.IsInvalid) return null;
        // FILE_LIST_DIRECTORY: 片付けで、このハンドルから直下の一覧を取るため（パスで列挙しない）
        return NtOpen(parent, name, DELETE | FILE_READ_ATTRIBUTES | FILE_LIST_DIRECTORY | SYNCHRONIZE, FILE_ATTRIBUTE_DIRECTORY,
            FILE_SHARE_READ | FILE_SHARE_WRITE, FILE_CREATE, FILE_DIRECTORY_FILE | FILE_SYNCHRONOUS_IO_NONALERT | FILE_OPEN_FOR_BACKUP_INTENT | FILE_OPEN_REPARSE_POINT);
    }

    /// <summary>親のハンドルからの相対の名前を NtCreateFile で開く（または作る）。リンクはたどらない指定は呼び出し側が options に入れる。</summary>
    private static SafeFileHandle? NtOpen(SafeFileHandle parent, string name, uint access, uint fileAttributes, uint share, uint disposition, uint options)
    {
        var buffer = Marshal.StringToHGlobalUni(name);
        var unicode = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
        try
        {
            var length = checked((ushort)(name.Length * 2));
            Marshal.StructureToPtr(new UnicodeString { Length = length, MaximumLength = length, Buffer = buffer }, unicode, false);
            var attributes = new ObjectAttributes
            {
                Length = (uint)Marshal.SizeOf<ObjectAttributes>(), RootDirectory = parent.DangerousGetHandle(), ObjectName = unicode,
                Attributes = OBJ_CASE_INSENSITIVE,
            };
            var status = NtCreateFile(out var raw, access, ref attributes, out _, IntPtr.Zero, fileAttributes, share, disposition, options, IntPtr.Zero, 0);
            return status >= 0 ? new SafeFileHandle(raw, ownsHandle: true) : null;
        }
        finally { Marshal.FreeHGlobal(unicode); Marshal.FreeHGlobal(buffer); }
    }

    /// <summary>S8: 識別子が取れていない（0, 0）ものは照合に使えない。</summary>
    internal static bool IsUsable((uint Volume, ulong Index) id) => id != (0u, 0ul);

    /// <summary>テストの確認用: 作ったときの識別子</summary>
    internal (uint Volume, ulong Index) Identity => _id;

    /// <summary>
    /// 持っているハンドルの先が、作ったフォルダのまま（同じ識別子・リンクでない）なら中身ごと消す。違えば何もしない。消せなくても無視する。
    /// どの場合も最後にハンドルを閉じる。
    /// </summary>
    public bool Delete()
    {
        try
        {
            // 照合はパスで開き直さず、固定しているハンドルで行う（開き直すと、その間にパスが別の物に変わりうる）
            if (InfoOf(_handle) is not { } now || (now.Volume, now.Index) != _id || (now.Attributes & (uint)FileAttributes.ReparsePoint) != 0) return false;
            Hook?.Invoke(DeletePhase.AfterOwnership);
            // S4・S8: 中身の一覧・属性の変更・削除は、すべて固定した根のハンドルからの相対で行い、パス（FullPath）は一切使わない。
            // パスは途中（%TEMP% など）のリンクが切り替わると別の場所を指す。一覧や削除にパスを使うと、根のハンドルは元のフォルダのまま、
            // 中身の処理だけが別の場所へ向いてしまう。確認の直前に照合し直しても、その後の切り替えを防げない。
            // ハンドルからの相対なら、パスが何を指していても、触れるのは固定したフォルダの中の物だけになる。
            var cleared = ClearChildren(_handle, () => Hook?.Invoke(DeletePhase.DuringScan), 0);
            Hook?.Invoke(DeletePhase.BeforeRemove);
            return cleared && SetDisposition(_handle);   // 根は、照合した固定のハンドルで、空になってから消す
        }
        catch (Exception) { return false; }   // R-146-1: finally から呼ばれる。ここの例外で Run の結果や続きの処理の例外を隠さない
        finally { _handle.Dispose(); }
    }

    /// <summary>確認用: 保存したパスが今も固定したフォルダ（同じ識別子）を指しているか。送る物の取り違えを防ぐ確認で、削除には使わない。</summary>
    internal bool PathStillPointsHere()
    {
        using var opened = CreateFile(FullPath, FILE_READ_ATTRIBUTES, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        return !opened.IsInvalid && InfoOf(opened) is { } now && (now.Volume, now.Index) == _id;
    }

    private const int MaxDepth = 128;   // 異常に深い入れ子で呼び出しの深さが尽きないように。超えたら残して false

    /// <summary>
    /// ハンドルのフォルダの直下の物をすべて消す（入れ子は再帰）。消せない物は残して先へ進み、1 つでも残れば false。
    /// 一覧は先に全部取る（消しながら列挙しない）。隠し・システム属性のものも含む。
    /// </summary>
    private static bool ClearChildren(SafeFileHandle directory, Action? onFirst, int depth)
    {
        if (depth > MaxDepth || ListChildren(directory) is not { } children) return false;
        var ok = true;
        foreach (var (name, listed) in children)
        {
            onFirst?.Invoke();
            onFirst = null;
            ok &= RemoveChild(directory, name, listed, depth);
        }
        return ok;
    }

    /// <summary>
    /// 親のハンドルからの相対の名前で子を開く（FILE_OPEN。作らない）。リンクはたどらず、リンクそのものを開く。
    /// 子が普通のフォルダなら中を処理して空にし、リンクならたどらずリンクそのものだけを消す。
    /// 読み取り専用の属性は開いたハンドルで外す。
    /// </summary>
    private static bool RemoveChild(SafeFileHandle parent, string name, uint listed, int depth)
    {
        var asDirectory = (listed & (uint)FileAttributes.Directory) != 0 && (listed & (uint)FileAttributes.ReparsePoint) == 0;
        var access = DELETE | FILE_READ_ATTRIBUTES | FILE_WRITE_ATTRIBUTES | SYNCHRONIZE | (asDirectory ? FILE_LIST_DIRECTORY : 0);
        // 読み取り専用の物は、DELETE を付けて開くと拒否される。属性だけ外せる権限で開いて外し、同じ物（識別子が同じ）を DELETE 付きで開き直す
        using var child = (listed & (uint)FileAttributes.ReadOnly) != 0 ? OpenReadOnlyForDelete(parent, name, access) : OpenChild(parent, name, access);
        if (child is null || InfoOf(child) is not { } info) return false;
        if ((info.Attributes & (uint)FileAttributes.ReadOnly) != 0 && !ClearReadOnly(child)) return false;
        if ((info.Attributes & (uint)FileAttributes.ReparsePoint) == 0 && (info.Attributes & (uint)FileAttributes.Directory) != 0
            && !ClearChildren(child, null, depth + 1)) return false;
        return SetDisposition(child);
    }

    private static SafeFileHandle? OpenChild(SafeFileHandle parent, string name, uint access) =>
        NtOpen(parent, name, access, 0, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, FILE_OPEN,
            FILE_SYNCHRONOUS_IO_NONALERT | FILE_OPEN_FOR_BACKUP_INTENT | FILE_OPEN_REPARSE_POINT);

    private static SafeFileHandle? OpenReadOnlyForDelete(SafeFileHandle parent, string name, uint access)
    {
        using var first = OpenChild(parent, name, FILE_READ_ATTRIBUTES | FILE_WRITE_ATTRIBUTES | SYNCHRONIZE);
        if (first is null || InfoOf(first) is not { } before) return null;
        if ((before.Attributes & (uint)FileAttributes.ReadOnly) != 0 && !ClearReadOnly(first)) return null;
        var second = OpenChild(parent, name, access);
        // 開き直す間に別の物に変わっていたら使わない（S8: 識別子を照合した物だけを消す）
        if (second is null || InfoOf(second) is not { } after || (after.Volume, after.Index) != (before.Volume, before.Index))
        {
            second?.Dispose();
            return null;
        }
        return second;
    }

    /// <summary>ハンドルの先の読み取り専用を外す（属性を NORMAL にする。時刻は変えない）。パスでは属性を変えない。</summary>
    private static bool ClearReadOnly(SafeFileHandle handle)
    {
        var basic = new FileBasicInfo { FileAttributes = FILE_ATTRIBUTE_NORMAL };
        return SetFileInformationByHandle(handle, FileBasicInfoClass, ref basic, (uint)Marshal.SizeOf<FileBasicInfo>());   // 安全: S4。識別子を照合して固定した、作ったフォルダの中の物の属性だけ
    }

    private static bool SetDisposition(SafeFileHandle handle)
    {
        var disposition = new FileDispositionInfo { DeleteFile = 1 };
        return SetFileInformationByHandle(handle, FileDispositionInfoClass, ref disposition, 1);   // 安全: S4。識別子を照合した固定のハンドル、またはそこからの相対で開いた、作ったフォルダの中の物だけを消す
    }

    /// <summary>ハンドルのフォルダの直下の名前と属性の一覧（「.」「..」は除く）。取れなければ null。</summary>
    private static List<(string Name, uint Attributes)>? ListChildren(SafeFileHandle directory)
    {
        const int size = 64 * 1024, ErrorNoMoreFiles = 18;
        var result = new List<(string Name, uint Attributes)>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            var infoClass = FileFullDirectoryRestartInfoClass;
            while (true)
            {
                if (!GetFileInformationByHandleEx(directory, infoClass, buffer, size))
                    return Marshal.GetLastWin32Error() == ErrorNoMoreFiles ? result : null;
                infoClass = FileFullDirectoryInfoClass;
                // FILE_FULL_DIR_INFO（64 ビット）: NextEntryOffset@0・FileAttributes@56・FileNameLength@60（バイト）・FileName@68
                for (var offset = 0; ;)
                {
                    var next = Marshal.ReadInt32(buffer, offset);
                    var attributes = (uint)Marshal.ReadInt32(buffer, offset + 56);
                    var name = Marshal.PtrToStringUni(buffer + offset + 68, Marshal.ReadInt32(buffer, offset + 60) / 2);
                    if (name is not ("." or "..")) result.Add((name, attributes));
                    if (next == 0) break;
                    offset += next;
                }
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static (uint Volume, ulong Index, uint Attributes)? InfoOf(SafeFileHandle handle) =>
        GetFileInformationByHandle(handle, out var info)
            ? (info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow, info.FileAttributes)
            : null;

    private const uint DELETE = 0x10000, SYNCHRONIZE = 0x100000, FILE_READ_ATTRIBUTES = 0x80, FILE_LIST_DIRECTORY = 0x1, FILE_ADD_SUBDIRECTORY = 0x4;
    private const uint FILE_SHARE_READ = 0x1, FILE_SHARE_WRITE = 0x2, FILE_SHARE_DELETE = 0x4, OPEN_EXISTING = 3, FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    // NtCreateFile: 作成のみ（既にあれば失敗）。ディレクトリとして・同期で・バックアップ意図で・リンクをたどらずに開く
    private const uint FILE_CREATE = 2, OBJ_CASE_INSENSITIVE = 0x40;
    private const uint FILE_DIRECTORY_FILE = 0x1, FILE_SYNCHRONOUS_IO_NONALERT = 0x20, FILE_OPEN_FOR_BACKUP_INTENT = 0x4000, FILE_OPEN_REPARSE_POINT = 0x200000;
    // 子を開く（FILE_OPEN。作らない）。属性を外す権限と、通常の属性
    private const uint FILE_OPEN = 1, FILE_WRITE_ATTRIBUTES = 0x100, FILE_ATTRIBUTE_NORMAL = 0x80;
    private const int FileBasicInfoClass = 0, FileDispositionInfoClass = 4, FileFullDirectoryInfoClass = 14, FileFullDirectoryRestartInfoClass = 15;

    // 64 ビットでは 40 バイト（時刻 4 つ 32・FileAttributes 4・詰め物 4）。時刻は 0（変更しない）にする
    [StructLayout(LayoutKind.Sequential)]
    private struct FileBasicInfo { public long CreationTime, LastAccessTime, LastWriteTime, ChangeTime; public uint FileAttributes; }

    // 64 ビットでは 16 バイト（Length 2・MaximumLength 2・詰め物 4・Buffer 8）。Length はバイト数で、終端の NUL を含めない
    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString { public ushort Length, MaximumLength; public IntPtr Buffer; }

    // 64 ビットでは 48 バイト（Length 4・詰め物 4・RootDirectory 8・ObjectName 8・Attributes 4・詰め物 4・SecurityDescriptor 8・SecurityQualityOfService 8）
    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes
    {
        public uint Length;
        public IntPtr RootDirectory, ObjectName;   // ObjectName は UnicodeString へのポインター
        public uint Attributes;
        public IntPtr SecurityDescriptor, SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock { public IntPtr Status; public UIntPtr Information; }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInfo { public byte DeleteFile; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    // 作成とハンドルの取得を 1 回で行う（FILE_CREATE。消す・移す・上書きする指定は使わない）。戻り値は NTSTATUS（0 以上が成功）
    [DllImport("ntdll.dll")]
    private static extern int NtCreateFile(out IntPtr handle, uint access, ref ObjectAttributes attributes, out IoStatusBlock status,
        IntPtr allocationSize, uint fileAttributes, uint share, uint disposition, uint options, IntPtr extendedAttributes, uint extendedAttributesLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation info);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, IntPtr buffer, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int infoClass, ref FileDispositionInfo info, uint size);   // 安全: S4。宣言（呼び出しは SetDisposition だけ）

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "SetFileInformationByHandle")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int infoClass, ref FileBasicInfo info, uint size);   // 安全: S4。宣言（呼び出しは ClearReadOnly だけ）
}

/// <summary>R-146-1: 取り出した一時フォルダ。転送元（Paths）と、衝突の確認に出す書庫の中のパスへの読み替え。</summary>
internal sealed class ExtractedFolder
{
    private readonly IReadOnlyList<string> _displayPaths;

    private ExtractedFolder(string folder, IReadOnlyList<string> paths, IReadOnlyList<string> displayPaths) =>
        (Folder, Paths, _displayPaths) = (folder, paths, displayPaths);

    public string Folder { get; }

    /// <summary>転送元。一時フォルダの直下の、落とした項目と同じ名前の項目（落とした順）</summary>
    public IReadOnlyList<string> Paths { get; }

    /// <summary>S6: 直下の項目が、落とした項目と数・名前で一致するときだけ作る。</summary>
    internal static ExtractedFolder? Match(string folder, IReadOnlyList<string> names, IReadOnlyList<string> displayPaths)
    {
        if (names.Count == 0 || names.Count != displayPaths.Count) return null;
        // S6: 一対一の照合。名前の重複があれば作らない（Run が先に断るが、Match 単独でも守る）。
        // 数が同じで互いに対応するときだけ通す（x.txt, x.txt と実際の x.txt, x (2).txt を通さない）
        var expected = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (expected.Count != names.Count) return null;
        var actual = Directory.EnumerateFileSystemEntries(folder).Select(p => Path.GetFileName(p)!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (actual.Count != names.Count || !expected.SetEquals(actual)) return null;
        return new ExtractedFolder(folder, [.. names.Select(name => Path.Combine(folder, name))], displayPaths);
    }

    /// <summary>一時フォルダの中のパスを書庫の中のパスに読み替える（表示だけ）。一時フォルダの外はそのまま。</summary>
    public string Display(string path)
    {
        for (var i = 0; i < Paths.Count; i++)
        {
            if (string.Equals(path, Paths[i], StringComparison.OrdinalIgnoreCase)) return _displayPaths[i];
            if (path.StartsWith(Paths[i] + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return _displayPaths[i] + path[Paths[i].Length..];
        }
        return path;
    }

    /// <summary>このフォルダの中のパスか（R-62 の別名複写を出さない判定・S5）</summary>
    public bool Contains(string path) =>
        path.StartsWith(Folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
