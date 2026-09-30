using System.Drawing;
using System.Windows.Forms;
using ReTAC.App;
using ReTAC.App.Rendering;
using ReTAC.Domain.Listing;
using SortOrder = ReTAC.Domain.Listing.SortOrder;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-122 / INV-DETAILS-ROW-HIT: コンテンツ。クリックとキーは詳細表示と同じ（アイコンと名前の欄は押した時点でカーソル、
/// それ以外は離したらカーソル・動かしたら投げ縄）。設定は「並べて表示・コンテンツ」の系統（Tiles）を読む。ハンドルは作らない。
/// </summary>
public class FileListViewContentTests
{
    private const string LongName = "とても長い名前のファイルでございますので省略されるはずのもの_とても長い名前のファイル.txt";

    private static FileListView View(FileViewSettings? views = null, int count = 6, int width = 900)
    {
        var list = new FileListView { Size = new Size(width, 600) };
        list.TypeText = _ => "テキスト ドキュメント";
        list.SetView(FileViewMode.Content, views ?? new FileViewSettings(), new Dictionary<string, int?>(), SortOrder.Default);
        list.SetEntries(Enumerable.Range(0, count).Select(i => TestEntries.File($"file{i:D2}.txt")).ToList());
        return list;
    }

    private static MouseEventArgs Mouse(MouseButtons button, Point p) => new(button, 1, p.X, p.Y, 0);

    private static ContentLayout Rows(FileListView list) => (ContentLayout)list.Layout;

    private static Point In(FileListView list, (int X, int Y, int Width, int Height) bounds, bool right = false)
    {
        var (x, y, w, h) = FileViewScroll.ToVisible(list.Layout, list.ScrollPosition, bounds);
        return new Point(right ? x + w - 2 : x + 2, y + h / 2);
    }

    /// <summary>左の 2 行目（選んだ情報の 1 つ目）。名前以外。</summary>
    private static Point Other(FileListView list, int index) => In(list, Rows(list).LeftInfoBounds(index));

    private static Point Below(FileListView list)
    {
        var last = FileViewScroll.VisibleBounds(list.Layout, list.ScrollPosition, list.State.Count - 1);
        return new Point(100, Math.Min(590, last.Y + last.Height + 10));
    }

    [Fact]
    public void コンテンツはコンテンツのレイアウトで_設定の大きさのアイコン()
    {
        using var list = View(new FileViewSettings { Tiles = new() { ContentSize = 32, TilesSize = 96 } });
        Assert.Equal(32 * list.DeviceDpi / 96, Rows(list).IconSize);
        Assert.False(list.Layout.ScrollBars.Horizontal);
    }

    [Fact]
    public void 名前以外を押しただけでは動かず_離すと移り_動かすと投げ縄()
    {
        using var list = View();
        list.RaiseMouseDown(Mouse(MouseButtons.Left, Other(list, 2)));
        Assert.Equal(0, list.State.CursorIndex);
        list.RaiseMouseUp(Mouse(MouseButtons.Left, Other(list, 2)));
        Assert.Equal(2, list.State.CursorIndex);

        list.RaiseMouseDown(Mouse(MouseButtons.Left, Other(list, 0)));
        list.RaiseMouseMove(Mouse(MouseButtons.Left, Below(list)));
        Assert.True(list.LassoActive);
        list.RaiseMouseUp(Mouse(MouseButtons.Left, Below(list)));
        Assert.NotEmpty(list.State.Marks);
    }

    [Fact]
    public void 名前の欄は文字の右の空きでも押した時点でカーソルが移る()
    {
        using var list = View();
        var point = In(list, Rows(list).NameBounds(3), right: true);
        list.RaiseMouseDown(Mouse(MouseButtons.Left, point));
        Assert.Equal(3, list.State.CursorIndex);
        list.RaiseMouseUp(Mouse(MouseButtons.Left, point));
        Assert.Empty(list.State.Marks);
    }

    [Fact]
    public void チェックボックスは離した時点でマークを切り替える()
    {
        using var list = View(new FileViewSettings { Tiles = new() { CheckBoxes = CheckBoxMode.Always } });
        var point = In(list, Rows(list).CheckBoxBounds(1)!.Value);
        list.RaiseMouseDown(Mouse(MouseButtons.Left, point));
        Assert.Empty(list.State.Marks);
        list.RaiseMouseUp(Mouse(MouseButtons.Left, point));
        Assert.Equal([1], list.State.Marks);
    }

    [Fact]
    public void 範囲選択がオフなら名前以外から動かしても投げ縄にならず_離してもカーソルもマークも変わらない()
    {
        using var list = View(new FileViewSettings { Tiles = new() { RangeSelection = false } });
        list.RaiseMouseDown(Mouse(MouseButtons.Left, Other(list, 2)));
        list.RaiseMouseMove(Mouse(MouseButtons.Left, Below(list)));
        Assert.False(list.LassoActive);
        list.RaiseMouseUp(Mouse(MouseButtons.Left, Below(list)));
        Assert.Equal(0, list.State.CursorIndex);
        Assert.Empty(list.State.Marks);
        list.RaiseMouseDown(Mouse(MouseButtons.Left, Below(list)));   // 項目の無い所からも始めない
        list.RaiseMouseMove(Mouse(MouseButtons.Left, Other(list, 0)));
        Assert.False(list.LassoActive);
        list.RaiseMouseUp(Mouse(MouseButtons.Left, Other(list, 0)));
        Assert.Empty(list.State.Marks);
    }

    [Fact]
    public void 範囲選択がオフでも_普通のクリックとShiftクリックは効く()
    {
        using var list = View(new FileViewSettings { Tiles = new() { RangeSelection = false } });
        list.RaiseMouseDown(Mouse(MouseButtons.Left, Other(list, 1)));
        list.RaiseMouseUp(Mouse(MouseButtons.Left, Other(list, 1)));
        Assert.Equal(1, list.State.CursorIndex);
        list.PressLeft(Other(list, 3), shift: true);
        list.ReleaseLeft();
        Assert.Equal([1, 2, 3], list.State.Marks.Order());
        Assert.Equal(3, list.State.CursorIndex);
    }

    [Fact]
    public void 上下は1行ずつ_左右は何もしない()
    {
        using var list = View();
        list.PressKey(System.Windows.Forms.Keys.Down);
        Assert.Equal(1, list.State.CursorIndex);
        list.PressKey(System.Windows.Forms.Keys.Right);
        Assert.Equal(1, list.State.CursorIndex);
        list.PressKey(System.Windows.Forms.Keys.Left);
        Assert.Equal(1, list.State.CursorIndex);
        Assert.Equal(new ScrollPosition(0, 0), list.ScrollPosition);
    }

    [Fact]
    public void 情報は左に1つ目_右に2つ目と3つ目を見出し付きで()
    {
        var views = new FileViewSettings { Tiles = new() { Info = [TileInfo.Type, TileInfo.Size, TileInfo.Modified] } };
        using var list = View(views);
        Assert.Equal(2, Rows(list).RightCount);
        Assert.Equal(3, list.InfoTexts(0).Count);
        Assert.StartsWith("サイズ: ", list.RightInfoText(TileInfo.Size, "1 KB"));
        Assert.Equal("更新日時: x", list.RightInfoText(TileInfo.Modified, "x"));
    }

    [Fact]
    public void 狭いと右の欄を下の段から省く()
    {
        var views = new FileViewSettings { Tiles = new() { Info = [TileInfo.Type, TileInfo.Size, TileInfo.Modified], ContentSize = 256 } };
        using var wide = View(views, width: 1600);
        using var narrow = View(views, width: 200);
        Assert.Equal(2, Rows(wide).RightCount);
        Assert.True(Rows(narrow).RightCount < 2);
        Assert.False(narrow.Layout.ScrollBars.Horizontal);
        Assert.Equal(256 * narrow.DeviceDpi / 96, Rows(narrow).IconBounds(0).Width);
    }

    [Fact]
    public void 情報を選ばなければ右の欄は無い()
    {
        using var list = View(new FileViewSettings { Tiles = new() { Info = [] } });
        Assert.Equal(0, Rows(list).RightCount);
        Assert.Empty(list.InfoTexts(0));
    }

    /// <summary>登録されているかの判定を差し替えて、実行環境の .txt の登録に左右されずに確かめる。</summary>
    private static FileListView Named(bool hideKnown, bool registered)
    {
        var list = new FileListView { Size = new Size(600, 400), IsRegisteredExtension = _ => registered };
        list.SetView(FileViewMode.Content, new FileViewSettings { Common = new() { HideKnownExtensions = hideKnown } },
            new Dictionary<string, int?>(), SortOrder.Default);
        list.SetEntries([TestEntries.File(LongName)]);
        return list;
    }

    [Fact]
    public void 名前はカーソルの項目でも1行で_全部は描かない()
    {
        using var list = Named(hideKnown: false, registered: true);
        Assert.Equal(0, list.State.CursorIndex);
        Assert.Single(list.NameLinesFor(0).Lines);
        Assert.False(list.DrawsFullName(0));   // Q27: コンテンツはカーソルの項目を広げない
    }

    [Theory]
    [InlineData(false, true)]    // 設定オフ: 登録されていても出す
    [InlineData(true, false)]    // 設定オン: 登録されていなければ出す
    public void 省略しても表示している拡張子は残す(bool hideKnown, bool registered)
    {
        using var list = Named(hideKnown, registered);
        var line = list.NameLinesFor(0).Lines[0];
        Assert.EndsWith(".txt", line);
        Assert.Contains("…", line);
    }

    [Fact]
    public void 隠した拡張子は省略しても出さない()
    {
        using var list = Named(hideKnown: true, registered: true);
        Assert.True(list.IsTruncated(0));
        Assert.DoesNotContain(".txt", list.NameLinesFor(0).Lines[0]);
    }

    [Fact]
    public void アイコンと名前の欄から動かすとDとDを始め_投げ縄にならない()
    {
        using var list = View();
        var rows = Rows(list);
        foreach (var (index, bounds, right) in new[] { (1, rows.IconBounds(1), false), (2, rows.NameBounds(2), true) })
        {
            var started = new List<IReadOnlyList<string>>();
            list.StartDragOverride = paths => started.Add(paths);
            var start = In(list, bounds, right);
            list.RaiseMouseDown(Mouse(MouseButtons.Left, start));
            list.RaiseMouseMove(Mouse(MouseButtons.Left, start with { Y = start.Y + 60 }));
            Assert.False(list.LassoActive);
            Assert.Equal([list.State.Entries[index].FullPath], Assert.Single(started));
            list.RaiseMouseUp(Mouse(MouseButtons.Left, start with { Y = start.Y + 60 }));
        }
    }

    [Fact]
    public void 名前以外から動かすとDとDにならず投げ縄になる()
    {
        var views = new FileViewSettings { Tiles = new() { Info = [TileInfo.Type, TileInfo.Size, TileInfo.Modified] } };
        using var list = View(views);
        var started = 0;
        list.StartDragOverride = _ => started++;
        foreach (var from in new[] { Other(list, 0), In(list, Rows(list).RightInfoBounds(0, 0)!.Value) })
        {
            list.RaiseMouseDown(Mouse(MouseButtons.Left, from));
            list.RaiseMouseMove(Mouse(MouseButtons.Left, Below(list)));
            Assert.True(list.LassoActive);
            list.RaiseMouseUp(Mouse(MouseButtons.Left, Below(list)));
        }
        Assert.Equal(0, started);
    }

    [Fact]
    public void 区切り線はハイコントラストではOSの文字色_それ以外は見出しの線の色()
    {
        var theme = Theme.Default;
        Assert.Equal(theme.Foreground, RowColors.Separator(theme, highContrast: true));
        Assert.Equal(RowColors.Header(theme).Line, RowColors.Separator(theme, highContrast: false));
    }

    [Fact]
    public void 区切り線だけの描き直しでも_その上の行を描く()
    {
        using var list = View(count: 3);
        var rows = Rows(list);
        var sep = FileViewScroll.ToVisible(rows, list.ScrollPosition, rows.SeparatorBounds(0)!.Value);
        var clip = new Rectangle(sep.X, sep.Y, sep.Width, sep.Height);
        Assert.True(list.PaintBounds(0).IntersectsWith(clip));    // 線を描くのは上の行
        Assert.False(list.PaintBounds(1).IntersectsWith(clip));
        Assert.Equal(list.DropFrameBounds(2), list.PaintBounds(2));   // 最後の行は線が無いので行の矩形だけ
    }
}
