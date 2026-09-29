using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;

namespace ReTAC.Shell;

/// <summary>ステータスバー ④ 区画の「種別」（R-34）。シェルの種類名を拡張子単位でキャッシュする。</summary>
public static class ShellFileType
{
    // 列挙は Task.Run 上でも走る（MainForm の非同期の folder 展開）。素の Dictionary だと壊れる
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string KeyOf(string fullPath, bool isFolder) => isFolder ? "__folder__" : Path.GetExtension(fullPath);

    public static string TypeName(string fullPath, bool isFolder)
    {
        var key = KeyOf(fullPath, isFolder);
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var name = Query(key);
        Cache[key] = name;
        return name;
    }

    public static bool TryGetCached(string fullPath, bool isFolder, out string name) =>
        Cache.TryGetValue(KeyOf(fullPath, isFolder), out name!);

    /// <summary>
    /// R-114: 詳細表示の種類の列は、描画を止めないよう背景で取り、届くまでは空欄で描く。
    /// 同じ鍵の問い合わせが進行中なら積まない（同じ拡張子 1000 件でも問い合わせは 1 回）。問い合わせは背景の 1 本のスレッドで順に行う。
    /// </summary>
    public static void Request(string fullPath, bool isFolder)
    {
        var key = KeyOf(fullPath, isFolder);
        if (Cache.ContainsKey(key) || !Pending.TryAdd(key, 0)) return;
        // 作業スレッドの「Cache に入れてから Pending を外す」と噛み合うと重複して積むので、積む前に見直す
        if (Cache.ContainsKey(key)) { Pending.TryRemove(key, out _); return; }
        Queue.Add(key);
        EnsureWorker();
    }

    /// <summary>届いた鍵。背景のスレッドから呼ばれる。受け取る側は UI のスレッドへ渡し、破棄済みなら何もしない。</summary>
    public static event Action<string>? Resolved;

    private static readonly ConcurrentDictionary<string, byte> Pending = new(StringComparer.OrdinalIgnoreCase);
    private static BlockingCollection<string> Queue = new();
    private static Thread? _worker;
    private static readonly object WorkerLock = new();

    private static void EnsureWorker()
    {
        lock (WorkerLock)
        {
            if (_worker is not null) return;
            // 終了を待たない（IsBackground）。SHGetFileInfo は COM を使うので STA で回す
            _worker = new Thread(Work) { IsBackground = true, Name = "ShellFileType" };
            _worker.SetApartmentState(ApartmentState.STA);
            _worker.Start(Queue);
        }
    }

    private static void Work(object? state)
    {
        try
        {
            foreach (var key in ((BlockingCollection<string>)state!).GetConsumingEnumerable())
            {
                string name;
                try { name = Query(key); }
                catch (Exception) { name = ""; }   // 1 つの失敗で列を止めない。空欄のまま（次に開いたときも問い合わせない）
                Cache[key] = name;
                Pending.TryRemove(key, out _);
                Interlocked.Increment(ref _queryCount);
                // 受け取る側（破棄済みのコントロールへの BeginInvoke など）の失敗で、背景のスレッドごと落とさない
                try { Resolved?.Invoke(key); }
                catch (Exception) { }
            }
        }
        finally
        {
            // どんな理由で抜けても、次の Request が新しいスレッドを起こせるようにする
            lock (WorkerLock) { if (ReferenceEquals(_worker, Thread.CurrentThread)) _worker = null; }
        }
    }

    private static string Query(string key)
    {
        if (QueryOverride is { } fake) return fake(key);
        // SHGFI_USEFILEATTRIBUTES: 実ファイルに触れないので、応答しないドライブでも固まらない（N-05）
        var isFolder = key == "__folder__";
        var attributes = isFolder ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
        return SHGetFileInfo(isFolder ? @"C:\x" : "x" + key, attributes,
            out var info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_TYPENAME | SHGFI_USEFILEATTRIBUTES) == IntPtr.Zero
            ? ""
            : info.szTypeName;
    }

    internal static Func<string, string>? QueryOverride;
    private static int _queryCount;
    internal static int QueryCount => _queryCount;

    internal static void ResetForTests()
    {
        Thread? worker;
        lock (WorkerLock) { Queue.CompleteAdding(); worker = _worker; }
        worker?.Join(TimeSpan.FromSeconds(5));   // 作業スレッドは終わり際に WorkerLock を取るので、ロックの外で待つ
        lock (WorkerLock) { _worker = null; Queue = new(); }
        Cache.Clear();
        Pending.Clear();
        Resolved = null;
        QueryOverride = null;
        _queryCount = 0;
    }

    /// <summary>
    /// 16.4 節「関連付けファイル」の判定。HKCR に拡張子の既定値があれば関連付けありとみなす。
    /// 種類名から推測するより確実で、レジストリを読むだけなので応答しないドライブの影響も受けない。
    /// </summary>
    public static bool HasAssociation(string extension)
    {
        if (string.IsNullOrEmpty(extension)) return false;
        if (AssociationCache.TryGetValue(extension, out var cached)) return cached;

        using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(extension);
        var associated = key?.GetValue(null) is string progId && progId.Length > 0;
        AssociationCache[extension] = associated;
        return associated;
    }

    private static readonly ConcurrentDictionary<string, bool> AssociationCache = new(StringComparer.OrdinalIgnoreCase);

    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const uint SHGFI_TYPENAME = 0x000000400;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        out SHFILEINFO psfi, uint cbFileInfo, uint uFlags);
}
