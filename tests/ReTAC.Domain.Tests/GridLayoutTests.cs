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

    private static GridLayout Tile(int count = 23, int clientWidth = 600, int infoLines = 2, int iconSize = 32, int textWidth = 160) =>
        GridLayout.Compute(new GridLayoutInput
        {
            EntryCount = count, Arrangement = GridArrangement.Tile, IconSize = iconSize, LineHeight = 16, NameLines = 1, InfoLines = infoLines,
            TextWidth = textWidth, PaddingX = 6, PaddingY = 4, Gap = 4, CheckBoxSize = 16,
            ClientWidth = clientWidth, ClientHeight = 300, VerticalBarWidth = 17, EdgeBand = 24,
        });

    [Fact]
    public void 並べて表示の項目の幅は欄の幅とアイコンで決まる()
    {
        var layout = Tile();
        Assert.Equal(6 * 3 + 32 + 160, layout.CellWidth);   // R-121: 名前の長さに依らない固定の幅
    }

    [Theory]
    [InlineData(0, 32 + 8)]        // 情報なし: アイコンの高さ
    [InlineData(1, 32 + 8)]        // 名前と 1 行 = 32
    [InlineData(2, 16 * 3 + 8)]    // 名前と 2 行 = 48 がアイコンより高い
    [InlineData(5, 16 * 3 + 8)]    // 3 行以上は 2 行に丸める
    public void 並べて表示の項目の高さは文字の行とアイコンの高い方(int infoLines, int height)
    {
        var layout = Tile(infoLines: infoLines);
        Assert.Equal(height, layout.CellHeight);
        Assert.Equal(Math.Min(2, infoLines), layout.InfoLines);
    }

    [Fact]
    public void 並べて表示の名前と情報の行は縦に続き_まとまりは縦に中央()
    {
        var layout = Tile(infoLines: 1, iconSize: 64);   // 高さ 64 + 8。文字は 32
        var (x, y, _, h) = layout.ItemBounds(0);
        var name = layout.NameBounds(0);
        Assert.Equal(y + (h - 32) / 2, name.Y);
        Assert.Equal(16, name.Height);
        Assert.Equal(x + 6 * 2 + 64, name.X);
        var info = layout.InfoBounds(0, 0);
        Assert.Equal((name.X, name.Y + 16, name.Width, 16), info);
    }

    [Fact]
    public void 並べて表示のアイコンは部品で行頭アイコンではない()
    {
        var layout = Tile();
        var icon = layout.IconBounds(0);
        // R-121: マークはチェックボックスで行う。アイコンは押すとカーソル、動かすと D&D（小アイコンの MarkIcon とは違う）
        Assert.Equal((0, FileViewArea.Name), layout.HitTest(icon.X + icon.Width - 2, icon.Y + icon.Height - 2, 23));
        var box = layout.CheckBoxBounds(0)!.Value;
        Assert.Equal((0, FileViewArea.CheckBox), layout.HitTest(box.X, box.Y, 23));
    }

    [Fact]
    public void 並べて表示の文字の矩形は左揃えで欄の幅で切る()
    {
        var layout = Tile();
        var name = layout.NameBounds(0);
        var lines = layout.NameTextBounds(0, [30, 50, 5000], 16);
        Assert.Equal([(name.X, name.Y, 30, 16), (name.X, name.Y + 16, 50, 16), (name.X, name.Y + 32, name.Width, 16)], lines);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(40)]
    [InlineData(80)]
    [InlineData(150)]
    public void 狭いパネルの並べて表示は横にスクロールせずアイコンを縮めず文字の欄を縮める(int clientWidth)
    {
        var layout = Tile(clientWidth: clientWidth, iconSize: 256);
        Assert.False(layout.ScrollBars.Horizontal);
        Assert.Equal(1, layout.Columns);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(256, layout.IconBounds(i).Width);
            Assert.Equal(16, layout.CheckBoxBounds(i)!.Value.Width);
            var icon = layout.IconBounds(i);
            var name = layout.NameBounds(i);
            Assert.True(name.Width >= 0);
            Assert.True(name.X >= icon.X + icon.Width, "文字の欄はアイコンに重ならない");
        }
    }

    [Fact]
    public void 並べて表示が空ならバーを出さず項目も無い()
    {
        var layout = Tile(count: 0);
        Assert.False(layout.ScrollBars.Vertical);
        Assert.Equal((-1, FileViewArea.None), layout.HitTest(10, 10, 0));
        Assert.Empty(layout.IndexesIn(0, 0, 600, 300, 0));
    }
}
