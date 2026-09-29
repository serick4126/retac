using System.Drawing;
using ReTAC.App.Rendering;
using ReTAC.Domain.Entries;

namespace ReTAC.Domain.Tests;

/// <summary>R-116 / 仕様書の色と重ね順: 枠・チェックはその場所の地と違う色。下地は不透明で、下がサムネイルでも見える。</summary>
public class ItemFramesTests
{
    // RowColorsTests と同じ 4 つの配色（custom・light・dark・high-contrast）
    public static TheoryData<string> Palettes => RowColorsTests.Palettes;

    [Theory]
    [MemberData(nameof(Palettes))]
    public void 項目のどの状態でも枠とチェックは下地と違う色(string palette)
    {
        var theme = RowColorsTests.PaletteOf(palette);
        foreach (var (cursor, marked) in new[] { (false, false), (false, true), (true, false), (true, true) })
        {
            var (background, foreground) = RowColors.Of(theme, AttributeColor.Normal, cursor, marked);
            var (fill, stroke) = ItemFrames.CheckBoxColors(background, foreground);
            Assert.Equal(background.ToArgb(), fill.ToArgb());   // 下地は項目の地の色（マーク・カーソルの塗りを含む）
            Assert.NotEqual(fill.ToArgb(), stroke.ToArgb());
            Assert.Equal(255, fill.A);                           // 不透明
        }
    }

    [Theory]
    [MemberData(nameof(Palettes))]
    public void カーソルの枠はどの状態でも項目の地と違う色(string palette)
    {
        var theme = RowColorsTests.PaletteOf(palette);
        foreach (var marked in new[] { false, true })
        {
            var (background, foreground) = RowColors.Of(theme, AttributeColor.Normal, isCursor: true, isMarked: marked);
            Assert.NotEqual(background.ToArgb(), ItemFrames.CursorFrameColor(background, foreground).ToArgb());
        }
    }

    [Theory]
    [MemberData(nameof(Palettes))]
    public void 投げ縄の枠は背景と違う色で_塗りは半透明(string palette)
    {
        var theme = RowColorsTests.PaletteOf(palette);
        var (stroke, fill) = ItemFrames.LassoColors(theme);
        Assert.NotEqual(theme.Background.ToArgb(), stroke.ToArgb());
        Assert.InRange(fill.A, 1, 254);
        Assert.Equal(stroke.ToArgb() & 0xFFFFFF, fill.ToArgb() & 0xFFFFFF);
    }

    [Theory]
    [InlineData(255, 255, 255)]   // 白一色のサムネイル
    [InlineData(0, 0, 0)]         // 黒一色
    public void どんなサムネイルの上でもチェックボックスは下地の上に描く(int r, int g, int b)
    {
        using var bitmap = new Bitmap(40, 40);
        using (var gr = Graphics.FromImage(bitmap)) gr.Clear(Color.FromArgb(r, g, b));
        var background = Color.FromArgb(r, g, b);   // 項目の地がサムネイルと同じ色でも
        var foreground = background;                 // 文字色まで同じ色（独自の配色）でも
        using (var gr = Graphics.FromImage(bitmap))
            ItemFrames.DrawCheckBox(gr, new Rectangle(10, 10, 16, 16), background, foreground, isChecked: true, stroke: 1);
        // 枠の上の 1 点が、地と違う色になっている
        Assert.NotEqual(background.ToArgb(), bitmap.GetPixel(10, 18).ToArgb());
    }

    private static Bitmap Canvas() => new(24, 24, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

    [Fact]
    public void チェックボックスの線は箱の右と下の外へはみ出さない()
    {
        using var bitmap = Canvas();
        using var g = Graphics.FromImage(bitmap);
        ItemFrames.DrawCheckBox(g, new Rectangle(2, 2, 16, 16), Color.White, Color.Black, isChecked: true, stroke: 1);
        for (var i = 0; i < 24; i++)
        {
            Assert.Equal(0, bitmap.GetPixel(18, i).A);   // 箱は 2..17
            Assert.Equal(0, bitmap.GetPixel(i, 18).A);
        }
        Assert.NotEqual(0, bitmap.GetPixel(17, 10).A);   // 右端の内側の線は描かれる
    }

    [Fact]
    public void カーソルの枠は項目の右と下の外へはみ出さない()
    {
        using var bitmap = Canvas();
        using var g = Graphics.FromImage(bitmap);
        ItemFrames.DrawCursorFrame(g, new Rectangle(2, 2, 16, 16), Color.White, Color.Black, 1);
        Assert.Equal(0, bitmap.GetPixel(18, 10).A);
        Assert.Equal(0, bitmap.GetPixel(10, 18).A);
        Assert.NotEqual(0, bitmap.GetPixel(17, 10).A);
        Assert.NotEqual(0, bitmap.GetPixel(10, 17).A);
    }
}
