using ReTAC.Domain.Listing;

namespace ReTAC.Domain.Tests;

/// <summary>R-122 / INV-DETAILS-ROW-HIT / INV-LAYOUT-GEOMETRY-SINGLE-SOURCE: コンテンツの行の中の配置・狭い幅・当たり判定。</summary>
public class ContentLayoutTests
{
    private static ContentLayout Content(int count = 23, int clientWidth = 800, int iconSize = 48, int[]? right = null, int clientHeight = 300) =>
        ContentLayout.Compute(new ContentLayoutInput
        {
            EntryCount = count, IconSize = iconSize, LineHeight = 16, PaddingX = 6, PaddingY = 4, Gap = 1, CheckBoxSize = 16,
            RightWidths = right ?? [180, 200], MinLeftWidth = 70,
            ClientWidth = clientWidth, ClientHeight = clientHeight, VerticalBarWidth = 17, EdgeBand = 24,
        });

    private static bool Overlaps((int X, int Y, int Width, int Height) a, (int X, int Y, int Width, int Height) b) =>
        a.Width > 0 && b.Width > 0 && a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    [Theory]
    [InlineData(16, 16 * 2 + 8)]    // 文字 2 行のほうが高い
    [InlineData(48, 48 + 8)]
    [InlineData(256, 256 + 8)]
    public void 行の高さはアイコンと文字2行の高い方に上下の余白(int iconSize, int height)
    {
        var layout = Content(iconSize: iconSize);
        Assert.Equal(height, layout.RowHeight);
        Assert.Equal(height + 1, layout.Pitch);
    }

    [Fact]
    public void 広ければ右の欄に2つ出し_左の欄は残り()
    {
        var layout = Content(clientWidth: 800, count: 3);   // バーなし
        Assert.Equal(2, layout.RightCount);
        Assert.Equal(200, layout.RightWidth);
        Assert.Equal(800 - (6 * 2 + 48) - 6 - 200 - 6, layout.LeftWidth);
        var name = layout.NameBounds(0);
        var right = layout.RightInfoBounds(0, 0)!.Value;
        Assert.Equal(800 - 6 - 200, right.X);
        Assert.Equal(name.Y, right.Y);                                    // 1 行目どうし
        Assert.Equal(name.Y + 16, layout.RightInfoBounds(0, 1)!.Value.Y);  // 2 行目
        Assert.Equal((name.X, name.Y + 16, name.Width, 16), layout.LeftInfoBounds(0));
    }

    [Fact]
    public void 狭いと右の欄を下の段から省き_それでも足りなければ無くす()
    {
        // 左の欄の最小 70。アイコンまで 60、右の余白 6
        var one = Content(count: 3, clientWidth: 60 + 6 + 190 + 6 + 70, right: [180, 190]);   // 2 つなら幅 190 で左が 70 ちょうど
        Assert.Equal(2, one.RightCount);
        var drop = Content(count: 3, clientWidth: 60 + 6 + 190 + 6 + 69, right: [180, 190]);  // 1 px 足りない → 3 つ目を省くと 180
        Assert.Equal(1, drop.RightCount);
        Assert.Equal(180, drop.RightWidth);
        Assert.Null(drop.RightInfoBounds(0, 1));
        var none = Content(count: 3, clientWidth: 60 + 6 + 180 + 6 + 69, right: [180, 190]);  // 1 つでも足りない → 右の欄を無くす
        Assert.Equal(0, none.RightCount);
        Assert.Null(none.RightInfoBounds(0, 0));
        Assert.Equal(60 + 6 + 180 + 6 + 69 - 60 - 6, none.LeftWidth);
    }

    public static TheoryData<int, int> NarrowCases()
    {
        var data = new TheoryData<int, int>();
        foreach (var width in new[] { 0, 1, 10, 100, 200, 320, 500, 800 })
            foreach (var icon in new[] { 32, 256 })
                data.Add(width, icon);
        return data;
    }

    [Theory]
    [MemberData(nameof(NarrowCases))]
    public void どの幅でも矩形は重ならず_アイコンを縮めず_横にスクロールしない(int clientWidth, int iconSize)
    {
        var layout = Content(clientWidth: clientWidth, iconSize: iconSize, right: [300, 320]);
        Assert.False(layout.ScrollBars.Horizontal);
        Assert.True(layout.LeftWidth >= 0);
        for (var i = 0; i < 3; i++)
        {
            var icon = layout.IconBounds(i);
            Assert.Equal(iconSize, icon.Width);
            Assert.Equal(16, layout.CheckBoxBounds(i)!.Value.Width);
            var item = layout.ItemBounds(i);
            Assert.True(icon.X + icon.Width <= item.X + item.Width, "アイコンは項目の矩形の中");
            var rights = Enumerable.Range(0, 2).Select(r => layout.RightInfoBounds(i, r)).OfType<(int, int, int, int)>().ToList();
            foreach (var left in new[] { layout.NameBounds(i), layout.LeftInfoBounds(i) })
            {
                Assert.False(Overlaps(icon, left), $"幅 {clientWidth}: アイコンと左の欄");
                foreach (var right in rights) Assert.False(Overlaps(left, right), $"幅 {clientWidth}: 左の欄と右の欄");
            }
            foreach (var right in rights) Assert.False(Overlaps(icon, right), $"幅 {clientWidth}: アイコンと右の欄");
        }
    }

    [Fact]
    public void 当たり判定は名前の欄とアイコンが名前_それ以外が名前以外()
    {
        var layout = Content(count: 3);
        var icon = layout.IconBounds(1);
        var name = layout.NameBounds(1);
        var leftInfo = layout.LeftInfoBounds(1);
        var right = layout.RightInfoBounds(1, 0)!.Value;
        var box = layout.CheckBoxBounds(1)!.Value;
        var item = layout.ItemBounds(1);
        Assert.Equal((1, FileViewArea.CheckBox), layout.HitTest(box.X, box.Y, 3));
        Assert.Equal((1, FileViewArea.Name), layout.HitTest(icon.X + icon.Width - 1, icon.Y + icon.Height - 1, 3));
        // INV-DETAILS-ROW-HIT: 名前の欄は文字の右の空きも含む（詳細表示の名前の列と同じ）
        Assert.Equal((1, FileViewArea.Name), layout.HitTest(name.X + name.Width - 1, name.Y, 3));
        Assert.Equal((1, FileViewArea.Other), layout.HitTest(leftInfo.X + 1, leftInfo.Y + 1, 3));
        Assert.Equal((1, FileViewArea.Other), layout.HitTest(right.X + 1, right.Y + 1, 3));
        Assert.Equal((1, FileViewArea.Other), layout.HitTest(item.X + item.Width - 1, item.Y + item.Height - 1, 3));   // 行の右下の空き
        var gap = layout.SeparatorBounds(1)!.Value;
        Assert.Equal((-1, FileViewArea.None), layout.HitTest(10, gap.Y, 3));   // 行と行の間の隙間は項目の無い所
        Assert.Equal((-1, FileViewArea.None), layout.HitTest(10, layout.Pitch * 3 + 5, 3));   // 最後の行より下
    }

    [Fact]
    public void 区切り線は行と行の間の隙間で_行の矩形に重ならない()
    {
        var layout = Content(count: 3);
        var sep = layout.SeparatorBounds(0)!.Value;
        var row0 = layout.ItemBounds(0);
        var row1 = layout.ItemBounds(1);
        Assert.Equal(row0.Y + row0.Height, sep.Y);
        Assert.Equal(1, sep.Height);
        Assert.Equal(row1.Y, sep.Y + sep.Height);   // 次の行のすぐ上
        Assert.False(Overlaps(sep, row0));
        Assert.False(Overlaps(sep, row1));
    }

    [Fact]
    public void 最後の行の後には区切り線も隙間も無い()
    {
        var layout = Content(count: 3);
        Assert.NotNull(layout.SeparatorBounds(1));
        Assert.Null(layout.SeparatorBounds(2));   // 行と行の「間」だけ
    }

    [Fact]
    public void 隙間込みでちょうど収まる高さではバーを出さない()
    {
        // 行の高さ 56（48 + 8）、隙間 1。2 行 = 56 + 1 + 56 = 113（最後の行の後の隙間は数えない）
        var fits = Content(count: 2, clientHeight: 113);
        Assert.False(fits.ScrollBars.Vertical);
        Assert.Equal(new ScrollPosition(0, 0), fits.MaxScrollPosition(2, 800, 113));
        Assert.Equal(new ScrollPosition(0, 0), fits.Reveal(1, new ScrollPosition(0, 0), 800, 113));
        var over = Content(count: 2, clientHeight: 112);
        Assert.True(over.ScrollBars.Vertical);
        Assert.Equal(new ScrollPosition(0, 1), over.MaxScrollPosition(2, 800, 112));
    }

    [Theory]
    [InlineData(3, 1, 0, 3)]     // → は動かない
    [InlineData(3, -1, 0, 3)]    // ← は動かない
    [InlineData(3, 0, 1, 4)]     // ↓ は 1 行
    [InlineData(3, 0, -1, 2)]    // ↑ は 1 行
    [InlineData(22, 0, 1, 22)]   // 最後の行の ↓ は動かない
    [InlineData(0, 0, -1, 0)]
    public void 上下は1行ずつ_左右は動かない(int from, int dx, int dy, int to)
    {
        var layout = Content();
        Assert.Equal(to, layout.Arrow(from, dx, dy, 23));
        Assert.False(layout.ArrowsScrollHorizontally);
    }

    [Fact]
    public void 右の欄に出す情報が無ければ右の欄は無い()
    {
        var layout = Content(count: 3, right: []);
        Assert.Equal(0, layout.RightCount);
        Assert.Null(layout.RightInfoBounds(0, 0));
    }

    [Fact]
    public void 空ならバーを出さず項目も無い()
    {
        var layout = Content(count: 0);
        Assert.False(layout.ScrollBars.Vertical);
        Assert.Equal((-1, FileViewArea.None), layout.HitTest(10, 10, 0));
        Assert.Empty(layout.IndexesIn(0, 0, 800, 300, 0));
        Assert.Equal(new ScrollPosition(0, 0), layout.MaxScrollPosition(0, 800, 300));
    }

    [Fact]
    public void 高さを超えると縦のバーを出し_その幅を引いて配置する()
    {
        var layout = Content(count: 23, clientWidth: 800);
        Assert.True(layout.ScrollBars.Vertical);
        Assert.Equal(800 - 17, layout.RowWidth);
    }
}
