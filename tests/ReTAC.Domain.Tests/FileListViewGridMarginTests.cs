using System.Drawing;
using System.Windows.Forms;
using ReTAC.App;
using ReTAC.Domain.Listing;
using SortOrder = ReTAC.Domain.Listing.SortOrder;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-120: 中〜特大の項目の余白（アイコン・名前の文字・チェックボックスに当たらない所）は、詳細表示の名前以外と同じ。
/// 押しただけでは動かず、動かさずに離すとカーソルを移し、動かすと投げ縄。名前の文字の上は従来どおり。ハンドルは作らない。
/// </summary>
public class FileListViewGridMarginTests
{
    private static FileListView View(int count = 6)
    {
        var list = new FileListView { Size = new Size(600, 400) };
        list.SetView(FileViewMode.MediumIcons, new FileViewSettings(), new Dictionary<string, int?>(), SortOrder.Default);
        list.SetEntries(Enumerable.Range(0, count).Select(i => TestEntries.File($"file{i:D2}.txt")).ToList());
        return list;
    }

    private static MouseEventArgs Mouse(MouseButtons button, Point p) => new(button, 1, p.X, p.Y, 0);

    private static Point Margin(FileListView list, int index)
    {
        var (x, y, _, _) = FileViewScroll.VisibleBounds(list.Layout, list.ScrollPosition, index);
        return new Point(x + 1, y + 1);   // チェックボックスの手前の角
    }

    /// <summary>名前の文字の真ん中（1 行目。どんな幅の文字でも中央は文字の上）。</summary>
    private static Point NameText(FileListView list, int index)
    {
        var grid = (GridLayout)list.Layout;
        var name = FileViewScroll.ToVisible(grid, list.ScrollPosition, grid.NameBounds(index));
        return new Point(name.X + name.Width / 2, name.Y + grid.NameHeight / 2 / 2);
    }

    private static Point Empty(FileListView list)
    {
        var last = FileViewScroll.VisibleBounds(list.Layout, list.ScrollPosition, list.State.Count - 1);
        return new Point(Math.Min(590, last.X + last.Width + 2), Math.Min(390, last.Y + last.Height + 2));
    }

    [Fact]
    public void 余白から動かすと投げ縄を始め_離した時点でマークする()
    {
        using var list = View();
        list.RaiseMouseDown(Mouse(MouseButtons.Left, Margin(list, 0)));
        Assert.Equal(0, list.State.CursorIndex);
        list.RaiseMouseMove(Mouse(MouseButtons.Left, Empty(list)));
        Assert.True(list.LassoActive);
        list.RaiseMouseUp(Mouse(MouseButtons.Left, Empty(list)));
        Assert.NotEmpty(list.State.Marks);
        Assert.False(list.IsHandleCreated);
    }

    [Fact]
    public void 余白を押しただけではカーソルは動かず_動かさずに離すと移る()
    {
        using var list = View();
        Assert.Equal(0, list.State.CursorIndex);
        list.RaiseMouseDown(Mouse(MouseButtons.Left, Margin(list, 2)));
        Assert.Equal(0, list.State.CursorIndex);   // 押しただけでは動かない
        list.RaiseMouseUp(Mouse(MouseButtons.Left, Margin(list, 2)));
        Assert.Equal(2, list.State.CursorIndex);
        Assert.Empty(list.State.Marks);
    }

    [Fact]
    public void 名前の文字を押すと従来どおりすぐカーソルが移り_動かしても投げ縄にならない()
    {
        using var list = View();
        list.RaiseMouseDown(Mouse(MouseButtons.Left, NameText(list, 2)));
        Assert.Equal(2, list.State.CursorIndex);
        list.RaiseMouseMove(Mouse(MouseButtons.Left, NameText(list, 2) with { X = NameText(list, 2).X + 1 }));   // 閾値の手前
        Assert.False(list.LassoActive);
        list.RaiseMouseUp(Mouse(MouseButtons.Left, NameText(list, 2)));
        Assert.Empty(list.State.Marks);
    }

    [Fact]
    public void 余白のShiftクリックは範囲マーク()
    {
        using var list = View();
        list.PressLeft(Margin(list, 3), shift: true);
        list.RaiseMouseUp(Mouse(MouseButtons.Left, Margin(list, 3)));
        Assert.Equal([0, 1, 2, 3], list.State.Marks.Order());
    }

    private static FileListView ViewWithoutRange(FileViewMode mode)
    {
        var list = new FileListView { Size = new Size(600, 400) };
        var off = new FileViewSettings
        {
            List = new() { RangeSelection = false }, Details = new() { RangeSelection = false }, Icons = new() { RangeSelection = false },
        };
        list.SetView(mode, off, new Dictionary<string, int?>(), SortOrder.Default);
        list.SetEntries(Enumerable.Range(0, 6).Select(i => TestEntries.File($"file{i:D2}.txt")).ToList());
        return list;
    }

    [Theory]
    [InlineData(FileViewMode.MediumIcons)]
    [InlineData(FileViewMode.SmallIcons)]
    [InlineData(FileViewMode.List)]
    [InlineData(FileViewMode.Details)]
    public void 範囲選択がオフなら項目の無い所から動かしても投げ縄にならない(FileViewMode mode)
    {
        using var list = ViewWithoutRange(mode);
        Assert.False(list.RangeSelection);
        list.RaiseMouseDown(Mouse(MouseButtons.Left, Empty(list)));
        list.RaiseMouseMove(Mouse(MouseButtons.Left, Margin(list, 0)));
        Assert.False(list.LassoActive);
        list.RaiseMouseUp(Mouse(MouseButtons.Left, Margin(list, 0)));
        Assert.Empty(list.State.Marks);
        Assert.Equal(0, list.State.CursorIndex);
    }

    [Fact]
    public void 範囲選択がオフなら余白から動かして離してもカーソルは移らない_動かさずに離せば移る()
    {
        using var list = ViewWithoutRange(FileViewMode.MediumIcons);
        list.RaiseMouseDown(Mouse(MouseButtons.Left, Margin(list, 2)));
        list.RaiseMouseMove(Mouse(MouseButtons.Left, Empty(list)));
        Assert.False(list.LassoActive);
        list.RaiseMouseUp(Mouse(MouseButtons.Left, Empty(list)));
        Assert.Equal(0, list.State.CursorIndex);   // 動かしたので保留のカーソル移動は捨てる
        Assert.Empty(list.State.Marks);

        list.RaiseMouseDown(Mouse(MouseButtons.Left, Margin(list, 2)));
        list.RaiseMouseUp(Mouse(MouseButtons.Left, Margin(list, 2)));
        Assert.Equal(2, list.State.CursorIndex);   // クリックは変わらない
    }

    [Fact]
    public void 範囲選択はモードの系統ごとの設定を見る()
    {
        using var list = ViewWithoutRange(FileViewMode.MediumIcons);
        var views = new FileViewSettings { Icons = new() { RangeSelection = false } };
        list.SetView(FileViewMode.Details, views, new Dictionary<string, int?>(), SortOrder.Default);
        Assert.True(list.RangeSelection);   // 詳細の系統はオンのまま
        list.SetView(FileViewMode.MediumIcons, views, new Dictionary<string, int?>(), SortOrder.Default);
        Assert.False(list.RangeSelection);
    }
}
