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
        Func<string, IReadOnlyList<string>, IReadOnlyList<string>, ExtractedFolder?> match)
    {
        // S6: 取り出す前に名前を確かめる。書庫の中の名前で外へ書き出さないことは Windows に頼る（取り出した後の検査では防げない）
        if (names.Count == 0 || names.Count != displayPaths.Count || !names.All(IsSingleName)) return false;
        // S6: 名前の重複（大小文字だけの違いを含む）は取り出す前に断る。重複を許すと、実際の結果が別名（x (2).txt）になっても
        // 数だけ合って照合が通り、同じ転送元が 2 回並ぶ（R-146-1）
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count) return false;
        if (OwnedTempFolder.TryCreate(tempRoot) is not { } owned) return false;   // S4

        try
        {
            ExtractedFolder? extracted;
            try
            {
                if (!extract(owned.FullPath)) return false;   // S3: 1 件でも終わらなければ何も送らない
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
            var status = NtCreateFile(out var raw, DELETE | FILE_READ_ATTRIBUTES | SYNCHRONIZE, ref attributes, out _, IntPtr.Zero,
                FILE_ATTRIBUTE_DIRECTORY, FILE_SHARE_READ | FILE_SHARE_WRITE, FILE_CREATE,
                FILE_DIRECTORY_FILE | FILE_SYNCHRONOUS_IO_NONALERT | FILE_OPEN_FOR_BACKUP_INTENT | FILE_OPEN_REPARSE_POINT, IntPtr.Zero, 0);
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
            // 読み取り専用のまま取り出されたファイルがあると Directory.Delete が止まる。
            // 中のリンク（再解析ポイント）へはたどらず、属性も触らない。
            // 隠し・システム属性のものも見る（既定では飛ばされ、読み取り専用が外されず一時フォルダが残る）。
            // フォルダのリンク（ジャンクション）が中にあると、再帰の削除がアクセス拒否で止まるので、リンクそのものだけを先に外す（リンク先は消えない）。
            // 根は固定されているので、パスはずっと同じフォルダの中を指す
            var info = new DirectoryInfo(FullPath);
            var links = new List<string>();
            var pending = new Stack<DirectoryInfo>();
            pending.Push(info);
            var first = true;
            while (pending.Count > 0)
                foreach (var entry in pending.Pop().EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }))
                {
                    if (first) { first = false; Hook?.Invoke(DeletePhase.DuringScan); }
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) { if (entry is DirectoryInfo) links.Add(entry.FullName); }
                    else if (entry is DirectoryInfo dir) pending.Push(dir);
                    else if ((entry.Attributes & FileAttributes.ReadOnly) != 0) entry.Attributes &= ~FileAttributes.ReadOnly;
                }
            foreach (var link in links) Remove(link, recursive: false);   // 安全: S4。作ったフォルダの中のリンクそのもの（非再帰。リンク先へはたどらない）
            // 根は自分のハンドルが消すので、直下の項目を 1 つずつ消す（根をパスで消すと、共有に DELETE を許さない自分のハンドルで失敗する）
            foreach (var entry in info.EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }).ToList())
                Remove(entry.FullName, recursive: true);   // 安全: S4。固定した、作ったフォルダの直下の項目だけ（リンクはリンクそのもの）
            Hook?.Invoke(DeletePhase.BeforeRemove);
            var disposition = new FileDispositionInfo { DeleteFile = 1 };
            return SetFileInformationByHandle(_handle, FileDispositionInfoClass, ref disposition, 1);   // 安全: S4。識別子を照合した固定のハンドルで、作ったフォルダ（空）だけを消す
        }
        catch (Exception) { return false; }   // R-146-1: finally から呼ばれる。ここの例外で Run の結果や続きの処理の例外を隠さない
        finally { _handle.Dispose(); }
    }

    private static void Remove(string path, bool recursive)   // 安全: S4。呼び出しは Delete だけ（識別子の照合の後）
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive);   // 安全: S4。Delete が渡した、固定したフォルダの中の項目（フォルダのリンクは非再帰）
        else File.Delete(path);   // 安全: S4。同上（ファイル、またはファイルのリンクそのもの）
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
    private const int FileDispositionInfoClass = 4;

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
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int infoClass, ref FileDispositionInfo info, uint size);   // 安全: S4。宣言（呼び出しは Delete だけ）
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
