using System.Drawing;
using System.Windows.Forms;
using ReTAC.App;
using ReTAC.Domain.Entries;
using ReTAC.Domain.Listing;
using SortOrder = ReTAC.Domain.Listing.SortOrder;

namespace ReTAC.Domain.Tests;

/// <summary>R-114 / INV-DETAILS-ROW-HIT: 詳細表示のファイルリスト。</summary>
public class FileListViewDetailsTests
{
    private static FileListView Details(int count, Dictionary<string, int?>? widths = null, bool fit = false)
    {
        var list = new FileListView { Size = new Size(500, 200) };
        list.SetEntries(Enumerable.Range(0, count).Select(i => TestEntries.File($"file{i:D3}.txt")).ToList());
        var views = new FileViewSettings { Details = new() { FitColumnsToWindow = fit } };
        list.SetView(FileViewMode.Details, views, widths ?? [], SortOrder.Default);
        return list;
    }

    [Fact]
    public void 詳細表示のレイアウトになり見出しは名前から既定の列の順()
    {
        using var list = Details(3);
        var layout = Assert.IsType<DetailsLayout>(list.Layout);
        Assert.Equal(new DetailsColumn?[] { null, DetailsColumn.Size, DetailsColumn.Modified, DetailsColumn.Type, DetailsColumn.Attributes },
            layout.Header.Select(h => h.Column));
    }

    [Fact]
    public void 項目が無くても見出しは出て例外にならない()
    {
        using var list = Details(0);
        Assert.NotEmpty(list.Layout.Header);
        list.PressLeft(new Point(50, list.Layout.HeaderHeight + 5), shift: false);
        // DrawToBitmap はハンドルを作り、OnHandleCreated の AllowDrop が OLE を呼ぶ。xunit のスレッドは MTA なので
        // そこで例外になり、WinForms の例外ダイアログが出てテストの実行が止まる。描画は STA のスレッドで行う
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var bitmap = new Bitmap(500, 200);
                using var drawn = Details(0);
                drawn.DrawToBitmap(bitmap, new Rectangle(0, 0, 500, 200));
            }
            catch (Exception e) { error = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(error);
    }

    [Fact]
    public void 名前以外を押しただけではカーソルは動かない()
    {
        using var list = Details(10);
        var y = list.Layout.HeaderHeight + list.Layout.ItemBounds(3).Y + 2;
        var sizeCell = list.Layout.Header[1];
        list.PressLeft(new Point(sizeCell.X + 2, y), shift: false);
        Assert.Equal(0, list.State.CursorIndex);
    }

    [Fact]
    public void 名前の列を押すとすぐカーソルが動く()
    {
        using var list = Details(10);
        var y = list.Layout.HeaderHeight + list.Layout.ItemBounds(3).Y + 2;
        list.PressLeft(new Point(list.Layout.Header[0].Width - 3, y), shift: false);   // 名前の列の中の余白（Q31）
        Assert.Equal(3, list.State.CursorIndex);
    }

    [Fact]
    public void 列の最小幅は見出しの文字だけで決まりソートの印の幅を含まない()
    {
        using var list = Details(0);   // 項目が無ければ列の幅は最小幅
        var layout = Assert.IsType<DetailsLayout>(list.Layout);
        var theme = ReTAC.App.Rendering.Theme.Default;
        using var font = new Font(theme.FontFamily, theme.FontSize);
        using var measure = new ReTAC.App.Rendering.TextMeasure(font, list.DeviceDpi);
        var size = layout.Header.Single(h => h.Column == DetailsColumn.Size);
        Assert.Equal(measure.Width(ReTAC.App.Rendering.DetailsCells.Header(DetailsColumn.Size)) + layout.ColumnPadding * 2, size.Width);
    }

    [Fact]
    public void 手動の幅はdpiで拡大して使う()
    {
        using var list = Details(3, new Dictionary<string, int?> { ["Size"] = 150 });
        var size = list.Layout.Header.Single(h => h.Column == DetailsColumn.Size);
        Assert.Equal(DetailsColumnWidths.ToPixels(150, list.DeviceDpi), size.Width);
    }

    [Fact]
    public void 左右は横スクロールでカーソルは動かない()
    {
        using var list = Details(3, new Dictionary<string, int?> { ["Name"] = 2000 });
        list.Focus();
        list.ScrollColumns(-1);   // 右へ 1 段
        Assert.Equal(1, list.ScrollPosition.X);
        Assert.Equal(0, list.State.CursorIndex);
    }

    [Fact]
    public void 切り替えても項目とカーソルとマークは保たれる()
    {
        using var list = Details(50);
        list.State.ToggleMark(4);
        list.MoveCursorTo(40);
        list.SetView(FileViewMode.List, new FileViewSettings(), new Dictionary<string, int?>(), SortOrder.Default);
        Assert.Equal(40, list.State.CursorIndex);
        Assert.Contains(4, list.State.Marks);
        Assert.Equal(50, list.State.Count);
        list.SetView(FileViewMode.Details, new FileViewSettings(), new Dictionary<string, int?>(), SortOrder.Default);
        Assert.Equal(40, list.State.CursorIndex);
        var (_, y, _, h) = FileViewScroll.VisibleBounds(list.Layout, list.ScrollPosition, 40);
        Assert.True(y >= list.Layout.HeaderHeight && y + h <= 200);   // カーソルが見える位置
    }

    [Fact]
    public void 詳細表示ではカーソルの項目を全部描かない()
    {
        // Q27: 8 つのモードの表のうち Phase 15 の 2 行（一覧は FileListViewNameTests）。Phase 16・17 でモードを作るたびに行を足す
        using var list = new FileListView { Size = new Size(500, 200) };
        list.SetEntries([TestEntries.File("とても長い資料の名前がここに続いていてまだ終わらないもっと長い名前.xlsx")]);
        list.SetView(FileViewMode.Details, new FileViewSettings { Details = new() { NameWidth = new() { Mode = NameWidthMode.MaxChars, MaxChars = 10 } } },
            new Dictionary<string, int?>(), SortOrder.Default);
        Assert.True(list.IsTruncated(0));
        Assert.False(list.DrawsFullName(0));
    }

    [Fact]
    public void 見出しのメニューは2項目と区切りと名前以外の列()
    {
        using var list = Details(3);
        using var menu = list.DetailsHeaderMenu(cellIndex: 1);
        Assert.Equal(["列のサイズを自動的に変更する", "すべての列のサイズを自動的に変更する", ""],
            menu.Items.Cast<ToolStripItem>().Take(3).Select(i => i is ToolStripSeparator ? "" : i.Text));
        var columns = menu.Items.Cast<ToolStripItem>().Skip(3).Cast<ToolStripMenuItem>().ToList();
        Assert.Equal(6, columns.Count);
        Assert.Equal([true, true, true, true, false, false], columns.Select(c => c.Checked));   // 既定の並び（R-112-3）
    }

    // ---- R-114: 種類名が 1 件ずつ届いても、項目の全走査は増えない ----

    private const int Kinds = 200;

    private static string KindName(int i) => new string('あ', 1 + i * 7 % 40) + i;   // 幅がまちまちで、最長は途中に来る

    /// <summary>拡張子 200 種類の項目を並べ、種類名を後から埋められる一覧（種類名は seam で差す）。</summary>
    private static (FileListView List, Dictionary<string, string> Names, List<Entry> Entries) TypeList(FileViewMode mode, bool typeVisible = true, bool prefill = false)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var list = new FileListView { Size = new Size(500, 200) };
        list.TypeText = e => names.GetValueOrDefault(e.Extension, "");
        var entries = Enumerable.Range(0, Kinds).Select(i => TestEntries.File($"file{i}.x{i}")).ToList();
        if (prefill) for (var i = 0; i < Kinds; i++) names[entries[i].Extension] = KindName(i);
        var columns = DetailsViewSettings.DefaultColumns.Select(c => c.Column == DetailsColumn.Type ? c with { Visible = typeVisible } : c).ToList();
        list.SetEntries(entries);
        list.SetView(mode, new FileViewSettings { Details = new() { Columns = columns } }, new Dictionary<string, int?>(), SortOrder.Default);
        return (list, names, entries);
    }

    private static int TypeWidth(FileListView list) => list.Layout.Header.Single(h => h.Column == DetailsColumn.Type).Width;

    private static void Deliver(FileListView list, Dictionary<string, string> names, IEnumerable<Entry> entries)
    {
        var i = 0;
        foreach (var entry in entries)
        {
            names[entry.Extension] = KindName(i++);
            Assert.True(list.QueueResolvedType(ReTAC.Shell.ShellFileType.KeyOf(entry.FullPath, false)));   // 1 件ごとに別の UI の番
            list.FlushResolvedTypes();
        }
    }

    [Fact]
    public void 種類名が1件ずつ200回届いても全走査は増えず最終の列幅は最長の名前に合う()
    {
        var (list, names, entries) = TypeList(FileViewMode.Details);
        using var _ = list;
        var before = list.ContentScanCount;
        Assert.Equal(1, before);

        Deliver(list, names, entries);

        Assert.Equal(before, list.ContentScanCount);
        // 全部そろった状態から新しく測った幅と同じ
        var (fresh, _, _) = TypeList(FileViewMode.Details, prefill: true);
        using var _f = fresh;
        Assert.Equal(TypeWidth(fresh), TypeWidth(list));
        Assert.True(TypeWidth(list) > TypeWidth(TypeList(FileViewMode.Details).List));   // 空欄のときより広がっている
    }

    [Fact]
    public void 種類の列を隠していると届いても走査も組み直しもしない()
    {
        var (list, names, entries) = TypeList(FileViewMode.Details, typeVisible: false);
        using var _ = list;
        var before = list.ContentScanCount;
        var layout = list.Layout;

        Deliver(list, names, entries);

        Assert.Equal(before, list.ContentScanCount);
        Assert.Same(layout, list.Layout);
    }

    [Fact]
    public void 一覧では届いても走査しない_詳細へ戻すと測り直して合う()
    {
        var (list, names, entries) = TypeList(FileViewMode.List);
        using var _ = list;
        var before = list.ContentScanCount;

        Deliver(list, names, entries);
        Assert.Equal(before, list.ContentScanCount);

        list.SetView(FileViewMode.Details, new FileViewSettings(), new Dictionary<string, int?>(), SortOrder.Default);
        Assert.Equal(before + 1, list.ContentScanCount);   // 一覧のあいだに届いた分はここで 1 回だけ測る
        var (fresh, _, _) = TypeList(FileViewMode.Details, prefill: true);
        using var _f = fresh;
        Assert.Equal(TypeWidth(fresh), TypeWidth(list));
    }

    [Fact]
    public void リサイズと列幅の変更では走査しない()
    {
        var (list, _, _) = TypeList(FileViewMode.Details);
        using var _ = list;
        var before = list.ContentScanCount;

        list.Size = new Size(700, 300);
        list.SetView(FileViewMode.Details, new FileViewSettings(), new Dictionary<string, int?> { ["Size"] = 150 }, SortOrder.Default);
        list.ScrollColumns(1);

        Assert.Equal(before, list.ContentScanCount);
    }

    [Fact]
    public void 親フォルダの行があっても拡張子の無いファイルの種類名で列が広がる()
    {
        var names = new Dictionary<string, string>();
        using var list = new FileListView { Size = new Size(500, 200) };
        list.TypeText = e => e.IsParent ? "" : names.GetValueOrDefault(e.Extension, "");
        list.SetEntries([TestEntries.Parent(), TestEntries.File("README"), TestEntries.File("LICENSE")]);
        list.SetView(FileViewMode.Details, new FileViewSettings(), new Dictionary<string, int?>(), SortOrder.Default);
        var before = TypeWidth(list);

        names[""] = new string('あ', 30);
        Assert.True(list.QueueResolvedType(ReTAC.Shell.ShellFileType.KeyOf(@"C:\work\README", false)));
        list.FlushResolvedTypes();

        Assert.True(TypeWidth(list) > before);
    }

    [Fact]
    public void 種類の列が広がって横のバーが出ても最下行のカーソルは見える範囲に残る()
    {
        var names = new Dictionary<string, string>();
        using var list = new FileListView { Size = new Size(700, 200) };
        list.TypeText = e => names.GetValueOrDefault(e.Extension, "");
        list.SetEntries([TestEntries.File("f0.x")]);
        list.SetView(FileViewMode.Details, new FileViewSettings(), new Dictionary<string, int?>(), SortOrder.Default);
        // 高さを 8 行分ちょうどにして 8 件並べ、最下行へカーソルを置く（横のバーの 1 行ぶんで縦のバーが要るようになる）
        const int rows = 8;
        var height = list.Layout.HeaderHeight + rows * list.Layout.ItemBounds(1).Y;
        list.Size = new Size(700, height);
        list.SetEntries(Enumerable.Range(0, rows).Select(i => TestEntries.File($"f{i}.x")).ToList());
        Assert.False(list.Layout.ScrollBars.Horizontal);
        Assert.False(list.Layout.ScrollBars.Vertical);
        list.MoveCursorTo(rows - 1);

        names[".x"] = new string('あ', 60);
        list.QueueResolvedType(ReTAC.Shell.ShellFileType.KeyOf(@"C:\work0.x", false));
        list.FlushResolvedTypes();

        Assert.True(list.Layout.ScrollBars.Horizontal);
        var (_, y, _, h) = FileViewScroll.VisibleBounds(list.Layout, list.ScrollPosition, list.State.CursorIndex);
        var viewportHeight = height - list.Layout.HeaderHeight - SystemInformation.HorizontalScrollBarHeight;
        Assert.True(y >= list.Layout.HeaderHeight && y + h <= list.Layout.HeaderHeight + viewportHeight);
    }
}
