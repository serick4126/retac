using System.Drawing;
using System.Runtime.InteropServices;

namespace ReTAC.Shell;

/// <summary>R-117: サムネイル。OS のキャッシュに任せる（アプリでは作らない・保存しない）。</summary>
public static class ShellThumbnails
{
    /// <param name="cacheOnly">
    /// true なら OS のキャッシュにあるものだけ（SIIGBF_INCACHEONLY）。クラウドの項目は常に true で呼ぶ（INV-THUMBNAIL-NO-CLOUD-DOWNLOAD）。
    /// </param>
    /// <returns>サムネイルが無い（アイコンしか無い）・取れなければ null。呼び出し側が Dispose する</returns>
    public static Bitmap? Get(string fullPath, int size, bool cacheOnly)
    {
        var iid = typeof(ShellImageInterop.IShellItemImageFactory).GUID;
        ShellImageInterop.IShellItemImageFactory factory;
        try { ShellImageInterop.SHCreateItemFromParsingName(fullPath, IntPtr.Zero, ref iid, out factory); }
        catch (COMException) { return null; }
        catch (FileNotFoundException) { return null; }
        try
        {
            var flags = ShellImageInterop.SIIGBF.ThumbnailOnly | ShellImageInterop.SIIGBF.BiggerSizeOk
                        | (cacheOnly ? ShellImageInterop.SIIGBF.InCacheOnly : 0);
            return factory.GetImage(new ShellImageInterop.SIZE { cx = size, cy = size }, flags, out var hbitmap) == 0 && hbitmap != IntPtr.Zero
                ? ShellImageInterop.ToArgb(hbitmap)
                : null;
        }
        finally { Marshal.ReleaseComObject(factory); }
    }
}

/// <summary>R-117 / R-118: 1 件の要求。Generation はフォルダを移るたびに進める（古い結果を捨てるため）。</summary>
public sealed record ImageRequest(int Generation, string FullPath, int Size, bool Thumbnail, bool Overlay, bool Cloud, bool IsFolder, DateTime Modified);

/// <summary>Thumbnail は受け取った側が持つ（捨てるなら Dispose）。OverlayIndex は印の番号（0 は無し。Request.Overlay が false なら意味を持たない）。</summary>
public sealed record ImageResult(ImageRequest Request, Bitmap? Thumbnail, int OverlayIndex);

/// <summary>
/// R-117 / R-118: サムネイルと OS の印を背景の 1 本の STA のスレッドで取る（ウィンドウごとに 1 つ）。
/// 2 段で取る: 1 段目は待ちの列の全部について印とキャッシュだけのサムネイル。キャッシュに無い項目は 2 段目の列へ回し、
/// 1 段目が空になってから作る要求を出す（先頭の作成が遅くても、後ろのキャッシュ済みが先に出る）。
/// クラウドの項目は 2 段目に進まない（INV-THUMBNAIL-NO-CLOUD-DOWNLOAD）。
/// Replace は両方の列を丸ごと差し替え、版を進める（見えている範囲に近い順に並べ直すため。空なら待ちを消すだけ）。
/// 差し替えの時点で取得中だった要求は、終わっても 2 段目へ回さず、結果も知らせずに解放する（古い要求を復活させない）。
/// Dispose は止める要求だけを出し、取得中のシェルの呼び出しの終わりを待たない（応答の遅いパスで終了が止まるため）。
/// 待ちの合図（AutoResetEvent）は、スレッドが抜けるときにスレッドの側で破棄する。
/// </summary>
public sealed class ShellImageWorker : IDisposable
{
    private readonly object _lock = new();
    // 要素は（差し替えの版, 要求）。Replace のたびに版を進め、古い版の要求は 2 段目へ回さず、結果も知らせない。
    // 画面側の Generation（フォルダの移動）とは別に要る: スクロールのような同じ Generation の中の差し替えも区別するため
    private readonly Queue<(int Version, ImageRequest Request)> _cached = new();
    private readonly Queue<(int Version, ImageRequest Request)> _generate = new();
    private int _version;
    private readonly AutoResetEvent _signal = new(false);
    private volatile bool _stopped;

    public event Action<ImageResult>? Completed;

    internal Func<string, int, bool, Bitmap?>? GetOverride { get; set; }
    internal Func<string, int>? OverlayOverride { get; set; }

    // テスト用の観測点。差し替えを送った側（FileListView）が空の一覧を送ったかを、待ちの列の中身に頼らず確かめるため
    private int _replaceCount, _lastReplaceSize = -1;
    internal int ReplaceCount => Volatile.Read(ref _replaceCount);
    internal int LastReplaceSize => Volatile.Read(ref _lastReplaceSize);
    /// <summary>テスト用: 1 段目でキャッシュを確かめ、版を判定し終えた（パス, 2 段目へ回したか）。スレッドの側から呼ぶ。</summary>
    internal Action<string, bool>? CacheChecked { get; set; }

    public ShellImageWorker()
    {
        var thread = new Thread(Work) { IsBackground = true, Name = "ShellImageWorker" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public void Replace(IReadOnlyList<ImageRequest> requests)
    {
        Interlocked.Increment(ref _replaceCount);
        Volatile.Write(ref _lastReplaceSize, requests.Count);
        lock (_lock)
        {
            _version++;
            _cached.Clear();
            _generate.Clear();
            foreach (var request in requests) _cached.Enqueue((_version, request));
        }
        // 止めた後はスレッドの側で破棄済みかもしれない。止める直前と競り合う隙は例外を飲み込んで塞ぐ
        if (!_stopped) { try { _signal.Set(); } catch (ObjectDisposedException) { } }
    }

    private void Work()
    {
        try
        {
            while (!_stopped)
            {
                (int Version, ImageRequest Request)? item;
                bool generate;
                lock (_lock)
                {
                    generate = _cached.Count == 0 && _generate.Count > 0;
                    item = _cached.Count > 0 ? _cached.Dequeue() : _generate.Count > 0 ? _generate.Dequeue() : null;
                }
                if (item is not { } taken) { _signal.WaitOne(); continue; }
                var (version, next) = taken;
                var get = GetOverride ?? ShellThumbnails.Get;
                var overlayOf = OverlayOverride ?? ShellOverlays.IndexOf;
                ImageResult result;
                try
                {
                    if (generate) result = new ImageResult(next with { Overlay = false }, get(next.FullPath, next.Size, false), 0);
                    else
                    {
                        var (thumbnail, overlay, needsGenerate) = QueryCached(next, get, overlayOf);
                        // 取得中に差し替えられていたら（設定をオフにした・フォルダを移った・スクロールした）、2 段目へ回さない
                        var queued = false;
                        if (needsGenerate) lock (_lock) { if (version == _version) { _generate.Enqueue((version, next)); queued = true; } }
                        if (next.Thumbnail) CacheChecked?.Invoke(next.FullPath, queued);
                        // キャッシュに無く、印も要らないなら、1 段目では知らせることが無い
                        if (thumbnail is null && !next.Overlay) continue;
                        result = new ImageResult(next, thumbnail, overlay);
                    }
                }
                catch (Exception) { continue; }   // 1 件の失敗で止めない。その項目はアイコンのまま
                if (_stopped) { result.Thumbnail?.Dispose(); return; }
                bool current;
                lock (_lock) current = version == _version;
                if (!current) { result.Thumbnail?.Dispose(); continue; }   // 差し替えた後に届いた古い結果は知らせない
                try { Completed?.Invoke(result); }
                catch (Exception) { result.Thumbnail?.Dispose(); }   // 受け取る側（破棄済みのコントロールなど）の失敗でスレッドを落とさない
            }
        }
        finally { _signal.Dispose(); }
    }

    /// <summary>
    /// 1 段目: 印（Overlay のとき）と、キャッシュだけのサムネイル（Thumbnail のとき）。
    /// キャッシュに無く、クラウドでなければ 2 段目へ回す（needsGenerate）。クラウドなら回さない（INV-THUMBNAIL-NO-CLOUD-DOWNLOAD）。
    /// </summary>
    internal static (Bitmap? Thumbnail, int Overlay, bool NeedsGenerate) QueryCached(ImageRequest request,
        Func<string, int, bool, Bitmap?> get, Func<string, int> overlayOf)
    {
        var overlay = request.Overlay ? overlayOf(request.FullPath) : 0;
        if (!request.Thumbnail) return (null, overlay, false);
        var thumbnail = get(request.FullPath, request.Size, true);
        return (thumbnail, overlay, thumbnail is null && !request.Cloud);
    }

    public void Dispose()
    {
        if (_stopped) return;
        _stopped = true;
        lock (_lock) { _cached.Clear(); _generate.Clear(); }
        try { _signal.Set(); } catch (ObjectDisposedException) { }   // 待っているスレッドを起こして抜けさせる（破棄はスレッドの側）
    }
}
