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
}