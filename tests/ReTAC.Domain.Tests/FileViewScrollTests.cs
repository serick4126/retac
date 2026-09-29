using ReTAC.Domain.Listing;

namespace ReTAC.Domain.Tests;

/// <summary>R-110-1〜R-110-3: 横だけの一覧と、縦横が同時にあるレイアウト（Phase 15 の詳細表示の形）の両方に、同じドロップの計算が効く</summary>
public class FileViewScrollTests
{
    // 行高 20・25 行・4 列（100 件）。列幅は 4+16+4+100+4+30+4 = 162。幅 500 に丸ごと入る列は 3 つ
    private static ColumnLayout Columns() =>
        ColumnLayout.Compute(100, maxBaseWidth: 100, maxExtensionWidth: 30, lineHeight: 18, viewportHeight: 500,
            iconWidth: 16, gap: 4, rowPadding: 2, columnPadding: 4);

    /// <summary>縦に行が並び、行の幅 800 が見えている幅より広いと横にもスクロールする試験用のレイアウト（横の 1 段は 100px）</summary>
    private sealed class GridLayout : IFileViewLayout
    {
        private const int RowHeight = 20, RowWidth = 800, HorizontalStep = 100;
        public int IndexAt(int x, int y, int entryCount) => x < RowWidth && y / RowHeight < entryCount ? y / RowHeight : -1;
        public (int X, int Y, int Width, int Height) ItemBounds(int index) => (0, index * RowHeight, RowWidth, RowHeight);
        public (int X, int Y) AutoScrollDirection(int x, int y, int viewportWidth, int viewportHeight) =>
            (x < RowHeight ? -1 : x >= viewportWidth - RowHeight ? 1 : 0, y < RowHeight ? -1 : y >= viewportHeight - RowHeight ? 1 : 0);
        public (int X, int Y) ScrollOffset(ScrollPosition position) => (position.X * HorizontalStep, position.Y * RowHeight);
        public ScrollPosition MaxScrollPosition(int entryCount, int viewportWidth, int viewportHeight) => new(
            Math.Max(0, (RowWidth - viewportWidth + HorizontalStep - 1) / HorizontalStep),
            Math.Max(0, entryCount - Math.Max(1, viewportHeight / RowHeight)));
        // 契約に足したメンバーはこの試験では使わない（ドロップの計算だけを見る）
        public int HeaderHeight => 0;
        public IReadOnlyList<HeaderCell> Header => [];
        public (bool Horizontal, bool Vertical) ScrollBars => (true, true);
        public bool ArrowsScrollHorizontally => true;
        public (int Index, FileViewArea Area) HitTest(int x, int y, int entryCount) { var i = IndexAt(x, y, entryCount); return (i, i < 0 ? FileViewArea.None : FileViewArea.Other); }
        public (int X, int Y, int Width, int Height) IconBounds(int index) => ItemBounds(index);
        public (int X, int Y, int Width, int Height) NameBounds(int index) => ItemBounds(index);
        public (int X, int Y, int Width, int Height) ExtensionBounds(int index) => ItemBounds(index);
        public IReadOnlyList<int> IndexesIn(int x, int y, int width, int height, int entryCount) => [];
        public int Arrow(int index, int dx, int dy, int entryCount) => index;
        public int PageItems(int viewportWidth, int viewportHeight) => 1;
        public ScrollPosition Reveal(int index, ScrollPosition current, int viewportWidth, int viewportHeight) => current;
        public (int X, int Y) VisibleSteps(int viewportWidth, int viewportHeight) => (1, 1);    }

    [Fact]
    public void 一覧は左右の端だけで横に1列進む()
    {
        IFileViewLayout layout = Columns();
        Assert.Equal((-1, 0), layout.AutoScrollDirection(5, 250, 500, 500));
        Assert.Equal((1, 0), layout.AutoScrollDirection(490, 250, 500, 500));
        Assert.Equal((0, 0), layout.AutoScrollDirection(250, 5, 500, 500));      // 上下の端では動かない
        var next = FileViewScroll.Next(layout, new(0, 0), (1, 0), 100, 500, 500);
        Assert.Equal(new ScrollPosition(1, 0), next);
        Assert.Equal((Columns().ColumnWidth, 0), layout.ScrollOffset(next));
        Assert.Equal(new ScrollPosition(1, 0), layout.MaxScrollPosition(100, 500, 500));
    }

    [Fact]
    public void 縦のレイアウトは下の端で1行進む()
    {
        IFileViewLayout layout = new GridLayout();
        Assert.Equal((0, 1), layout.AutoScrollDirection(250, 490, 500, 500));
        var next = FileViewScroll.Next(layout, new(0, 0), (0, 1), 100, 500, 500);
        Assert.Equal(new ScrollPosition(0, 1), next);
        Assert.Equal((0, 20), layout.ScrollOffset(next));
    }

    [Fact]
    public void 角では縦横に同時に進む()
    {
        IFileViewLayout layout = new GridLayout();
        var direction = layout.AutoScrollDirection(495, 495, 500, 500);
        Assert.Equal((1, 1), direction);
        Assert.Equal(new ScrollPosition(1, 1), FileViewScroll.Next(layout, new(0, 0), direction, 100, 500, 500));
    }

    [Fact]
    public void スクロールできる端では軸ごとに止まる()
    {
        IFileViewLayout layout = new GridLayout();
        var max = layout.MaxScrollPosition(100, 500, 500);
        Assert.Equal(new ScrollPosition(3, 75), max);
        Assert.Equal(max, FileViewScroll.Next(layout, max, (1, 1), 100, 500, 500));
        Assert.Equal(new ScrollPosition(0, 0), FileViewScroll.Next(layout, new(0, 0), (-1, -1), 100, 500, 500));
        Assert.Equal(new ScrollPosition(3, 1), FileViewScroll.Next(layout, new(3, 0), (1, 1), 100, 500, 500));   // 横は端、縦は進む
    }

    [Fact]
    public void 縦にスクロールした後も当たり判定と枠の位置が合う()
    {
        IFileViewLayout layout = new GridLayout();
        var scrolled = new ScrollPosition(0, 3);
        Assert.Equal(3, FileViewScroll.IndexAt(layout, scrolled, 10, 5, 100));          // 見えている先頭の行は 4 行目
        Assert.Equal((0, 0, 800, 20), FileViewScroll.VisibleBounds(layout, scrolled, 3));
        Assert.Equal((0, 20, 800, 20), FileViewScroll.VisibleBounds(layout, scrolled, 4));
    }

    [Fact]
    public void 縦横にスクロールした後も当たり判定と枠の位置が合う()
    {
        IFileViewLayout layout = new GridLayout();
        var scrolled = new ScrollPosition(2, 3);
        Assert.Equal(3, FileViewScroll.IndexAt(layout, scrolled, 10, 5, 100));
        Assert.Equal(-1, FileViewScroll.IndexAt(layout, scrolled, 650, 5, 100));        // 中身の x = 850。行の幅 800 の外
        Assert.Equal((-200, 0, 800, 20), FileViewScroll.VisibleBounds(layout, scrolled, 3));
    }

    [Fact]
    public void 一覧を横にスクロールした後も当たり判定と枠の位置が合う()
    {
        IFileViewLayout layout = Columns();
        var scrolled = new ScrollPosition(1, 0);
        Assert.Equal(25, FileViewScroll.IndexAt(layout, scrolled, 5, 5, 100));          // 2 列目の先頭
        Assert.Equal((0, 0, Columns().ColumnWidth, 20), FileViewScroll.VisibleBounds(layout, scrolled, 25));
        Assert.Equal((Columns().ColumnWidth, 60, Columns().ColumnWidth, 20), layout.ItemBounds(28));   // 中身の座標では 2 列目の 4 行目
    }

    [Fact]
    public void 見出しの無いレイアウトでは見えている座標と中身の座標のずれはスクロールだけ()
    {
        var layout = ColumnLayout.Compute(23, 120, 30, 16, 100, 16, 4, 2, 4);
        var position = new ScrollPosition(1, 0);
        var (x, y, _, _) = FileViewScroll.VisibleBounds(layout, position, 5);
        Assert.Equal(layout.ItemBounds(5).X - layout.ColumnWidth, x);
        Assert.Equal(layout.ItemBounds(5).Y, y);
        Assert.Equal(5, FileViewScroll.IndexAt(layout, position, x + 1, y + 1, 23));
    }
}
