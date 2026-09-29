using ReTAC.Domain.Listing;

namespace ReTAC.Domain.Tests;

/// <summary>R-01-3・R-01-6・R-66-3 のレイアウト計算</summary>
public class ColumnLayoutTests
{
    [Fact]
    public void 負の矩形には項目を返さない()
    {
        var layout = ColumnLayout.Compute(23, 120, 30, 16, 100, 16, 4, 2, 4);
        Assert.Empty(layout.IndexesIn(-500, 0, 100, 100, 23));
        Assert.Empty(layout.IndexesIn(0, -100, 500, 50, 23));
    }

    // 行高 20 / 1 列 25 行 / アイコン 16 / gap 4 / 余白 4 / 基底名 100 / 拡張子 30
    private static ColumnLayout Sample(int entryCount) =>
        ColumnLayout.Compute(entryCount, maxBaseWidth: 100, maxExtensionWidth: 30,
            lineHeight: 18, viewportHeight: 500, iconWidth: 16, gap: 4, rowPadding: 2,
            columnPadding: 4);

    [Fact]
    public void 拡張子の開始位置と列幅が仕様書のとおりに決まる()
    {
        var layout = Sample(100);
        Assert.Equal(20, layout.RowHeight);                       // 18 + 2
        Assert.Equal(4, layout.ColumnPadding);
        // B-07: 列の先頭に余白が入るぶん、拡張子の位置も右へずれる
        Assert.Equal(4 + 16 + 4 + 100 + 4, layout.ExtensionOffset);
        // 末尾にも同じ余白。1 列目の末尾と 2 列目の先頭で余白が 2 つ挟まるのは許容（利用者判断）
        Assert.Equal(layout.ExtensionOffset + 30 + 4, layout.ColumnWidth);
        Assert.Equal(25, layout.RowsPerColumn);                   // 500 / 20
    }

    [Fact]
    public void 余白を0にすると先頭末尾の余白が消える()
    {
        // 余白は定数 1 個に集約されており、実機で撮り比べて微調整できる
        var layout = ColumnLayout.Compute(100, 100, 30, 18, 500, 16, 4, 2, columnPadding: 0);
        Assert.Equal(16 + 4 + 100 + 4, layout.ExtensionOffset);
        Assert.Equal(layout.ExtensionOffset + 30, layout.ColumnWidth);
    }

    [Fact]
    public void 列幅はウィンドウ幅から独立している()
    {
        // R-01-3 / N-04-3: Compute の引数にウィンドウ幅が存在しないこと自体が保証
        var narrow = ColumnLayout.Compute(100, 100, 30, 18, 500, 16, 4, 2, 4);
        var tall = ColumnLayout.Compute(100, 100, 30, 18, 900, 16, 4, 2, 4);
        Assert.Equal(narrow.ColumnWidth, tall.ColumnWidth);
    }

    [Fact]
    public void エントリは縦に流れて右の列へ折り返す()
    {
        // R-01-2
        var layout = Sample(60);
        Assert.Equal(0, layout.ColumnOf(0));
        Assert.Equal(0, layout.ColumnOf(24));
        Assert.Equal(1, layout.ColumnOf(25));   // 25 行目で右隣の列へ
        Assert.Equal(0, layout.RowOf(25));
        Assert.Equal(2, layout.ColumnOf(59));
        Assert.Equal(9, layout.RowOf(59));
        Assert.Equal(3, layout.ColumnCount);    // 60 件 / 25 行 = 3 列
    }

    [Fact]
    public void 座標からエントリを引ける()
    {
        var layout = Sample(60);
        Assert.Equal(0, layout.IndexAt(0, 0, 60));
        Assert.Equal(3, layout.IndexAt(10, 3 * layout.RowHeight, 60));
        // 2 列目の 4 行目
        Assert.Equal(25 + 3, layout.IndexAt(layout.ColumnWidth + 5, 3 * layout.RowHeight, 60));
    }

    [Fact]
    public void 範囲外の座標は該当なしを返す()
    {
        var layout = Sample(60);
        Assert.Equal(-1, layout.IndexAt(-1, 0, 60));
        Assert.Equal(-1, layout.IndexAt(0, -1, 60));
        Assert.Equal(-1, layout.IndexAt(0, layout.RowsPerColumn * layout.RowHeight, 60));
        // 3 列目は 10 件しかないので 11 行目以降は空
        Assert.Equal(-1, layout.IndexAt(2 * layout.ColumnWidth, 10 * layout.RowHeight, 60));
    }

    [Fact]
    public void 空のフォルダでも計算が壊れない()
    {
        var layout = ColumnLayout.Compute(0, 0, 0, 18, 0, 16, 4, 2, 4);
        Assert.Equal(0, layout.ColumnCount);
        Assert.Equal(0, layout.TotalWidth);
        Assert.True(layout.RowsPerColumn >= 1);
        Assert.Equal(-1, layout.IndexAt(0, 0, 0));
    }

    [Fact]
    public void 総幅は列数ぶんの横スクロール量になる()
    {
        // R-01-2: 列が増えるとリストは横方向にスクロールする
        var layout = Sample(60);
        Assert.Equal(layout.ColumnWidth * 3, layout.TotalWidth);
    }

    // pad 4 + icon 16 + gap 4 = 24 が文字の始まり
    private static ColumnLayout Capped(int maxBase, int maxExt, int? cap) => ColumnLayout.Compute(
        entryCount: 10, maxBaseWidth: maxBase, maxExtensionWidth: maxExt, lineHeight: 16, viewportHeight: 100,
        iconWidth: 16, gap: 4, rowPadding: 2, columnPadding: 4, maxTextWidth: cap);

    [Fact]
    public void 上限が無ければ今と同じ()
    {
        var layout = Capped(200, 30, null);
        Assert.Equal(24 + 200 + 4, layout.ExtensionOffset);
        Assert.Equal(24 + 200 + 4 + 30 + 4, layout.ColumnWidth);
    }

    [Fact]
    public void 中身が上限より短ければ上限は効かない()
    {
        var layout = Capped(50, 30, 400);
        Assert.Equal(24 + 50 + 4 + 30 + 4, layout.ColumnWidth);   // R-113: 余白を残さない
    }

    [Fact]
    public void 上限を超えたら本体を縮めて拡張子は揃えた位置に残す()
    {
        var layout = Capped(200, 30, 100);
        Assert.Equal(24 + 100 + 4, layout.ColumnWidth);            // 文字は 100 まで
        Assert.Equal(24 + (100 - 30 - 4) + 4, layout.ExtensionOffset);
    }

    [Fact]
    public void 拡張子だけで上限を超えても拡張子の位置は文字の始まりより前に行かない()
    {
        var layout = Capped(200, 150, 100);
        Assert.Equal(24 + 100 + 4, layout.ColumnWidth);
        Assert.True(layout.ExtensionOffset >= 24);
        Assert.True(layout.ExtensionOffset <= layout.ColumnWidth - 4);
    }

    [Fact]
    public void 当たり判定は行頭アイコンと名前を分ける()
    {
        var layout = Capped(200, 30, null);   // 列幅 262、行高 18
        Assert.Equal((0, FileViewArea.MarkIcon), layout.HitTest(0, 0, 10));      // 左の余白もアイコン（B-07）
        Assert.Equal((0, FileViewArea.MarkIcon), layout.HitTest(19, 0, 10));
        Assert.Equal((0, FileViewArea.Name), layout.HitTest(20, 0, 10));
        Assert.Equal((0, FileViewArea.Name), layout.HitTest(261, 0, 10));        // 名前の右の空き（Q26）
        Assert.Equal((5, FileViewArea.MarkIcon), layout.HitTest(262, 0, 10));    // 次の列の先頭
        Assert.Equal((-1, FileViewArea.None), layout.HitTest(5, 18 * 5 + 1, 5)); // 最後の項目より下
    }

    [Fact]
    public void 矢印は隣の列へ動き隣が無ければ動かない()
    {
        var layout = Capped(200, 30, null);   // 1 列 5 行
        Assert.Equal(1, layout.Arrow(0, 0, 1, 10));
        Assert.Equal(0, layout.Arrow(0, 0, -1, 10));
        Assert.Equal(5, layout.Arrow(0, 1, 0, 10));
        Assert.Equal(7, layout.Arrow(7, 1, 0, 10));   // 12 は無い（R-01-5）
        Assert.False(layout.ArrowsScrollHorizontally);
    }

    [Theory]
    [InlineData(NameWidthMode.ShowAll, 40, null)]
    [InlineData(NameWidthMode.Auto, 40, 500)]
    [InlineData(NameWidthMode.MaxChars, 10, 80)]
    public void 名前の文字の上限は方式で決まる(NameWidthMode mode, int chars, int? expected) =>
        Assert.Equal(expected, NameWidths.TextCap(new NameWidthSetting { Mode = mode, MaxChars = chars }, zeroWidth: 8, panelTextWidth: 500));
}
