using System.Collections.Concurrent;
using System.Drawing;
using ReTAC.App;
using ReTAC.Domain.Entries;
using ReTAC.Domain.Listing;
using ReTAC.Shell;

namespace ReTAC.Domain.Tests;

/// <summary>R-117 / R-118: 要求の作り方・背景の結果の反映。古い世代は捨てる。届いた項目だけを描き直す。設定をオフにしたら待ちも消す。</summary>
public class FileListViewImagesTests
{
    private static List<Entry> Files(int count, string prefix = "file", DateTime? modified = null) =>
        Enumerable.Range(0, count).Select(i => TestEntries.File($"{prefix}{i:D3}.jpg") with { LastWriteTime = modified ?? DateTime.MinValue }).ToList();

    private static FileListView View(FileViewMode mode, FileViewSettings? views = null, int count = 200)
    {
        var list = new FileListView { Size = new Size(600, 400) };
        list.SetView(mode, views ?? new FileViewSettings(), new Dictionary<string, int?>(), SortOrder.Default);
        list.SetEntries(Files(count));
        return list;
    }

    [Fact]
    public void 見えている項目から順に要求する()
    {
        using var list = View(FileViewMode.LargeIcons);
        var requests = list.BuildImageRequests();
        Assert.Equal("file000.jpg", Path.GetFileName(requests[0].FullPath));
        var visible = list.Layout.IndexesIn(0, 0, 600, 400, 200).Count;
        Assert.True(requests.Count > visible);                    // 前後 1 画面分も
        Assert.True(requests.Count <= visible * 3 + 2 * ((GridLayout)list.Layout).Columns);
    }

    [Fact]
    public void サムネイルをオフにした系統ではサムネイルを要求しない()
    {
        using var list = View(FileViewMode.LargeIcons, new FileViewSettings { Icons = new() { Thumbnails = false } });
        Assert.All(list.BuildImageRequests(), r => Assert.False(r.Thumbnail));
    }

    [Fact]
    public void 印をオフにした一覧では何も要求しない()
    {
        using var list = View(FileViewMode.List, new FileViewSettings { Common = new() { ShowOverlays = false } });
        Assert.Empty(list.BuildImageRequests());
    }

    [Fact]
    public void 一覧と詳細でも印は要求する()
    {
        using var list = View(FileViewMode.Details);
        Assert.All(list.BuildImageRequests(), r => { Assert.True(r.Overlay); Assert.False(r.Thumbnail); });
    }

    [Fact]
    public void クラウドの項目は印を付けて要求する()
    {
        using var list = new FileListView { Size = new Size(600, 400) };
        list.SetView(FileViewMode.LargeIcons, new FileViewSettings(), new Dictionary<string, int?>(), SortOrder.Default);
        list.SetEntries([TestEntries.File("a.jpg") with { Attributes = (FileAttributes)0x00400020 }]);
        Assert.True(list.BuildImageRequests()[0].Cloud);
    }

    /// <summary>取得がすぐ終わるワーカー（シェルに問い合わせない）。FileListView が何を送ったかを ReplaceCount・LastReplaceSize で見る。</summary>
    private static ShellImageWorker QuietWorker() => new() { OverlayOverride = _ => 0, GetOverride = (_, _, _) => null };

    /// <summary>
    /// 待ちの列が本当に消えることは Task 5 のワーカーのテストが確かめる。ここでは、FileListView が空の一覧で差し替えを送ったことを確かめる
    /// （ワーカーへ別の差し替えを送らない。送ると、FileListView が送り忘れても古い待ちが消えてしまい、見逃す）。
    /// </summary>
    [Fact]
    public void 設定をオフにしたら空の一覧で差し替える()
    {
        using var list = View(FileViewMode.Details);   // 印だけを要求する
        using var worker = QuietWorker();
        list.ImageWorker = worker;
        list.UpdateImageQueue();
        Assert.True(worker.LastReplaceSize > 0);
        var before = worker.ReplaceCount;
        list.SetView(FileViewMode.Details, new FileViewSettings { Common = new() { ShowOverlays = false } },
            new Dictionary<string, int?>(), SortOrder.Default);
        Assert.True(worker.ReplaceCount > before);
        Assert.Equal(0, worker.LastReplaceSize);
    }

    [Fact]
    public void 空の一覧へ移ったら空の一覧で差し替える()
    {
        using var list = View(FileViewMode.Details);
        using var worker = QuietWorker();
        list.ImageWorker = worker;
        list.UpdateImageQueue();
        var before = worker.ReplaceCount;
        list.SetEntries([]);
        Assert.True(worker.ReplaceCount > before);
        Assert.Equal(0, worker.LastReplaceSize);
    }

    [Fact]
    public void サムネイルをオフにしても印を出すなら印だけで差し替える()
    {
        using var list = View(FileViewMode.LargeIcons);
        using var worker = QuietWorker();
        list.ImageWorker = worker;
        list.UpdateImageQueue();
        list.SetView(FileViewMode.LargeIcons, new FileViewSettings { Icons = new() { Thumbnails = false } },
            new Dictionary<string, int?>(), SortOrder.Default);
        Assert.True(worker.LastReplaceSize > 0);
        Assert.All(list.BuildImageRequests(), r => Assert.False(r.Thumbnail));
    }

    [Fact]
    public void ハンドルが無ければワーカーを作らない()
    {
        using var list = View(FileViewMode.LargeIcons);
        list.UpdateImageQueue();
        Assert.Null(list.ImageWorker);
        Assert.False(list.IsHandleCreated);
    }

    [Fact]
    public void 要求が無ければワーカーを作らない()
    {
        using var list = View(FileViewMode.List, new FileViewSettings { Common = new() { ShowOverlays = false } });
        list.UpdateImageQueue();
        Assert.Null(list.ImageWorker);
    }

    [Fact]
    public void フォルダを移った後に届いた古い結果は描かずに解放する()
    {
        using var list = View(FileViewMode.LargeIcons);
        var old = list.BuildImageRequests()[0];
        list.SetEntries(Files(5, "other"));
        Assert.NotEqual(old.Generation, list.ImageGeneration);
        var bitmap = new Bitmap(4, 4);
        list.DeliverImage(new ImageResult(old, bitmap, 0));
        Assert.Throws<ArgumentException>(() => _ = bitmap.Width);   // 解放された
        Assert.Equal(0, list.InvalidatedItems);
    }

    [Fact]
    public void 同じ一覧を入れ直しても世代が進み_前の結果は捨てる()
    {
        using var list = View(FileViewMode.LargeIcons, count: 5);
        var old = list.BuildImageRequests()[0];
        list.SetEntries(Files(5));   // 自動更新で同じ内容を読み直した
        Assert.NotEqual(old.Generation, list.ImageGeneration);
        var bitmap = new Bitmap(4, 4);
        list.DeliverImage(new ImageResult(old, bitmap, 0));
        Assert.Throws<ArgumentException>(() => _ = bitmap.Width);
    }

    [Fact]
    public void 同じパス_大きさ_更新日時のサムネイルは使い回し_更新日時が変わったら取り直す()
    {
        var at = new DateTime(2026, 9, 1);
        using var list = new FileListView { Size = new Size(600, 400) };
        list.SetView(FileViewMode.LargeIcons, new FileViewSettings(), new Dictionary<string, int?>(), SortOrder.Default);
        list.SetEntries(Files(1, modified: at));
        list.DeliverImage(new ImageResult(list.BuildImageRequests()[0], new Bitmap(4, 4), 0));
        list.SetEntries(Files(1, modified: at));                         // 読み直し（世代は進む）
        Assert.All(list.BuildImageRequests(), r => Assert.False(r.Thumbnail));   // キャッシュ済みは要求しない
        Assert.NotNull(list.ThumbnailFor(list.State.Entries[0]));
        list.SetEntries(Files(1, modified: at.AddMinutes(1)));           // 中身が変わった
        Assert.True(list.BuildImageRequests()[0].Thumbnail);
        Assert.Null(list.ThumbnailFor(list.State.Entries[0]));
    }

    [Fact]
    public void フォルダのサムネイルをオフにしたらキャッシュ済みでもフォルダのアイコンに戻る()
    {
        using var list = new FileListView { Size = new Size(600, 400) };
        list.SetView(FileViewMode.LargeIcons, new FileViewSettings(), new Dictionary<string, int?>(), SortOrder.Default);
        list.SetEntries([TestEntries.Folder("photos")]);
        var request = list.BuildImageRequests()[0];
        Assert.True(request.Thumbnail);
        list.DeliverImage(new ImageResult(request, new Bitmap(4, 4), 0));
        Assert.NotNull(list.ThumbnailFor(list.State.Entries[0]));
        list.SetView(FileViewMode.LargeIcons, new FileViewSettings { Icons = new() { FolderThumbnails = false } },
            new Dictionary<string, int?>(), SortOrder.Default);
        Assert.Null(list.ThumbnailFor(list.State.Entries[0]));
        Assert.All(list.BuildImageRequests(), r => Assert.False(r.Thumbnail));
    }

    [Fact]
    public void 届いた結果はその項目だけを描き直す()
    {
        using var list = View(FileViewMode.LargeIcons);
        var request = list.BuildImageRequests()[3];
        list.DeliverImage(new ImageResult(request, new Bitmap(4, 4), 2));
        Assert.Equal(1, list.InvalidatedItems);
    }

    [Fact]
    public void 届いた結果で全項目を測り直さない()
    {
        using var list = View(FileViewMode.SmallIcons);
        var scans = list.ContentScanCount;
        foreach (var request in list.BuildImageRequests().Take(20)) list.DeliverImage(new ImageResult(request, null, 1));
        Assert.Equal(scans, list.ContentScanCount);
    }

    [Fact]
    public void 閉じた後に届いた結果は解放して何もしない()
    {
        var list = View(FileViewMode.LargeIcons);
        var request = list.BuildImageRequests()[0];
        list.Dispose();
        var bitmap = new Bitmap(4, 4);
        list.DeliverImage(new ImageResult(request, bitmap, 0));
        Assert.Throws<ArgumentException>(() => _ = bitmap.Width);
    }
}
