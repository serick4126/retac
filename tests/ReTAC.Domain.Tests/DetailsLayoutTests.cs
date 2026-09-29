using ReTAC.Domain.Listing;

namespace ReTAC.Domain.Tests;

public class DetailsLayoutTests
{
    private static DetailsColumnInput Name(int auto, int? manual = null) => new(null, auto, 60, manual);
    private static DetailsColumnInput Col(DetailsColumn column, int auto, int min = 40, int? manual = null) => new(column, auto, min, manual);

    private static DetailsLayout Layout(int count, int clientWidth, int clientHeight, bool fit = false, params DetailsColumnInput[] columns) =>
        DetailsLayout.Compute(new DetailsLayoutInput
        {
            EntryCount = count, RowHeight = 20, IconWidth = 16, ColumnPadding = 4, HeaderHeight = 20, StepWidth = 32,
            Columns = columns.Length > 0 ? columns : [Name(200), Col(DetailsColumn.Size, 80), Col(DetailsColumn.Modified, 120)],
            FitToWindow = fit, ClientWidth = clientWidth, ClientHeight = clientHeight,
            VerticalBarWidth = 17, HorizontalBarHeight = 17, ExtensionOffset = 150,
        });

    // ---- 列幅（R-114 / V6）----

    [Fact]
    public void 手動の幅が自動より優先し最小幅より狭くはしない()
    {
        var widths = DetailsLayout.ResolveWidths([Name(200, manual: 300), Col(DetailsColumn.Size, 80, min: 50, manual: 10)], fit: false, available: 1000);
        Assert.Equal([300, 50], widths);
    }

    [Fact]
    public void 合わせる設定では名前の列から最小幅まで縮める()
    {
        // 合計 400。幅 300 に合わせるには 100 縮める。名前は 200→100（最小 60 より上）で足りる
        var widths = DetailsLayout.ResolveWidths([Name(200), Col(DetailsColumn.Size, 80), Col(DetailsColumn.Modified, 120)], fit: true, available: 300);
        Assert.Equal([100, 80, 120], widths);
    }

    [Fact]
    public void 名前を最小幅まで縮めても入らなければ右の列から縮める()
    {
        // 合計 400。幅 200 に合わせるには 200 縮める。名前 200→60（140）、右の Modified 120→60（残り 60）
        var widths = DetailsLayout.ResolveWidths(
            [Name(200), Col(DetailsColumn.Size, 80, min: 40), Col(DetailsColumn.Modified, 120, min: 40)], fit: true, available: 200);
        Assert.Equal([60, 80, 60], widths);
    }

    [Fact]
    public void 最小幅まで縮めても入らなければ最小幅で止める()
    {
        var widths = DetailsLayout.ResolveWidths(
            [Name(200), Col(DetailsColumn.Size, 80, min: 40), Col(DetailsColumn.Modified, 120, min: 40)], fit: true, available: 100);
        Assert.Equal([60, 40, 40], widths);
    }

    [Fact]
    public void 合わせない設定では縮めない() =>
        Assert.Equal([200, 80, 120], DetailsLayout.ResolveWidths([Name(200), Col(DetailsColumn.Size, 80), Col(DetailsColumn.Modified, 120)], fit: false, available: 100));

    [Fact]
    public void 合わせた後も手動の幅の入力は変わらない()
    {
        // 表示のときだけ縮め、保存した幅は変えない（入力は record struct なので呼び出し側の値は変わらない）
        DetailsColumnInput[] columns = [Name(200, manual: 300), Col(DetailsColumn.Size, 80)];
        DetailsLayout.ResolveWidths(columns, fit: true, available: 150);
        Assert.Equal(300, columns[0].ManualWidth);
    }

    // ---- スクロールバー（2 回で確定する）----

    [Fact]
    public void 行が収まり列も収まればスクロールバーは要らない()
    {
        var layout = Layout(count: 5, clientWidth: 500, clientHeight: 200);   // 見出し 20 + 5 行 100
        Assert.Equal((false, false), layout.ScrollBars);
        Assert.Equal(500, layout.ViewportWidth);
        Assert.Equal(180, layout.ViewportHeight);
    }

    [Fact]
    public void 縦スクロールバーの分だけ狭くなって横が要るようになる()
    {
        // 列の合計 400。幅 410 は縦バーが無ければ入るが、行が多くて縦バー（17）が要ると 393 で入らない
        var layout = Layout(count: 50, clientWidth: 410, clientHeight: 200);
        Assert.Equal((true, true), layout.ScrollBars);
        Assert.Equal(393, layout.ViewportWidth);
        Assert.Equal(200 - 20 - 17, layout.ViewportHeight);
    }

    [Fact]
    public void 横スクロールバーで高さが減って縦も要るようになる()
    {
        // 8 行 = 160。見出し 20 を引いて 180 に入るが、横バー 17 で 163 に減っても入る。9 行 = 180 は横バーで入らなくなる
        var layout = Layout(count: 9, clientWidth: 300, clientHeight: 200);
        Assert.Equal((true, true), layout.ScrollBars);
    }

    [Fact]
    public void 合わせる設定では縦バーを除いた幅に合わせる()
    {
        var layout = Layout(count: 50, clientWidth: 350, clientHeight: 200, fit: true);
        Assert.Equal(350 - 17, layout.TotalWidth);
        Assert.Equal((false, true), layout.ScrollBars);
    }

    // ---- 当たり判定（INV-DETAILS-ROW-HIT / Q31）----

    [Fact]
    public void 行の中の領域は行頭アイコン名前の列ほかの列最後の列より右()
    {
        var layout = Layout(count: 3, clientWidth: 800, clientHeight: 200);   // 名前 0..200、サイズ 200..280、更新日時 280..400
        Assert.Equal((1, FileViewArea.MarkIcon), layout.HitTest(0, 25, 3));
        Assert.Equal((1, FileViewArea.MarkIcon), layout.HitTest(19, 25, 3));
        Assert.Equal((1, FileViewArea.Name), layout.HitTest(20, 25, 3));
        Assert.Equal((1, FileViewArea.Name), layout.HitTest(199, 25, 3));     // 名前の列の中の余白（Q31）
        Assert.Equal((1, FileViewArea.Other), layout.HitTest(200, 25, 3));    // 境界の 1 ピクセルは右の領域
        Assert.Equal((1, FileViewArea.Other), layout.HitTest(399, 25, 3));
        Assert.Equal((-1, FileViewArea.None), layout.HitTest(400, 25, 3));    // 最後の列より右
        Assert.Equal((-1, FileViewArea.None), layout.HitTest(50, 60, 3));     // 最後の行より下
    }

    [Fact]
    public void 項目が無くても見出しは出て当たり判定は何も返さない()
    {
        var layout = Layout(count: 0, clientWidth: 500, clientHeight: 200);
        Assert.Equal(3, layout.Header.Count);
        Assert.Equal((-1, FileViewArea.None), layout.HitTest(10, 5, 0));
        Assert.Equal(0, layout.Arrow(0, 0, 1, 0));
        Assert.Empty(layout.IndexesIn(0, 0, 500, 200, 0));
        Assert.Equal(new ScrollPosition(0, 0), layout.MaxScrollPosition(0, layout.ViewportWidth, layout.ViewportHeight));
    }

    // ---- 見出し ----

    [Fact]
    public void 見出しのセルは列の並びと幅のとおり()
    {
        var layout = Layout(count: 3, clientWidth: 800, clientHeight: 200);
        Assert.Equal([new HeaderCell(null, 0, 200), new HeaderCell(DetailsColumn.Size, 200, 80), new HeaderCell(DetailsColumn.Modified, 280, 120)],
            layout.Header);
        Assert.Equal(0, DetailsLayout.HeaderBorderAt(layout.Header, 198, tolerance: 4));   // 名前の右の境界
        Assert.Equal(1, DetailsLayout.HeaderBorderAt(layout.Header, 283, tolerance: 4));
        Assert.Equal(-1, DetailsLayout.HeaderBorderAt(layout.Header, 100, tolerance: 4));
        Assert.Equal(2, DetailsLayout.HeaderCellAt(layout.Header, 300));
        Assert.Equal(-1, DetailsLayout.HeaderCellAt(layout.Header, 400));
    }

    // ---- キー・スクロール（Q28）----

    [Fact]
    public void 上下は1行ずつで左右は横スクロール()
    {
        var layout = Layout(count: 3, clientWidth: 800, clientHeight: 200);
        Assert.Equal(2, layout.Arrow(1, 0, 1, 3));
        Assert.Equal(2, layout.Arrow(2, 0, 1, 3));
        Assert.Equal(1, layout.Arrow(1, 1, 0, 3));   // 左右ではカーソルを動かさない
        Assert.True(layout.ArrowsScrollHorizontally);
    }

    [Fact]
    public void 横の最後の段は内容の右端が表示の右端に来る位置で止まる()
    {
        // 合計 400、表示 300（縦バーなし）、1 段 32。はみ出し 100 → 4 段目（128）ではなく 100 で止める
        var layout = Layout(count: 3, clientWidth: 300, clientHeight: 200);
        var max = layout.MaxScrollPosition(3, layout.ViewportWidth, layout.ViewportHeight);
        Assert.Equal(4, max.X);
        Assert.Equal(100, layout.ScrollOffset(max).X);
    }

    [Fact]
    public void 見せるときは縦だけ動かし横は保つ()
    {
        var layout = Layout(count: 50, clientWidth: 300, clientHeight: 200);   // 見える行は 163/20 = 8
        var scrolled = layout.Reveal(30, new ScrollPosition(2, 0), layout.ViewportWidth, layout.ViewportHeight);
        Assert.Equal(new ScrollPosition(2, 30 - 8 + 1), scrolled);
        Assert.Equal(scrolled, layout.Reveal(25, scrolled, layout.ViewportWidth, layout.ViewportHeight));   // 見えていれば動かさない
    }
}
