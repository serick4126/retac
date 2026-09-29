using System.Collections.Concurrent;
using System.Drawing;
using ReTAC.Shell;

namespace ReTAC.Domain.Tests;

/// <summary>R-117 / R-118 / INV-THUMBNAIL-NO-CLOUD-DOWNLOAD: 画像の変換・背景の取得の順・差し替え・後始末。</summary>
public class ShellThumbnailsTests
{
    /// <summary>4×4 の 32bpp の DIB セクション。四隅は透明（α 0）、(1,1) は半透明（α 128。乗算済みの BGRA）、ほかは不透明の赤。</summary>
    private static IntPtr TestDib(bool bottomUp)
    {
        var bits = new byte[4 * 4 * 4];
        for (var y = 0; y < 4; y++)
            for (var x = 0; x < 4; x++)
            {
                var corner = (x is 0 or 3) && (y is 0 or 3);
                var half = x == 1 && y == 1;
                var (b, g, r, a) = corner ? (0, 0, 0, 0) : half ? (0, 0, 128, 128) : (0, 0, 255, 255);
                var row = bottomUp ? 3 - y : y;   // bottom-up は下の行から並ぶ
                var i = (row * 4 + x) * 4;
                (bits[i], bits[i + 1], bits[i + 2], bits[i + 3]) = ((byte)b, (byte)g, (byte)r, (byte)a);
            }
        return ShellImageInterop.CreateTestDib(4, 4, bottomUp, bits);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 透明度を保って変換する(bool bottomUp)
    {
        using var bitmap = ShellImageInterop.ToArgb(TestDib(bottomUp));
        Assert.Equal(0, bitmap.GetPixel(0, 0).A);
        Assert.Equal(0, bitmap.GetPixel(3, 3).A);
        Assert.Equal(255, bitmap.GetPixel(2, 2).A);
        Assert.Equal(255, bitmap.GetPixel(2, 2).R);
        var half = bitmap.GetPixel(1, 1);
        Assert.InRange(half.A, 127, 129);
        Assert.InRange(half.R, 250, 255);   // 乗算済みを戻した値（128 / 128 × 255）
    }

    private static ImageRequest Req(string path, bool cloud = false, bool thumbnail = true, bool overlay = true) =>
        new(1, path, 96, thumbnail, overlay, cloud, IsFolder: false, DateTime.MinValue);

    [Fact]
    public void 差し替えた列の順に取り_捨てた要求は取らない()
    {
        using var worker = new ShellImageWorker();
        using var started = new ManualResetEventSlim(false);
        using var gate = new ManualResetEventSlim(false);
        var order = new ConcurrentQueue<string>();
        worker.OverlayOverride = path =>
        {
            started.Set();                                   // 最初の 1 件を取り始めた
            gate.Wait(TimeSpan.FromSeconds(5));
            order.Enqueue(path);
            return 0;
        };
        using var done = new CountdownEvent(2);
        worker.Completed += r => { if (r.Request.FullPath != "a") done.Signal(); };   // a は差し替えで捨てるので知らせない
        worker.Replace([Req("a", thumbnail: false), Req("b", thumbnail: false), Req("c", thumbnail: false)]);
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));   // a を取り始めたのを合図で知る（時間で待たない）
        worker.Replace([Req("x", thumbnail: false), Req("y", thumbnail: false)]);   // b・c は捨てる
        gate.Set();
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(["a", "x", "y"], order.ToArray());      // a は取得中だったので問い合わせは終わるが、b・c は取らない
    }

    /// <summary>目印の要求（印だけ）を後から入れ、その処理が始まったことを合図で待つ。これより前に呼ばれなかったものは、もう呼ばれない。</summary>
    private static void AwaitMarker(ShellImageWorker worker, ManualResetEventSlim markerSeen)
    {
        worker.Replace([Req("marker", thumbnail: false)]);
        Assert.True(markerSeen.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void 空で差し替えると取得中の1件のほかは取らない()
    {
        using var worker = new ShellImageWorker();
        using var started = new ManualResetEventSlim(false);
        using var gate = new ManualResetEventSlim(false);
        using var markerSeen = new ManualResetEventSlim(false);
        var calls = new ConcurrentQueue<string>();
        worker.OverlayOverride = path =>
        {
            calls.Enqueue(path);
            if (path == "marker") { markerSeen.Set(); return 0; }
            started.Set();
            gate.Wait(TimeSpan.FromSeconds(5));
            return 0;
        };
        worker.Replace([Req("a", thumbnail: false), Req("b", thumbnail: false), Req("c", thumbnail: false)]);
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        worker.Replace([]);                                  // 設定をオフにした・空のフォルダへ移った
        gate.Set();
        AwaitMarker(worker, markerSeen);                     // 時間で待たず、処理の順で判定する
        Assert.Equal(["a", "marker"], calls.ToArray());
    }

    /// <summary>
    /// キャッシュの確認（1 段目）で止まるワーカー。作る要求（2 段目）の呼び出しを記録する。
    /// checkedA は、a のキャッシュの確認と版の判定が終わった合図（2 段目へ回したかを添えて）。
    /// </summary>
    private static ShellImageWorker CacheBlockedWorker(ManualResetEventSlim started, ManualResetEventSlim gate,
        ConcurrentQueue<string> generated, Action<bool> checkedA, ManualResetEventSlim? generatedSeen = null)
    {
        var worker = new ShellImageWorker();
        worker.OverlayOverride = _ => 0;
        worker.GetOverride = (path, _, cacheOnly) =>
        {
            if (!cacheOnly) { generated.Enqueue(path); generatedSeen?.Set(); return null; }
            if (path == "a") { started.Set(); gate.Wait(TimeSpan.FromSeconds(5)); }
            return null;                                     // どれもキャッシュに無い
        };
        worker.CacheChecked = (path, queued) => { if (path == "a") checkedA(queued); };
        return worker;
    }

    [Fact]
    public void キャッシュの確認中に空へ差し替えたら_その要求は作る段へ進まない()
    {
        using var started = new ManualResetEventSlim(false);
        using var gate = new ManualResetEventSlim(false);
        using var judged = new ManualResetEventSlim(false);
        bool? queued = null;
        var generated = new ConcurrentQueue<string>();
        using var worker = CacheBlockedWorker(started, gate, generated, q => { queued = q; judged.Set(); });
        worker.Replace([Req("a", overlay: false)]);
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));   // a のキャッシュの確認の途中
        worker.Replace([]);
        gate.Set();                                          // a はキャッシュに無かった（needsGenerate）が、版が古い
        // 版の判定が終わった合図を待つ。確かめ終えるまで、ほかの Replace を呼ばない（呼ぶと 2 段目の列が消え、復活を見逃す）
        Assert.True(judged.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(queued);                                // 2 段目へ回していない
        Assert.Empty(generated);                             // cacheOnly: false の取得は一度も呼ばれない
    }

    [Fact]
    public void キャッシュの確認中に新しい一覧へ差し替えたら_新しい要求だけを作る()
    {
        using var started = new ManualResetEventSlim(false);
        using var gate = new ManualResetEventSlim(false);
        using var generatedSeen = new ManualResetEventSlim(false);
        var generated = new ConcurrentQueue<string>();
        using var worker = CacheBlockedWorker(started, gate, generated, _ => { }, generatedSeen);
        worker.Replace([Req("a", overlay: false)]);
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        worker.Replace([Req("b", overlay: false)]);
        gate.Set();
        Assert.True(generatedSeen.Wait(TimeSpan.FromSeconds(5)));   // b の 2 段目が始まった（a の 1 段目はその前に終わっている）
        Assert.Equal(["b"], generated.ToArray());
    }

    [Fact]
    public void キャッシュの確認を全部終えてから作る()
    {
        using var worker = new ShellImageWorker();
        var calls = new ConcurrentQueue<(string Path, bool CacheOnly)>();
        worker.OverlayOverride = _ => 0;
        worker.GetOverride = (path, _, cacheOnly) => { calls.Enqueue((path, cacheOnly)); return null; };
        using var done = new CountdownEvent(3 + 3);   // 1 段目 3 件（印の知らせ）+ 2 段目 3 件
        worker.Completed += _ => done.Signal();
        worker.Replace([Req("a"), Req("b"), Req("c")]);
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal([("a", true), ("b", true), ("c", true), ("a", false), ("b", false), ("c", false)], calls.ToArray());
    }

    [Fact]
    public void クラウドの項目はキャッシュだけを見て作る要求に進まない()
    {
        var calls = new List<bool>();
        var (_, _, needs) = ShellImageWorker.QueryCached(Req("a", cloud: true), (_, _, cacheOnly) => { calls.Add(cacheOnly); return null; }, _ => 0);
        Assert.Equal([true], calls);
        Assert.False(needs);
        (_, _, needs) = ShellImageWorker.QueryCached(Req("b"), (_, _, _) => null, _ => 0);
        Assert.True(needs);
    }

    [Fact]
    public void サムネイルを出さない要求はサムネイルに触れず_印を出さない要求は印に触れない()
    {
        var gets = 0;
        var overlays = 0;
        ShellImageWorker.QueryCached(Req("a", thumbnail: false), (_, _, _) => { gets++; return null; }, _ => { overlays++; return 3; });
        ShellImageWorker.QueryCached(Req("b", overlay: false), (_, _, _) => { gets++; return null; }, _ => { overlays++; return 3; });
        Assert.Equal((1, 1), (gets, overlays));
    }

    [Fact]
    public void 止めた後に届いた画像は解放する()
    {
        var worker = new ShellImageWorker();   // Dispose はテストの中で呼ぶ
        var bitmap = new Bitmap(4, 4);
        using var started = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        using var returned = new ManualResetEventSlim(false);
        worker.OverlayOverride = _ => 0;
        worker.GetOverride = (_, _, _) => { started.Set(); release.Wait(TimeSpan.FromSeconds(5)); returned.Set(); return bitmap; };
        var delivered = false;
        worker.Completed += _ => delivered = true;
        worker.Replace([Req("a")]);
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        worker.Dispose();                                    // 取得中でも待たずに戻る
        release.Set();
        Assert.True(returned.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(SpinWait.SpinUntil(() => { try { _ = bitmap.Width; return false; } catch (ArgumentException) { return true; } },
            TimeSpan.FromSeconds(5)));                       // Dispose 済みになる
        Assert.False(delivered);
    }

    [Fact]
    public void 受け取る側が例外を出してもスレッドは続く()
    {
        using var worker = new ShellImageWorker();
        worker.OverlayOverride = _ => 0;
        var count = 0;
        using var done = new ManualResetEventSlim(false);
        worker.Completed += _ => { if (Interlocked.Increment(ref count) == 1) throw new InvalidOperationException(); done.Set(); };
        worker.Replace([Req("a", thumbnail: false), Req("b", thumbnail: false)]);
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void システムのイメージリストの宣言が合っている()
    {
        var size = ShellImageInterop.WithImageList(ShellImageInterop.SHIL_JUMBO,
            list => list.GetIconSize(out var cx, out var cy) == 0 ? (cx, cy) : (0, 0), (0, 0));
        Assert.Equal((256, 256), size);
    }

    [Fact]
    public void 要求より大きい画像は縦横比を保って要求の大きさへ縮め_透明を保つ()
    {
        using var big = new Bitmap(1024, 512, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        big.SetPixel(0, 0, System.Drawing.Color.FromArgb(0, 0, 0, 0));
        big.SetPixel(500, 250, System.Drawing.Color.FromArgb(255, 200, 10, 10));
        using var fitted = ShellImageWorker.FitTo(big, 256);
        Assert.Equal((256, 128), (fitted.Width, fitted.Height));
        Assert.Equal(System.Drawing.Imaging.PixelFormat.Format32bppArgb, fitted.PixelFormat);
        Assert.Equal(0, fitted.GetPixel(0, 0).A);
    }

    [Fact]
    public void 要求以下の画像はそのまま返す()
    {
        using var small = new Bitmap(100, 50);
        Assert.Same(small, ShellImageWorker.FitTo(small, 256));
    }

    [Fact]
    public void 縮めたら元の画像を解放して届ける()
    {
        var big = new Bitmap(512, 512);
        using var fitted = ShellImageWorker.FitTo(big, 128);
        Assert.Throws<ArgumentException>(() => _ = big.Width);
        Assert.Equal(128, fitted.Width);
    }
}
