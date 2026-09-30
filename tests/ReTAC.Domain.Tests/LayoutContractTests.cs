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
        { "小アイコン", GridLayout.Compute(new GridLayoutInput
            {
                EntryCount = 23, Arrangement = GridArrangement.IconLeft, IconSize = 16, LineHeight = 16, NameLines = 1,
                TextWidth = 60, PaddingX = 4, PaddingY = 2, Gap = 4, CheckBoxSize = 0,
                ClientWidth = 250, ClientHeight = 150, VerticalBarWidth = 17, EdgeBand = 24,
            }), 23 },
        { "中アイコン", GridLayout.Compute(new GridLayoutInput
            {
                EntryCount = 23, Arrangement = GridArrangement.IconTop, IconSize = 32, LineHeight = 14, NameLines = 2,
                TextWidth = 48, PaddingX = 6, PaddingY = 4, Gap = 4, CheckBoxSize = 12,
                ClientWidth = 250, ClientHeight = 150, VerticalBarWidth = 17, EdgeBand = 24,
            }), 23 },
        { "並べて表示", GridLayout.Compute(new GridLayoutInput
            {
                EntryCount = 23, Arrangement = GridArrangement.Tile, IconSize = 32, LineHeight = 14, NameLines = 1, InfoLines = 2,
                TextWidth = 100, PaddingX = 6, PaddingY = 4, Gap = 4, CheckBoxSize = 12,
                ClientWidth = 250, ClientHeight = 150, VerticalBarWidth = 17, EdgeBand = 24,
            }), 23 },
        { "コンテンツ", ContentLayout.Compute(new ContentLayoutInput
            {
                EntryCount = 23, IconSize = 32, LineHeight = 14, PaddingX = 6, PaddingY = 4, Gap = 1, CheckBoxSize = 12,
                RightWidths = [60, 70], MinLeftWidth = 40,
                ClientWidth = 250, ClientHeight = 150, VerticalBarWidth = 17, EdgeBand = 24,
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

    /// <summary>格子の狭いパネル。部品が項目の矩形の中に収まる境目（幅 0・1・アイコンより狭い・ちょうど・余裕あり）を通す。</summary>
    public static TheoryData<string, IFileViewLayout, int> NarrowGrids()
    {
        var data = new TheoryData<string, IFileViewLayout, int>();
        foreach (var width in new[] { 0, 1, 10, 40, 60, 100, 250 })
            foreach (var arrangement in new[] { GridArrangement.IconLeft, GridArrangement.IconTop, GridArrangement.Tile })
            {
                var left = arrangement == GridArrangement.IconLeft;
                var tile = arrangement == GridArrangement.Tile;
                data.Add($"{(left ? "小" : tile ? "並べて表示" : "中")} 幅{width}", GridLayout.Compute(new GridLayoutInput
                {
                    EntryCount = 23, Arrangement = arrangement,
                    IconSize = left ? 16 : 32, LineHeight = left ? 16 : 14, NameLines = left || tile ? 1 : 2, InfoLines = tile ? 2 : 0,
                    TextWidth = left ? 60 : tile ? 100 : 48, PaddingX = left ? 4 : 6, PaddingY = left ? 2 : 4, Gap = 4,
                    CheckBoxSize = left ? 0 : 12,   // 小アイコンにチェックボックスは付かない（既存の「小アイコン」と同じ）
                    ClientWidth = width, ClientHeight = 150, VerticalBarWidth = 17, EdgeBand = 24,
                }), 23);
            }
        return data;
    }

    [Theory]
    [MemberData(nameof(NarrowGrids))]
    public void 狭いパネルでも部品は項目の矩形の中にある(string name, IFileViewLayout layout, int count) =>
        部品は項目の矩形の中にある(name, layout, count);

    [Theory]
    [MemberData(nameof(NarrowGrids))]
    public void 狭いパネルでも項目の矩形の中心を当てるとその項目(string name, IFileViewLayout layout, int count) =>
        項目の矩形の中心を当てるとその項目(name, layout, count);

    [Theory]
    [MemberData(nameof(NarrowGrids))]
    public void 狭いパネルでも範囲の項目は矩形が交わる項目と一致する(string name, IFileViewLayout layout, int count) =>
        範囲の項目は矩形が交わる項目と一致する(name, layout, count);

    [Theory]
    [MemberData(nameof(Layouts))]
    public void 部品は項目の矩形の中にある(string name, IFileViewLayout layout, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var item = layout.ItemBounds(i);
            var parts = new List<(int X, int Y, int Width, int Height)> { layout.IconBounds(i), layout.NameBounds(i), layout.ExtensionBounds(i) };
            if (layout.CheckBoxBounds(i) is { } box) parts.Add(box);
            if (layout is GridLayout { Arrangement: GridArrangement.Tile } tile)
                for (var row = 0; row < tile.InfoLines; row++) parts.Add(tile.InfoBounds(i, row));
            if (layout is ContentLayout content)
            {
                parts.Add(content.LeftInfoBounds(i));
                for (var row = 0; row < content.RightCount; row++) parts.Add(content.RightInfoBounds(i, row)!.Value);
            }
            foreach (var part in parts)
                Assert.True(part.X >= item.X && part.Y >= item.Y && part.X + part.Width <= item.X + item.Width
                            && part.Y + part.Height <= item.Y + item.Height, $"{name} の {i}");
            // R-116: チェックボックスのあるレイアウトでは、アイコンを押してもマークは変わらない（カーソルの移動と D&D）
            var iconArea = layout.CheckBoxBounds(i) is null ? FileViewArea.MarkIcon : FileViewArea.Name;
            var icon = layout.IconBounds(i);
            Assert.Equal(iconArea, layout.HitTest(icon.X + icon.Width / 2, icon.Y + icon.Height / 2, count).Area);
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

    /// <summary>R-120: 投げ縄は項目の矩形と交われば対象（部品の無い余白だけでも）。</summary>
    [Theory]
    [MemberData(nameof(Layouts))]
    public void 余白だけに交わる投げ縄でも項目が対象になる(string name, IFileViewLayout layout, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var (x, y, w, h) = layout.ItemBounds(i);
            // 項目の右下の 1px（どの部品にも入らない余白）だけに交わる 1×1 の矩形
            Assert.True(layout.IndexesIn(x + w - 1, y + h - 1, 1, 1, count).Contains(i), $"{name} の {i}");
            // 項目の外に 1px ずらすと対象にならない（1px でも交われば対象、交わらなければ対象外）
            Assert.DoesNotContain(i, layout.IndexesIn(x + w, y + h, 1, 1, count));
        }
    }

    /// <summary>R-116: チェックボックスの当たり判定は部品の矩形の中だけ。</summary>
    [Theory]
    [MemberData(nameof(Layouts))]
    public void チェックボックスの当たり判定は部品の矩形だけ(string name, IFileViewLayout layout, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (layout.CheckBoxBounds(i) is not { } box) continue;
            Assert.True((i, FileViewArea.CheckBox) == layout.HitTest(box.X, box.Y, count), $"{name} の {i}");
            Assert.Equal((i, FileViewArea.CheckBox), layout.HitTest(box.X + box.Width - 1, box.Y + box.Height - 1, count));
            Assert.NotEqual(FileViewArea.CheckBox, layout.HitTest(box.X + box.Width, box.Y + box.Height / 2, count).Area);
            Assert.NotEqual(FileViewArea.CheckBox, layout.HitTest(box.X + box.Width / 2, box.Y + box.Height, count).Area);
        }
    }

    /// <summary>R-110-2 / R-116: 落とす先の枠とカーソルの枠は、見えている座標に移した ItemBounds と同じ矩形。</summary>
    [Theory]
    [MemberData(nameof(Layouts))]
    public void 枠はItemBoundsと一致する(string name, IFileViewLayout layout, int count)
    {
        var max = layout.MaxScrollPosition(count, 200, 90);
        foreach (var scroll in new[] { new ScrollPosition(0, 0), max })
            for (var i = 0; i < count; i++)
                Assert.True(FileViewScroll.ToVisible(layout, scroll, layout.ItemBounds(i)) == FileViewScroll.VisibleBounds(layout, scroll, i), $"{name} の {i}");
    }

    /// <summary>
    /// R-120 / V3: レイアウトごとの矢印キー。格子は行の端で隣の行へ。一覧は隣の列の同じ高さ（無ければ動かない）。
    /// 詳細の左右は横スクロール（カーソルは動かない）。Phase 17 で並べて表示（格子）とコンテンツ（↑↓ は 1 行、←→ は動かない）を足す。
    /// </summary>
    public static TheoryData<string, int, int, int, int> ArrowCases()
    {
        // 格子は 5 件ずつの行（GridFive）。23 件なので最終行は 20〜22 の 3 件（欠けた最終行）
        var data = new TheoryData<string, int, int, int, int>
        {
            // レイアウト, 始めの位置, dx, dy, 行き先
            { "格子", 4, 1, 0, 5 },     // 右端の → は次の行の先頭
            { "格子", 5, -1, 0, 4 },    // 左端の ← は前の行の末尾
            { "格子", 0, -1, 0, 0 },    // 先頭の ← は動かない
            { "格子", 22, 1, 0, 22 },   // 末尾の → は動かない
            { "格子", 2, 0, 1, 7 },     // ↓ は下の行の同じ列
            { "格子", 18, 0, 1, 22 },   // 下の行にその列が無ければ最後の項目
            { "格子", 21, 0, 1, 21 },   // 最終行の ↓ は動かない
            { "格子", 3, 0, -1, 3 },    // 先頭の行の ↑ は動かない
            { "格子", 12, 0, -1, 7 },   // ↑ は上の行の同じ列
            { "一覧", 0, 0, 1, 1 },
            { "一覧", 0, 1, 0, 5 },     // 1 列 5 行（ListFive）の隣の列
            { "一覧", 20, 1, 0, 20 },   // 隣の列が無ければ動かない
            { "コンテンツ", 3, 1, 0, 3 },    // ← → は何もしない（横スクロールも無い）
            { "コンテンツ", 3, -1, 0, 3 },
            { "コンテンツ", 3, 0, 1, 4 },
            { "コンテンツ", 22, 0, 1, 22 },
        };
        return data;
    }

    private static IFileViewLayout GridFive() => GridLayout.Compute(new GridLayoutInput
    {
        EntryCount = 23, Arrangement = GridArrangement.IconTop, IconSize = 32, LineHeight = 14, NameLines = 2,
        TextWidth = 36, PaddingX = 6, PaddingY = 4, Gap = 4, CheckBoxSize = 12,
        // (4 + 48) × 5 + 4 = 264。縦のバーを引いても 5 列に収まる幅
        ClientWidth = 290, ClientHeight = 150, VerticalBarWidth = 17, EdgeBand = 24,
    });

    private static IFileViewLayout ListFive() => ColumnLayout.Compute(23, 120, 30, 16, 5 * 18, 16, 4, 2, 4);

    private static IFileViewLayout ContentRows() => ContentLayout.Compute(new ContentLayoutInput
    {
        EntryCount = 23, IconSize = 32, LineHeight = 14, PaddingX = 6, PaddingY = 4, Gap = 1, CheckBoxSize = 12,
        RightWidths = [60, 70], MinLeftWidth = 40, ClientWidth = 250, ClientHeight = 150, VerticalBarWidth = 17, EdgeBand = 24,
    });

    [Theory]
    [MemberData(nameof(ArrowCases))]
    public void 矢印キーの行き先(string kind, int from, int dx, int dy, int to)
    {
        var layout = kind switch { "格子" => GridFive(), "コンテンツ" => ContentRows(), _ => ListFive() };
        if (kind == "格子") Assert.Equal(5, ((GridLayout)layout).Columns);
        Assert.Equal(to, layout.Arrow(from, dx, dy, 23));
    }

    [Fact]
    public void 詳細の左右はカーソルを動かさず横スクロール()
    {
        Assert.True(DetailsFor("Size", 96).ArrowsScrollHorizontally);
        Assert.False(GridFive().ArrowsScrollHorizontally);
        Assert.False(ListFive().ArrowsScrollHorizontally);
        Assert.False(ContentRows().ArrowsScrollHorizontally);
    }

    /// <summary>
    /// INV-LAYOUT-GEOMETRY-SINGLE-SOURCE: IFileViewLayout の実装はすべて、この契約テストのデータと不変条件の applies に載っている。
    /// specIds は矩形の契約の参照元を含む（R-121（並べて表示）・R-122（コンテンツ）を含む）。
    /// </summary>
    [Fact]
    public void すべてのレイアウトが契約テストと不変条件に載っている()
    {
        var implementations = typeof(IFileViewLayout).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IFileViewLayout).IsAssignableFrom(t)).ToList();
        var tested = Layouts().Select(row => row[1].GetType()).ToHashSet();
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(SchemaManifest.RepoRoot(), "schema", "invariants.json")));
        var invariant = doc.RootElement.GetProperty("invariants").EnumerateArray()
            .Single(e => e.GetProperty("id").GetString() == "INV-LAYOUT-GEOMETRY-SINGLE-SOURCE");
        var applies = invariant.GetProperty("applies").EnumerateArray().Select(a => a.GetString()).ToHashSet();
        foreach (var type in implementations)
        {
            Assert.True(tested.Contains(type), $"{type.Name} が LayoutContractTests.Layouts に無い");
            Assert.True(applies.Contains("type:" + type.FullName), $"{type.Name} が不変条件の applies に無い");
        }
        var specIds = invariant.GetProperty("specIds").EnumerateArray().Select(a => a.GetString()).ToHashSet();
        Assert.Superset(new HashSet<string?> { "R-114", "R-110-2", "R-116", "R-117", "R-119", "R-120", "R-121", "R-122" }, specIds);
    }
}
