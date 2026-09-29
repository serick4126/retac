using ReTAC.Domain.Listing;
using ReTAC.Domain.Selection;

namespace ReTAC.Domain.Tests;

/// <summary>R-120 / INV-MARKS-EXPLICIT-ONLY: 投げ縄は離した時点でまとめて確定。追加か解除かは始めた時点の Ctrl で決める。</summary>
public class LassoTests
{
    private static ListState State(int n) => new(Enumerable.Range(0, n).Select(i => TestEntries.File($"f{i}.txt")));

    // 5 列の格子。項目は 48×62、間は 4
    private static GridLayout Grid() => GridLayout.Compute(new GridLayoutInput
    {
        EntryCount = 23, Arrangement = GridArrangement.IconTop, IconSize = 32, LineHeight = 14, NameLines = 2,
        TextWidth = 36, PaddingX = 6, PaddingY = 4, Gap = 4, CheckBoxSize = 12,
        ClientWidth = 290, ClientHeight = 150, VerticalBarWidth = 17, EdgeBand = 24,
    });

    [Fact]
    public void 動かしている間はマークを変えず_離した時点で追加する()
    {
        var state = State(23);
        var grid = Grid();
        var lasso = new Lasso(0, 0, remove: false);
        lasso.Move(60, 20);
        Assert.Equal([0, 1], lasso.Covered(grid, 23));
        Assert.Empty(state.Marks);
        Assert.True(lasso.Commit(state, grid));
        Assert.Equal([0, 1], state.Marks.Order());
    }

    [Fact]
    public void 追加はマーク済みを外さない()
    {
        var state = State(23);
        state.ToggleMark(0);
        var lasso = new Lasso(0, 0, remove: false);
        lasso.Move(60, 20);
        lasso.Commit(state, Grid());
        Assert.Equal([0, 1], state.Marks.Order());
    }

    [Fact]
    public void Ctrlで始めたら囲んだ項目のマークを外す()
    {
        var state = State(23);
        state.ToggleMark(0);
        state.ToggleMark(4);
        var lasso = new Lasso(0, 0, remove: true);
        lasso.Move(60, 20);
        lasso.Commit(state, Grid());
        Assert.Equal([4], state.Marks.Order());
    }

    [Fact]
    public void 境界の1pxで交われば対象()
    {
        var grid = Grid();
        var (x, y, w, h) = grid.ItemBounds(1);
        var touching = new Lasso(x + w - 1, y + h - 1, remove: false);
        touching.Move(x + w + 3, y + h + 3);   // 項目 1 の右下の 1px だけに交わる
        Assert.Contains(1, touching.Covered(grid, 23));
        var apart = new Lasso(x + w, y + h, remove: false);
        apart.Move(x + w + 3, y + h + 3);      // 余白の中だけ
        Assert.DoesNotContain(1, apart.Covered(grid, 23));
    }

    [Fact]
    public void 逆向きにドラッグしても同じ矩形()
    {
        var lasso = new Lasso(100, 80, remove: false);
        lasso.Move(10, 20);
        Assert.Equal((10, 20, 91, 61), lasso.Rect);
    }

    [Fact]
    public void スクロールで見えなくなった項目も矩形と交われば含む()
    {
        // 矩形は中身の座標で持つので、始めた点が画面の外へ出ても（オフセットが変わっても）含まれる
        var grid = Grid();
        var lasso = new Lasso(5, 5, remove: false);
        lasso.Move(5, 5 + grid.PitchY * 3);   // 自動スクロールで 3 行下まで来た
        Assert.Contains(0, lasso.Covered(grid, 23));
        Assert.Contains(15, lasso.Covered(grid, 23));
    }

    [Fact]
    public void 親フォルダの行はマークしない()
    {
        var entries = new[] { TestEntries.Parent() }.Concat(Enumerable.Range(0, 4).Select(i => TestEntries.File($"f{i}.txt")));
        var state = new ListState(entries);
        var lasso = new Lasso(0, 0, remove: false);
        lasso.Move(60, 20);
        lasso.Commit(state, Grid());
        Assert.Equal([1], state.Marks.Order());
    }
}
