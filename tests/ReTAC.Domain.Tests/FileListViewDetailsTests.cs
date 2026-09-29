using System.Drawing;
using System.Windows.Forms;
using ReTAC.App;
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
        using var bitmap = new Bitmap(500, 200);
        list.DrawToBitmap(bitmap, new Rectangle(0, 0, 500, 200));
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
}
