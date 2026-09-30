using System.Drawing;
using ReTAC.App;
using ReTAC.Domain.Listing;
using SortOrder = ReTAC.Domain.Listing.SortOrder;

namespace ReTAC.Domain.Tests;

/// <summary>R-116 / R-119 / R-120: アイコン表示のレイアウト・チェックボックス・名前。</summary>
public class FileListViewIconsTests
{
    private const string LongName = "とても長い名前がここに続いてまだ終わらない名前.txt";

    private static FileListView View(FileViewMode mode, FileViewSettings? views = null, int count = 30, int width = 600, int height = 400)
    {
        var list = new FileListView { Size = new Size(width, height) };
        list.SetView(mode, views ?? new FileViewSettings(), new Dictionary<string, int?>(), SortOrder.Default);
        list.SetEntries(Enumerable.Range(0, count).Select(i => TestEntries.File($"file{i:D2}.txt")).ToList());
        return list;
    }

    [Theory]
    [InlineData(FileViewMode.SmallIcons, GridArrangement.IconLeft)]
    [InlineData(FileViewMode.MediumIcons, GridArrangement.IconTop)]
    [InlineData(FileViewMode.LargeIcons, GridArrangement.IconTop)]
    [InlineData(FileViewMode.ExtraLargeIcons, GridArrangement.IconTop)]
    public void アイコンのモードは格子のレイアウト(FileViewMode mode, GridArrangement arrangement)
    {
        using var list = View(mode);
        var grid = Assert.IsType<GridLayout>(list.Layout);
        Assert.Equal(arrangement, grid.Arrangement);
    }

    [Fact]
    public void アイコンの大きさは設定のとおり()
    {
        var views = new FileViewSettings { Icons = new() { MediumSize = 64, LargeSize = 128, ExtraLargeSize = 192 } };
        using var list = View(FileViewMode.LargeIcons, views);
        Assert.Equal(128, ((GridLayout)list.Layout).IconSize);   // テストの DeviceDpi は 96
    }

    [Fact]
    public void ホバー中とマーク済みの項目だけチェックボックスを出す()
    {
        using var list = View(FileViewMode.MediumIcons);
        list.State.ToggleMark(3);
        list.HotIndex = 5;
        Assert.True(list.ShowsCheckBox(3));
        Assert.True(list.ShowsCheckBox(5));
        Assert.False(list.ShowsCheckBox(4));
    }

    [Fact]
    public void 常に表示なら全項目に出す()
    {
        var views = new FileViewSettings { Icons = new() { CheckBoxes = CheckBoxMode.Always } };
        using var list = View(FileViewMode.MediumIcons, views);
        Assert.True(list.ShowsCheckBox(4));
    }

    [Fact]
    public void 小アイコンにはチェックボックスが無い()
    {
        using var list = View(FileViewMode.SmallIcons);
        list.HotIndex = 1;
        Assert.False(list.ShowsCheckBox(1));
        Assert.Null(list.Layout.CheckBoxBounds(1));
    }

    [Fact]
    public void チェックボックスを押して離すとマークが変わり_ほかの所ではカーソルだけ()
    {
        using var list = View(FileViewMode.MediumIcons);
        var box = list.Layout.CheckBoxBounds(2)!.Value;
        list.PressLeft(new Point(box.X + 2, box.Y + 2), shift: false);
        list.ReleaseLeft();
        Assert.Contains(2, list.State.Marks);
        var icon = list.Layout.IconBounds(4);
        list.PressLeft(new Point(icon.X + icon.Width / 2, icon.Y + icon.Height / 2), shift: false);
        list.ReleaseLeft();
        Assert.Equal(4, list.State.CursorIndex);
        Assert.DoesNotContain(4, list.State.Marks);
    }

    [Fact]
    public void 名前は設定の行数まで折り返す()
    {
        var views = new FileViewSettings { Icons = new() { NameLines = 1 } };
        using var list = new FileListView { Size = new Size(600, 400) };
        list.SetView(FileViewMode.MediumIcons, views, new Dictionary<string, int?>(), SortOrder.Default);
        list.SetEntries([TestEntries.File(LongName), TestEntries.File("b.txt")]);
        list.MoveCursorTo(1);
        var r = list.NameLinesFor(0);
        Assert.Single(r.Lines);
        Assert.True(r.Truncated);
        Assert.EndsWith(".txt", r.Lines[0]);
    }

    [Fact]
    public void カーソルの項目は全部の行を描く()
    {
        var views = new FileViewSettings { Icons = new() { NameLines = 1 } };
        using var list = new FileListView { Size = new Size(600, 400) };
        list.SetView(FileViewMode.MediumIcons, views, new Dictionary<string, int?>(), SortOrder.Default);
        list.SetEntries([TestEntries.File(LongName)]);
        var r = list.NameLinesFor(0);
        Assert.False(r.Truncated);
        Assert.True(r.Lines.Count > 1);
        Assert.True(list.IsTruncated(0));   // 省略はされている（ツールチップ・ステータスバーの判断）
    }

    [Fact]
    public void 隠した拡張子は折り返しても出さない()
    {
        var views = new FileViewSettings { Common = new() { HideKnownExtensions = true }, Icons = new() { NameLines = 1 } };
        using var list = new FileListView { Size = new Size(600, 400) };
        list.SetView(FileViewMode.MediumIcons, views, new Dictionary<string, int?>(), SortOrder.Default);
        list.SetEntries([TestEntries.File(LongName), TestEntries.File("b.txt")]);
        list.MoveCursorTo(1);
        if (list.HidesExtension(list.State.Entries[0]))   // .txt が登録されている環境だけ
            Assert.DoesNotContain(".txt", string.Concat(list.NameLinesFor(0).Lines));
    }

    [Fact]
    public void 同じ項目の名前の行は測り直さず使い回し_カーソルが動くと測り直す()
    {
        // NameWrap.Lines は長い名前で measure を何百回も呼ぶので、描くたびに全項目を折り返さない
        var views = new FileViewSettings { Icons = new() { NameLines = 1 } };
        using var list = new FileListView { Size = new Size(600, 400) };
        list.SetView(FileViewMode.MediumIcons, views, new Dictionary<string, int?>(), SortOrder.Default);
        list.SetEntries([TestEntries.File(LongName), TestEntries.File("b.txt")]);
        list.MoveCursorTo(1);
        var first = list.NameLinesFor(0);
        Assert.Same(first, list.NameLinesFor(0));
        list.MoveCursorTo(0);   // カーソルの項目は全部の行なので別の答え
        Assert.NotSame(first, list.NameLinesFor(0));
    }

    [Fact]
    public void 小アイコンの項目の幅は最大文字数で決まる()
    {
        var views = new FileViewSettings { Icons = new() { SmallIconWidth = new() { Mode = NameWidthMode.MaxChars, MaxChars = 10 } } };
        using var list = new FileListView { Size = new Size(800, 400) };
        list.SetView(FileViewMode.SmallIcons, views, new Dictionary<string, int?>(), SortOrder.Default);
        list.SetEntries([TestEntries.File(new string('あ', 50) + ".txt")]);
        var zero = list.ZeroWidth;   // 数字 0 の幅（テスト用の internal）
        Assert.True(((GridLayout)list.Layout).TextWidth <= zero * 10);
    }

    [Fact]
    public void カーソルの枠は名前がはみ出したら帯まで囲み_落とす先の枠は項目の矩形のまま()
    {
        // INV-LAYOUT-GEOMETRY-SINGLE-SOURCE: 枠の下辺が名前の帯を横切る線に見えないよう、カーソルの枠だけ帯を囲む
        var views = new FileViewSettings { Icons = new() { NameLines = 1 } };
        using var list = new FileListView { Size = new Size(600, 400) };
        list.SetView(FileViewMode.MediumIcons, views, new Dictionary<string, int?>(), SortOrder.Default);
        list.SetEntries([TestEntries.File(LongName), TestEntries.File("b.txt")]);
        Assert.True(list.NameLinesFor(0).Lines.Count > 1);   // 帯ははみ出す
        var (x, y, w, h) = FileViewScroll.VisibleBounds(list.Layout, list.ScrollPosition, 0);
        var item = new Rectangle(x, y, w, h);
        var frame = list.CursorFrameBounds(0);
        Assert.Equal(item, list.DropFrameBounds(0));
        Assert.True(frame.Bottom > item.Bottom);
        Assert.Equal(item, Rectangle.Intersect(frame, item));   // 項目は枠の中
        list.MoveCursorTo(1);
        Assert.Equal(item, list.CursorFrameBounds(0));          // カーソルでなければはみ出さない
    }

    [Fact]
    public void 名前の帯は行が収まるなら項目のまま_収まらなければ下へ広げる()
    {
        var item = new Rectangle(0, 0, 100, 150);
        var name = new Rectangle(5, 100, 90, 32);
        Assert.Equal(item, FileListView.NameOverflowBand(item, name, 2, 16, 4));
        Assert.Equal(new Rectangle(0, 0, 100, 100 + 3 * 16 + 4), FileListView.NameOverflowBand(item, name, 3, 16, 4));
    }

    [Fact]
    public void 小アイコンはカーソルの項目だけ1行のまま全部の名前を描く()
    {
        var views = new FileViewSettings { Icons = new() { SmallIconWidth = new() { Mode = NameWidthMode.MaxChars, MaxChars = 10 } } };
        using var list = new FileListView { Size = new Size(800, 400) };
        list.SetView(FileViewMode.SmallIcons, views, new Dictionary<string, int?>(), SortOrder.Default);
        list.SetEntries([TestEntries.File(LongName), TestEntries.File("b.txt")]);
        list.MoveCursorTo(1);
        Assert.True(list.NameLinesFor(0).Truncated);                       // カーソルでなければ省略
        Assert.NotEqual(LongName, list.SmallIconNameText(0));
        list.MoveCursorTo(0);
        Assert.True(list.DrawsFullName(0));
        Assert.Equal(list.FullNameText(list.State.Entries[0]), list.SmallIconNameText(0));   // 1 行のまま全部
        Assert.True(list.IsTruncated(0));                                   // ツールチップ・ステータスバーは省略の扱いのまま
    }

    [Fact]
    public void 格子の項目は仕様の重ね順で描く()
    {
        Assert.Equal([GridLayer.Fill, GridLayer.Image, GridLayer.Overlay, GridLayer.CheckBox, GridLayer.CursorFrame, GridLayer.DropFrame],
            FileListView.GridLayers);
    }

    [Fact]
    public void 格子の左右はカーソルを隣の行へ進める()
    {
        using var list = View(FileViewMode.MediumIcons);
        var columns = ((GridLayout)list.Layout).Columns;
        list.MoveCursorTo(columns - 1);
        list.PressKey(System.Windows.Forms.Keys.Right);
        Assert.Equal(columns, list.State.CursorIndex);
    }
}
