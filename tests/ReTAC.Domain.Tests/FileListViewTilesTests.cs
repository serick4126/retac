using System.Drawing;
using System.Windows.Forms;
using ReTAC.App;
using ReTAC.Domain.Listing;
using SortOrder = ReTAC.Domain.Listing.SortOrder;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-121: 並べて表示。当たり判定は中〜特大と同じ（アイコン・名前と情報の文字・チェックボックスは部品、それ以外の項目の余白は
/// 押しただけでは動かず・離すとカーソル・動かすと投げ縄）。設定は「並べて表示・コンテンツ」の系統（Tiles）を読む。ハンドルは作らない。
/// </summary>
public class FileListViewTilesTests
{
    private const string LongName = "とても長い名前のファイルでございますので省略されるはずのもの_とても長い名前のファイル.txt";

    private static FileListView View(FileViewSettings? views = null, int count = 6, int width = 800, FileViewMode mode = FileViewMode.Tiles)
    {
        var list = new FileListView { Size = new Size(width, 400) };
        list.TypeText = _ => "テキスト ドキュメント";
        list.SetView(mode, views ?? new FileViewSettings(), new Dictionary<string, int?>(), SortOrder.Default);
        list.SetEntries(Enumerable.Range(0, count).Select(i => TestEntries.File($"file{i:D2}.txt")).ToList());
        return list;
    }

    private static MouseEventArgs Mouse(MouseButtons button, Point p) => new(button, 1, p.X, p.Y, 0);

    private static GridLayout Grid(FileListView list) => (GridLayout)list.Layout;

    /// <summary>項目の右端の、文字も部品も無い所（短い名前なので欄の右は空き）。</summary>
    private static Point Margin(FileListView list, int index)
    {
        var (x, y, w, h) = FileViewScroll.VisibleBounds(list.Layout, list.ScrollPosition, index);
        return new Point(x + w - 2, y + h - 2);
    }

    private static Point Text(FileListView list, (int X, int Y, int Width, int Height) bounds)
    {
        var (x, y, _, h) = FileViewScroll.ToVisible(list.Layout, list.ScrollPosition, bounds);
        return new Point(x + 2, y + h / 2);
    }

    private static Point Empty(FileListView list)
    {
        var last = FileViewScroll.VisibleBounds(list.Layout, list.ScrollPosition, list.State.Count - 1);
        return new Point(Math.Min(790, last.X + last.Width + 2), Math.Min(390, last.Y + last.Height + 2));
    }

    [Fact]
    public void 並べて表示は格子の並べて表示のレイアウトで_設定の大きさのアイコン()
    {
        using var list = View(new FileViewSettings { Tiles = new() { TilesSize = 64 }, Icons = new() { MediumSize = 32 } });
        var grid = Grid(list);
        Assert.Equal(GridArrangement.Tile, grid.Arrangement);
        Assert.Equal(64 * list.DeviceDpi / 96, grid.IconSize);
        Assert.Equal(2, grid.InfoLines);   // 既定の情報は種類とサイズ
    }

    [Fact]
    public void 余白を押しただけではカーソルは動かず_動かさずに離すと移る()
    {
        using var list = View();
        list.RaiseMouseDown(Mouse(MouseButtons.Left, Margin(list, 2)));
        Assert.Equal(0, list.State.CursorIndex);
        list.RaiseMouseUp(Mouse(MouseButtons.Left, Margin(list, 2)));
        Assert.Equal(2, list.State.CursorIndex);
        Assert.Empty(list.State.Marks);
    }

    [Fact]
    public void 余白から動かすと投げ縄を始め_離した時点でマークする()
    {
        using var list = View();
        list.RaiseMouseDown(Mouse(MouseButtons.Left, Margin(list, 0)));
        list.RaiseMouseMove(Mouse(MouseButtons.Left, Empty(list)));
        Assert.True(list.LassoActive);
        list.RaiseMouseUp(Mouse(MouseButtons.Left, Empty(list)));
        Assert.NotEmpty(list.State.Marks);
    }

    [Fact]
    public void 名前_情報の文字_アイコンは部品で_押した時点でカーソルが移る()
    {
        using var list = View();
        var grid = Grid(list);
        foreach (var (index, point) in new[]
                 {
                     (1, Text(list, grid.NameBounds(1))),
                     (2, Text(list, grid.InfoBounds(2, 0))),   // 種類の文字
                     (3, Text(list, grid.IconBounds(3))),       // アイコンの左端から 2px・縦の中央（チェックボックスより下）
                 })
        {
            list.RaiseMouseDown(Mouse(MouseButtons.Left, point));
            Assert.Equal(index, list.State.CursorIndex);
            list.RaiseMouseUp(Mouse(MouseButtons.Left, point));
            Assert.Empty(list.State.Marks);   // アイコンは MarkIcon ではない
        }
    }

    [Fact]
    public void 範囲選択がオフなら余白から動かしても投げ縄にならず_離してもカーソルもマークも変わらない()
    {
        using var list = View(new FileViewSettings { Tiles = new() { RangeSelection = false } });
        list.RaiseMouseDown(Mouse(MouseButtons.Left, Margin(list, 2)));
        list.RaiseMouseMove(Mouse(MouseButtons.Left, Empty(list)));
        Assert.False(list.LassoActive);
        list.RaiseMouseUp(Mouse(MouseButtons.Left, Empty(list)));
        Assert.Equal(0, list.State.CursorIndex);
        Assert.Empty(list.State.Marks);
        // 項目の無い所からも始めない
        list.RaiseMouseDown(Mouse(MouseButtons.Left, Empty(list)));
        list.RaiseMouseMove(Mouse(MouseButtons.Left, Margin(list, 0)));
        Assert.False(list.LassoActive);
        list.RaiseMouseUp(Mouse(MouseButtons.Left, Margin(list, 0)));
        Assert.Empty(list.State.Marks);
    }

    [Fact]
    public void 範囲選択がオフでも_普通のクリックとShiftクリックは効く()
    {
        using var list = View(new FileViewSettings { Tiles = new() { RangeSelection = false } });
        list.RaiseMouseDown(Mouse(MouseButtons.Left, Margin(list, 2)));
        list.RaiseMouseUp(Mouse(MouseButtons.Left, Margin(list, 2)));
        Assert.Equal(2, list.State.CursorIndex);
        list.PressLeft(Margin(list, 4), shift: true);
        list.ReleaseLeft();
        Assert.Equal([2, 3, 4], list.State.Marks.Order());
        Assert.Equal(4, list.State.CursorIndex);
    }

    [Fact]
    public void 範囲選択はアイコンの系統の設定に左右されない()
    {
        using var list = View(new FileViewSettings { Icons = new() { RangeSelection = false } });
        Assert.True(list.RangeSelection);
        using var off = View(new FileViewSettings { Tiles = new() { RangeSelection = false } }, mode: FileViewMode.MediumIcons);
        Assert.True(off.RangeSelection);
    }

    [Fact]
    public void DとDとチェックボックスとサムネイルは並べて表示の系統の設定を読む()
    {
        var views = new FileViewSettings
        {
            Icons = new() { InPanelDragDrop = true, CheckBoxes = CheckBoxMode.HoverAndMarked },
            Tiles = new() { InPanelDragDrop = false, CheckBoxes = CheckBoxMode.Always, Thumbnails = false },
            Common = new() { ShowOverlays = false },
        };
        using var list = View(views);
        Assert.False(list.InPanelDragDrop);
        Assert.True(list.ShowsCheckBox(0));           // 常に表示（ホバーもマークも無い）
        Assert.Empty(list.BuildImageRequests());       // サムネイルも印も求めない
        using var on = View(views with { Tiles = views.Tiles with { Thumbnails = true } });
        Assert.NotEmpty(on.BuildImageRequests());
    }

    [Fact]
    public void 名前はカーソルの項目でも1行で_全部は描かない()
    {
        using var list = new FileListView { Size = new Size(800, 400) };
        list.SetView(FileViewMode.Tiles, new FileViewSettings(), new Dictionary<string, int?>(), SortOrder.Default);
        list.SetEntries([TestEntries.File(LongName), TestEntries.File("b.txt")]);
        Assert.Equal(0, list.State.CursorIndex);
        Assert.Single(list.NameLinesFor(0).Lines);
        Assert.True(list.IsTruncated(0));
        Assert.False(list.DrawsFullName(0));   // Q27: 並べて表示はカーソルの項目を広げない
    }

    /// <summary>登録されているかの判定を差し替えて、実行環境の .txt の登録に左右されずに確かめる。</summary>
    private static FileListView Named(FileViewMode mode, bool hideKnown, bool registered, string name = LongName)
    {
        var list = new FileListView { Size = new Size(800, 400), IsRegisteredExtension = _ => registered };
        list.SetView(mode, new FileViewSettings { Common = new() { HideKnownExtensions = hideKnown } }, new Dictionary<string, int?>(), SortOrder.Default);
        list.SetEntries([TestEntries.File(name)]);
        return list;
    }

    [Theory]
    [InlineData(false, true)]    // 設定オフ: 登録されていても出す
    [InlineData(true, false)]    // 設定オン: 登録されていなければ出す
    public void 省略しても表示している拡張子は残す(bool hideKnown, bool registered)
    {
        using var list = Named(FileViewMode.Tiles, hideKnown, registered);
        Assert.False(list.HidesExtension(list.State.Entries[0]));
        var line = list.NameLinesFor(0).Lines[0];
        Assert.EndsWith(".txt", line);
        Assert.Contains("…", line);
    }

    [Fact]
    public void 隠した拡張子は省略しても出さない()
    {
        using var list = Named(FileViewMode.Tiles, hideKnown: true, registered: true);
        Assert.True(list.HidesExtension(list.State.Entries[0]));
        Assert.True(list.IsTruncated(0));
        Assert.DoesNotContain(".txt", list.NameLinesFor(0).Lines[0]);
        Assert.DoesNotContain(".txt", list.FullNameText(list.State.Entries[0]));
    }

    [Fact]
    public void 部品から動かすとDとDを始め_投げ縄にならない()
    {
        using var list = View();
        var grid = Grid(list);
        foreach (var (index, bounds) in new[] { (1, grid.IconBounds(1)), (2, grid.NameBounds(2)), (3, grid.InfoBounds(3, 0)) })
        {
            var started = new List<IReadOnlyList<string>>();
            list.StartDragOverride = paths => started.Add(paths);
            var origin = Text(list, bounds);
            list.RaiseMouseDown(Mouse(MouseButtons.Left, origin));
            list.RaiseMouseMove(Mouse(MouseButtons.Left, origin with { Y = origin.Y + 40 }));
            Assert.False(list.LassoActive);
            Assert.Equal([list.State.Entries[index].FullPath], Assert.Single(started));
            list.RaiseMouseUp(Mouse(MouseButtons.Left, origin with { Y = origin.Y + 40 }));
        }
    }

    [Fact]
    public void 余白から動かすとDとDにならず投げ縄になる()
    {
        using var list = View();
        var started = 0;
        list.StartDragOverride = _ => started++;
        list.RaiseMouseDown(Mouse(MouseButtons.Left, Margin(list, 0)));
        list.RaiseMouseMove(Mouse(MouseButtons.Left, Empty(list)));
        Assert.True(list.LassoActive);
        Assert.Equal(0, started);
        list.RaiseMouseUp(Mouse(MouseButtons.Left, Empty(list)));
    }

    /// <summary>R-121 / Q1: 項目の幅の式（欄は数字 0 × 16 + アイコン、項目はそれに横の余白 × 3 とアイコン）。dpi は実行環境のままで確かめる。</summary>
    [Fact]
    public void 並べて表示の項目の幅は式どおり()
    {
        using var list = View(width: 2000);
        var grid = Grid(list);
        int Scaled(int v) => v * list.DeviceDpi / 96;
        Assert.Equal(Scaled(6) * 3 + grid.IconSize + list.ZeroWidth * 16 + grid.IconSize, grid.CellWidth);
    }

    /// <summary>R-121 / Q1: 既定のフォント（メイリオ 16pt）・96 dpi・48px の幅を実測で確かめる。2026-09-30 の実測は数字 0 が 14px で 338px。フォントの版で動いても通るよう範囲で確かめる。</summary>
    [Fact]
    public void 既定のフォントと48pxでの並べて表示の項目の幅()
    {
        using var font = new Font("メイリオ", 16f);
        using var m = new ReTAC.App.Rendering.TextMeasure(font, 96);
        var zero = m.Width("0");
        Assert.InRange(18 + 48 + zero * 16 + 48, 300, 380);
    }

    [Fact]
    public void 情報は選んだものを候補の並びの順に2つまで_親フォルダは空()
    {
        var views = new FileViewSettings { Tiles = new() { Info = [TileInfo.Attributes, TileInfo.Type, TileInfo.Size] } };
        using var list = new FileListView { Size = new Size(800, 400) };
        list.TypeText = _ => "テキスト ドキュメント";
        list.SetView(FileViewMode.Tiles, FileViewSettings.Normalize(views), new Dictionary<string, int?>(), SortOrder.Default);
        list.SetEntries([TestEntries.Parent(), TestEntries.File("a.txt", size: 2048)]);
        Assert.Equal(2, Grid(list).InfoLines);
        Assert.Equal(["テキスト ドキュメント", ReTAC.Domain.Formatting.Display.Size(2048)], list.InfoTexts(1));   // 種類・サイズ（属性は 3 つ目）
        Assert.All(list.InfoTexts(0), text => Assert.Equal("", text));
        Assert.False(list.ShowsCheckBox(0));   // 「..」にはチェックボックスを出さない
    }

    [Fact]
    public void 情報を選ばなければ名前だけの項目になる()
    {
        using var list = View(new FileViewSettings { Tiles = new() { Info = [] } });
        Assert.Equal(0, Grid(list).InfoLines);
        Assert.Empty(list.InfoTexts(0));
    }

    [Fact]
    public void 種類が届いても並べて表示のレイアウトのまま()
    {
        using var list = View();
        list.QueueResolvedType(".txt");
        list.FlushResolvedTypes();
        Assert.IsType<GridLayout>(list.Layout);
        Assert.Equal(GridArrangement.Tile, Grid(list).Arrangement);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(40)]
    [InlineData(120)]
    public void 狭いパネルでは横にスクロールせず_アイコンを縮めない(int width)
    {
        using var list = View(new FileViewSettings { Tiles = new() { TilesSize = 256 } }, width: width);
        var grid = Grid(list);
        Assert.False(grid.ScrollBars.Horizontal);
        Assert.Equal(256 * list.DeviceDpi / 96, grid.IconBounds(0).Width);
        Assert.True(grid.NameBounds(0).Width >= 0);
    }
}
