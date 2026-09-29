using ReTAC.Domain.Listing;

namespace ReTAC.Domain.Tests;

/// <summary>
/// INV-LAYOUT-GEOMETRY-SINGLE-SOURCE: どのレイアウトでも、当たり判定・項目の矩形・範囲の項目・部品の矩形が同じ答えになる。
/// 描画・当たり判定・ドロップの枠が別々に計算しないことの土台。
/// </summary>
public class LayoutContractTests
{
    public static TheoryData<string, IFileViewLayout, int> Layouts() => new()
    {
        { "一覧", ColumnLayout.Compute(23, 120, 30, 16, 100, 16, 4, 2, 4), 23 },
        { "詳細", DetailsLayout.Compute(new DetailsLayoutInput
            {
                EntryCount = 23, RowHeight = 20, IconWidth = 16, ColumnPadding = 4, HeaderHeight = 20, StepWidth = 32,
                Columns = [new(null, 200, 60, null), new(DetailsColumn.Size, 80, 40, null), new(DetailsColumn.Type, 120, 40, 90)],
                FitToWindow = false, ClientWidth = 250, ClientHeight = 150, VerticalBarWidth = 17, HorizontalBarHeight = 17,
                ExtensionOffset = 150,
            }), 23 },
    };

    [Theory]
    [MemberData(nameof(Layouts))]
    public void 項目の矩形の中心を当てるとその項目(string name, IFileViewLayout layout, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var (x, y, w, h) = layout.ItemBounds(i);
            Assert.True(layout.HitTest(x + w / 2, y + h / 2, count).Index == i, $"{name} の {i}");
        }
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void 部品は項目の矩形の中にある(string name, IFileViewLayout layout, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var item = layout.ItemBounds(i);
            foreach (var part in new[] { layout.IconBounds(i), layout.NameBounds(i), layout.ExtensionBounds(i) })
                Assert.True(part.X >= item.X && part.Y >= item.Y && part.X + part.Width <= item.X + item.Width
                            && part.Y + part.Height <= item.Y + item.Height, $"{name} の {i}");
            var icon = layout.IconBounds(i);
            Assert.Equal(FileViewArea.MarkIcon, layout.HitTest(icon.X + icon.Width / 2, icon.Y + icon.Height / 2, count).Area);
            var nameRect = layout.NameBounds(i);
            Assert.Equal(FileViewArea.Name, layout.HitTest(nameRect.X + 1, nameRect.Y + nameRect.Height / 2, count).Area);
        }
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void 範囲の項目は矩形が交わる項目と一致する(string name, IFileViewLayout layout, int count)
    {
        var (x, y, w, h) = (10, 5, 150, 40);
        var expected = Enumerable.Range(0, count).Where(i =>
        {
            var b = layout.ItemBounds(i);
            return b.X < x + w && x < b.X + b.Width && b.Y < y + h && y < b.Y + b.Height;
        }).ToList();
        Assert.True(expected.SequenceEqual(layout.IndexesIn(x, y, w, h, count)), name);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void 見せた項目は見える範囲に入る(string name, IFileViewLayout layout, int count)
    {
        const int vw = 200, vh = 90;
        for (var i = 0; i < count; i++)
        {
            var scroll = layout.Reveal(i, new ScrollPosition(0, 0), vw, vh);
            var (ox, oy) = layout.ScrollOffset(scroll);
            var (x, y, w, h) = layout.ItemBounds(i);
            Assert.True(y - oy >= 0 && y - oy + h <= vh, $"{name} の {i} が縦に見えない");
            // 詳細は縦だけ動かし横は保つ（行は幅が内容全体）ので、横のどこかで見えていればよい。一覧は列の左端が見えている
            if (layout.ArrowsScrollHorizontally)
                Assert.True(x - ox < vw && x - ox + w > 0, $"{name} の {i} が横に見えない");
            else
                Assert.True(x - ox >= 0 && x - ox < vw, $"{name} の {i} が横に見えない");
        }
    }

    /// <summary>詳細表示のレイアウト。列の組み合わせ（非表示を含む）と dpi（96・144）を変える。</summary>
    public static TheoryData<string, int> DetailsCases()
    {
        var data = new TheoryData<string, int>();
        foreach (var dpi in new[] { 96, 144 })
            foreach (var set in new[] { "", "Size", "Modified,Type", "Extension,Size,Modified,Created,Type,Attributes", "Attributes,Extension" })
                data.Add(set, dpi);
        return data;
    }

    private static DetailsLayout DetailsFor(string set, int dpi)
    {
        int S(int v) => v * dpi / 96;
        var columns = new List<DetailsColumnInput> { new(null, S(200), S(60), null) };
        foreach (var name in set.Split(',', StringSplitOptions.RemoveEmptyEntries))
            columns.Add(new(Enum.Parse<DetailsColumn>(name), S(90), S(40), null));
        return DetailsLayout.Compute(new DetailsLayoutInput
        {
            EntryCount = 40, RowHeight = S(20), IconWidth = S(16), ColumnPadding = S(4), HeaderHeight = S(24), StepWidth = S(32),
            Columns = columns, FitToWindow = false, ClientWidth = S(300), ClientHeight = S(200),
            VerticalBarWidth = S(17), HorizontalBarHeight = S(17), ExtensionOffset = S(150),
        });
    }

    [Theory]
    [MemberData(nameof(DetailsCases))]
    public void 詳細の描画のセル_見出し_境界_行の当たり判定は同じ矩形と境目を指す(string set, int dpi)
    {
        var layout = DetailsFor(set, dpi);
        var tolerance = 4 * dpi / 96;
        var max = layout.MaxScrollPosition(40, layout.ViewportWidth, layout.ViewportHeight);
        foreach (var scroll in new[] { new ScrollPosition(0, 0), new ScrollPosition(max.X, 0), new ScrollPosition(0, max.Y), max })
        {
            var (ox, oy) = layout.ScrollOffset(scroll);
            for (var cellIndex = 0; cellIndex < layout.Header.Count; cellIndex++)
            {
                var header = layout.Header[cellIndex];
                // 見出しのセル・境界: 内側の点はそのセル、右の境界はそのセルの境界（内側の点は境界ではない）
                Assert.Equal(cellIndex, layout.HeaderCellAt(header.X));
                Assert.Equal(cellIndex, layout.HeaderCellAt(header.X + header.Width - 1));
                Assert.Equal(cellIndex, layout.HeaderBorderAt(header.X + header.Width, tolerance));
                if (header.Width > tolerance * 3) Assert.NotEqual(cellIndex, layout.HeaderBorderAt(header.X + header.Width / 2, tolerance));

                if (header.Column is not { } column) continue;
                for (var row = 0; row < 40; row++)
                {
                    var cell = layout.CellBounds(row, column);
                    Assert.NotNull(cell);
                    var (x, y, w, h) = cell.Value;
                    // セルは見出しのセルの内側で、行の帯に載る
                    Assert.True(x >= header.X && x + w <= header.X + header.Width);
                    Assert.Equal((row * layout.RowHeight, layout.RowHeight), (y, h));
                    if (w == 0) continue;
                    // 見えている座標のセルの中心を、行の当たり判定に当てる（縦横のずれと見出しの分を通す）
                    var (vx, vy, vw, vh) = FileViewScroll.ToVisible(layout, scroll, cell.Value);
                    Assert.Equal((x - ox, y - oy + layout.HeaderHeight), (vx, vy));
                    var (cx, cy) = (vx + vw / 2, vy + vh / 2);
                    if (cy < layout.HeaderHeight || cx < 0) continue;
                    Assert.Equal(row, FileViewScroll.IndexAt(layout, scroll, cx, cy, 40));
                    Assert.Equal((row, FileViewArea.Other), layout.HitTest(cx + ox, cy - layout.HeaderHeight + oy, 40));
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(DetailsCases))]
    public void 詳細では列が無いか名前の列ならセルは無い(string set, int dpi)
    {
        var layout = DetailsFor(set, dpi);
        foreach (var column in Enum.GetValues<DetailsColumn>())
            Assert.Equal(layout.Header.Any(h => h.Column == column), layout.CellBounds(0, column) is not null);
    }

    [Fact]
    public void 見出しの無いレイアウトは列のセルも境界も無い()
    {
        IFileViewLayout layout = ColumnLayout.Compute(23, 120, 30, 16, 100, 16, 4, 2, 4);
        Assert.Empty(layout.Header);
        Assert.All(Enum.GetValues<DetailsColumn>(), c => Assert.Null(layout.CellBounds(0, c)));
        Assert.Equal(-1, layout.HeaderBorderAt(120, 4));
        Assert.Equal(-1, layout.HeaderCellAt(10));
    }
}
