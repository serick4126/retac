using ReTAC.Domain.Listing;

namespace ReTAC.Domain.Tests;

/// <summary>R-119 / R-120: 格子の配置の境界（間隔・余白・チェックボックスの大きさはコードに固定）。</summary>
public class GridLayoutTests
{
    private static GridLayout Grid(int count, int clientWidth, int clientHeight = 150, int textWidth = 36,
        GridArrangement arrangement = GridArrangement.IconTop) => GridLayout.Compute(new GridLayoutInput
    {
        EntryCount = count, Arrangement = arrangement, IconSize = 32, LineHeight = 14, NameLines = 2,
        TextWidth = textWidth, PaddingX = 6, PaddingY = 4, Gap = 4,
        CheckBoxSize = arrangement == GridArrangement.IconTop ? 12 : 0,
        ClientWidth = clientWidth, ClientHeight = clientHeight, VerticalBarWidth = 17, EdgeBand = 24,
    });

    [Fact]
    public void 項目が無くても割り算で落ちない()
    {
        var grid = Grid(0, 0, 0);
        Assert.Equal(0, grid.Rows);
        Assert.Equal((-1, FileViewArea.None), grid.HitTest(5, 5, 0));
        Assert.Empty(grid.IndexesIn(0, 0, 100, 100, 0));
        Assert.Equal(new ScrollPosition(0, 0), grid.MaxScrollPosition(0, 0, 0));
        Assert.False(grid.ScrollBars.Vertical);   // 高さが外周の余白より小さくてもバーは出さない
        Assert.False(Grid(0, 100, 2).ScrollBars.Vertical);
    }

    [Fact]
    public void 狭い幅でも1列は並ぶ()
    {
        var grid = Grid(3, 10);
        Assert.Equal(1, grid.Columns);
        Assert.Equal(3, grid.Rows);
    }

    [Theory]
    [InlineData(GridArrangement.IconTop)]
    [InlineData(GridArrangement.IconLeft)]
    public void 部品より狭いパネルでは項目がはみ出すが横にはスクロールしない(GridArrangement arrangement)
    {
        var grid = Grid(3, 10, arrangement: arrangement);
        Assert.True(grid.CellWidth > 10);   // 部品を縮めずにはみ出す（R-119）
        Assert.False(grid.ScrollBars.Horizontal);
        Assert.Equal(0, grid.MaxScrollPosition(3, 10, 150).X);
        Assert.Equal(0, grid.ScrollOffset(new ScrollPosition(5, 0)).X);
        Assert.Equal(0, grid.AutoScrollDirection(9, 50, 10, 150).X);
    }

    [Fact]
    public void パネルより広い項目はパネルの幅まで縮めて横にはみ出さない()
    {
        var grid = Grid(3, 120, textWidth: 500, arrangement: GridArrangement.IconLeft);
        var (x, _, w, _) = grid.ItemBounds(0);
        Assert.True(x + w <= 120 - (grid.VerticalBar ? 17 : 0));
        Assert.False(grid.ScrollBars.Horizontal);
    }

    [Fact]
    public void 縦に入りきらなければ縦のバーを出し_その幅を引いて列を数え直す()
    {
        // バーなしなら 5 列 (264 <= 270)、バーを引くと 253 なので 4 列
        var grid = Grid(100, 270);
        Assert.True(grid.VerticalBar);
        Assert.Equal(4, grid.Columns);
        var single = Grid(4, 270);
        Assert.False(single.VerticalBar);
        Assert.Equal(5, single.Columns);
    }

    [Fact]
    public void 項目の間は項目の無い所()
    {
        var grid = Grid(10, 290);
        var (x, y, w, _) = grid.ItemBounds(0);
        Assert.Equal((-1, FileViewArea.None), grid.HitTest(x + w, y, 10));    // 右の余白
        Assert.Equal((-1, FileViewArea.None), grid.HitTest(1, 1, 10));        // 外周の余白
    }

    [Fact]
    public void 小アイコンは行頭アイコンでマークし_チェックボックスは無い()
    {
        var grid = Grid(10, 290, arrangement: GridArrangement.IconLeft);
        Assert.Null(grid.CheckBoxBounds(0));
        var (x, y, _, h) = grid.ItemBounds(0);
        Assert.Equal(FileViewArea.MarkIcon, grid.HitTest(x, y + h / 2, 10).Area);   // 左の余白もアイコン（B-07）
    }

    [Fact]
    public void 最後の行が全部見える位置がいちばん後ろ()
    {
        var grid = Grid(100, 290, clientHeight: 150);
        var max = grid.MaxScrollPosition(100, 290 - 17, 150);
        var (_, oy) = grid.ScrollOffset(max);
        var last = grid.ItemBounds(99);
        Assert.True(last.Y + last.Height - oy <= 150);
        Assert.True(grid.ItemBounds(99).Y - oy >= 0);
    }

    [Fact]
    public void 自動スクロールは上下の端だけ()
    {
        var grid = Grid(100, 290);
        Assert.Equal((0, -1), grid.AutoScrollDirection(100, 5, 273, 150));
        Assert.Equal((0, 1), grid.AutoScrollDirection(100, 140, 273, 150));
        Assert.Equal((0, 0), grid.AutoScrollDirection(2, 75, 273, 150));   // 左右の端では横に動かない
    }

    [Fact]
    public void ホイールは1ノッチで1行だけ進む()
    {
        var grid = Grid(200, 600, 400);
        Assert.Equal(1, grid.WheelSteps(3, 600, 400));
        Assert.Equal(1, grid.WheelSteps(1, 600, 20));
    }

    [Fact]
    public void 名前の文字の矩形は各行を名前の領域の中で中央に置き_領域より広い行は切る()
    {
        var grid = Grid(2, 400, textWidth: 60);
        var (nx, ny, nw, _) = grid.NameBounds(1);
        var lines = grid.NameTextBounds(1, [20, 500], 14);
        Assert.Equal((nx + (nw - 20) / 2, ny, 20, 14), lines[0]);
        Assert.Equal((nx, ny + 14, nw, 14), lines[1]);   // 広すぎる行は領域の幅
        Assert.Empty(Grid(2, 400, arrangement: GridArrangement.IconLeft).NameTextBounds(1, [20], 14));
    }
}
