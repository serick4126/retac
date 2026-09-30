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
        public int AutoScrollBand => RowHeight;
        public (int X, int Y) AutoScrollDirection(int x, int y, int viewportWidth, int viewportHeight) =>
            (x < RowHeight ? -1 : x >= viewportWidth - RowHeight ? 1 : 0, y < RowHeight ? -1 : y >= viewportHeight - RowHeight ? 1 : 0);
        public (int X, int Y) ScrollOffset(ScrollPosition position) => (position.X * HorizontalStep, position.Y * RowHeight);
        public ScrollPosition MaxScrollPosition(int entryCount, int viewportWidth, int viewportHeight) => new(
            Math.Max(0, (RowWidth - viewportWidth + HorizontalStep - 1) / HorizontalStep),
            Math.Max(0, entryCount - Math.Max(1, viewportHeight / RowHeight)));
        // 契約に足したメンバーはこの試験では使わない（ドロップの計算だけを見る）
        public int HeaderHeight => 0;
        public IReadOnlyList<HeaderCell> Header => [];
    public (int X, int Y, int Width, int Height)? CellBounds(int index, DetailsColumn column) => null;
    public int HeaderBorderAt(int x, int tolerance) => -1;
    public int HeaderCellAt(int x) => -1;
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

    [Fact]
    public void 見出しのあるレイアウトでは見出しの上は項目なしで枠と当たり判定が往復する()
    {
        IFileViewLayout layout = DetailsLayout.Compute(new DetailsLayoutInput
        {
            EntryCount = 50, RowHeight = 20, IconWidth = 16, ColumnPadding = 4, HeaderHeight = 20, StepWidth = 32,
            Columns = [new(null, 200, 60, null), new(DetailsColumn.Size, 80, 40, null)],
            FitToWindow = false, ClientWidth = 500, ClientHeight = 200,
            VerticalBarWidth = 17, HorizontalBarHeight = 17, ExtensionOffset = 150,
        });
        var scrolled = new ScrollPosition(0, 3);
        Assert.Equal(-1, FileViewScroll.IndexAt(layout, scrolled, 10, 5, 50));
        Assert.Equal(-1, FileViewScroll.IndexAt(layout, scrolled, 10, 19, 50));
        Assert.Equal(3, FileViewScroll.IndexAt(layout, scrolled, 10, 20, 50));
        var (x, y, w, h) = FileViewScroll.VisibleBounds(layout, scrolled, 5);
        Assert.Equal(20 + 2 * 20, y);
        Assert.Equal(5, FileViewScroll.IndexAt(layout, scrolled, x + 1, y + 1, 50));
        Assert.Equal(5, FileViewScroll.IndexAt(layout, scrolled, x + w - 1, y + h - 1, 50));
    }

    [Theory]
    [InlineData(24, 200)]    // 帯に入った所（深さ 0）
    [InlineData(23, 200 - 170 / 48)]   // 深さ 1
    [InlineData(0, 200 - 170 * 24 / 48)]   // 深さ 24（帯の幅）は中間
    [InlineData(-24, 30)]    // 帯の幅の 2 倍（コントロールの外へ 24）
    [InlineData(-500, 30)]   // それより深くても下限
    [InlineData(300, 200)]   // 帯の外（向きが 0 の軸は見ない）
    public void 投げ縄の自動スクロールの間隔は帯からの深さで直線に縮む_上端(int y, int expected)
    {
        var direction = y < 24 ? (0, -1) : (0, 0);
        Assert.Equal(expected, FileViewScroll.LassoInterval(24, direction, 100, y, 500, 500));
    }

    [Fact]
    public void 投げ縄の自動スクロールの間隔は下端と右端でも同じ深さの数え方で_軸ごとに深いほうを取る()
    {
        // 下端: 帯は 476 から。深さ 0 → 200、48 → 30
        Assert.Equal(200, FileViewScroll.LassoInterval(24, (0, 1), 100, 476, 500, 500));
        Assert.Equal(30, FileViewScroll.LassoInterval(24, (0, 1), 100, 524, 500, 500));
        Assert.Equal(30, FileViewScroll.LassoInterval(24, (0, 1), 100, 900, 500, 500));
        // 右下の角: 縦は浅く（深さ 0）、横は深い（48）→ 深いほう
        Assert.Equal(30, FileViewScroll.LassoInterval(24, (1, 1), 524, 476, 500, 500));
        Assert.Equal(200, FileViewScroll.LassoInterval(24, (1, 1), 476, 476, 500, 500));
        // 向きが 0 の軸はどれだけ外へ出ていても見ない
        Assert.Equal(200, FileViewScroll.LassoInterval(24, (0, 1), 9999, 476, 500, 500));
    }

    [Fact]
    public void すべてのレイアウトが自動スクロールの帯の幅を答える()
    {
        Assert.Equal(20, Columns().AutoScrollBand);
        Assert.Equal(20, DetailsLayout.Compute(new DetailsLayoutInput
        {
            EntryCount = 5, RowHeight = 20, IconWidth = 16, ColumnPadding = 4, HeaderHeight = 20, StepWidth = 32,
            Columns = [new(null, 200, 60, null)], FitToWindow = false, ClientWidth = 500, ClientHeight = 200,
            VerticalBarWidth = 17, HorizontalBarHeight = 17, ExtensionOffset = 150,
        }).AutoScrollBand);
    }

    // ---- R-123: 再表示で、先頭に見えていた項目の段を保つ ----

    [Fact]
    public void 見えている範囲のいちばん小さい添字が先頭の項目()
    {
        var layout = Columns();
        Assert.Equal(0, FileViewScroll.FirstVisible(layout, default, 500, 500, 100));
        Assert.Equal(50, FileViewScroll.FirstVisible(layout, new ScrollPosition(2, 0), 500, 500, 100));   // 3 列目の先頭
        Assert.Equal(-1, FileViewScroll.FirstVisible(layout, default, 500, 500, 0));
    }

    [Fact]
    public void 前に項目が足されても_先頭に見えていた項目の列が左端に来る()
    {
        var layout = Columns();
        var position = new ScrollPosition(2, 0);          // 先頭は 50 番
        // 30 件が前に足され、50 番だった項目は 80 番（4 列目）になった
        var after = ColumnLayout.Compute(130, maxBaseWidth: 100, maxExtensionWidth: 30, lineHeight: 18, viewportHeight: 500,
            iconWidth: 16, gap: 4, rowPadding: 2, columnPadding: 4);
        var kept = FileViewScroll.KeepAnchor(layout, position, 50, after, 80, 130, 500, 500);
        Assert.Equal(new ScrollPosition(3, 0), kept);
    }

    [Fact]
    public void スクロールしていない軸は0のままにする()
    {
        var layout = Columns();
        var after = ColumnLayout.Compute(130, maxBaseWidth: 100, maxExtensionWidth: 30, lineHeight: 18, viewportHeight: 500,
            iconWidth: 16, gap: 4, rowPadding: 2, columnPadding: 4);
        // 先頭にいる（0 段）。先頭だった 0 番が 30 番（2 列目）になっても、0 段のまま
        Assert.Equal(default, FileViewScroll.KeepAnchor(layout, default, 0, after, 30, 130, 500, 500));
    }

    [Fact]
    public void 範囲の外になるときは範囲に収め_先頭の項目が無ければ座標を収めるだけ()
    {
        var layout = Columns();
        var small = ColumnLayout.Compute(10, maxBaseWidth: 100, maxExtensionWidth: 30, lineHeight: 18, viewportHeight: 500,
            iconWidth: 16, gap: 4, rowPadding: 2, columnPadding: 4);
        Assert.Equal(default, FileViewScroll.KeepAnchor(layout, new ScrollPosition(2, 0), 50, small, 5, 10, 500, 500));
        Assert.Equal(default, FileViewScroll.KeepAnchor(layout, new ScrollPosition(2, 0), -1, small, -1, 0, 500, 500));
    }

    [Fact]
    public void 縦横にスクロールするレイアウトでは_横の段はそのまま縦だけ合わせる()
    {
        var layout = new GridLayout();                     // この試験用の入れ子のレイアウト（行高 20・横の 1 段 100px）
        var position = new ScrollPosition(3, 10);          // 先頭は 10 行目
        // 同じレイアウトで、10 行目だった項目が 14 行目になった
        var kept = FileViewScroll.KeepAnchor(layout, position, 10, layout, 14, 100, 500, 200);
        Assert.Equal(new ScrollPosition(3, 14), kept);
    }
}
