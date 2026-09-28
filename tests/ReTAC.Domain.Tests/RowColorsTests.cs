using System.Drawing;
using ReTAC.App.Rendering;
using ReTAC.Domain.Entries;

namespace ReTAC.Domain.Tests;

/// <summary>R-110-2: 落とす先の枠はその項目の文字の色で描く。どの配色・どの行の状態でも、地と違う色になる</summary>
public class RowColorsTests
{
    private static readonly OsTheme HighContrast = OsTheme.Reference(dark: false) with
    {
        HighContrast = true,
        Window = Color.Black, WindowText = Color.White, Highlight = Color.FromArgb(0x1A, 0xEB, 0xFF), HighlightText = Color.Black,
    };

    public static TheoryData<string> Palettes => ["custom", "light", "dark", "high-contrast"];

    private static Theme Palette(string name) => name switch
    {
        "custom" => Theme.Default,
        "light" => Theme.Resolve(Theme.Default, ColorMode.System, OsTheme.Reference(dark: false)),
        "dark" => Theme.Resolve(Theme.Default, ColorMode.System, OsTheme.Reference(dark: true)),
        _ => Theme.Resolve(Theme.Default, ColorMode.System, HighContrast),
    };

    [Theory]
    [MemberData(nameof(Palettes))]
    public void 文字の色はどの状態でも行の地と違う(string palette)
    {
        var theme = Palette(palette);
        foreach (var attribute in Enum.GetValues<AttributeColor>())
        foreach (var (cursor, marked) in new[] { (false, false), (false, true), (true, false), (true, true) })
        {
            var (background, foreground) = RowColors.Of(theme, attribute, cursor, marked);
            Assert.NotEqual(background.ToArgb(), foreground.ToArgb());
        }
    }

    [Theory]
    [MemberData(nameof(Palettes))]
    public void 枠の色はどの状態でも行の地と違う(string palette)
    {
        var theme = Palette(palette);
        foreach (var attribute in Enum.GetValues<AttributeColor>())
        foreach (var (cursor, marked) in new[] { (false, false), (false, true), (true, false), (true, true) })
        {
            var (background, foreground) = RowColors.Of(theme, attribute, cursor, marked);
            Assert.NotEqual(background.ToArgb(), RowColors.Frame(background, foreground).ToArgb());
        }
    }

    [Theory]
    [InlineData("#808080")]
    [InlineData("#000000")]
    [InlineData("#FFFFFF")]
    public void 独自の配色で文字と地を同じ色にしても枠は見える(string color)
    {
        // 独自の配色では、利用者が文字色と地の色を同じ値にできる。そのときも枠は地と違う色で描く
        var same = ColorTranslator.FromHtml(color);
        var theme = Theme.Default with
        {
            Background = same, Foreground = same, MarkBackground = same, MarkForeground = same,
            CursorBackground = same, CursorForeground = same,
            SystemColor = same, ReadOnlyColor = same, HiddenColor = same, CompressedColor = same, EncryptedColor = same,
        };
        foreach (var (cursor, marked) in new[] { (false, false), (false, true), (true, false), (true, true) })
        {
            var (background, foreground) = RowColors.Of(theme, AttributeColor.Normal, cursor, marked);
            Assert.NotEqual(background.ToArgb(), RowColors.Frame(background, foreground).ToArgb());
        }
    }

    [Fact]
    public void ハイコントラストのマークの行でも枠が消えない()
    {
        // R-108-3: マークの地は WindowText。通常の文字色（WindowText）で枠を描くと消える（P4 のレビュー）
        var (background, foreground) = RowColors.Of(Palette("high-contrast"), AttributeColor.Normal, isCursor: false, isMarked: true);
        Assert.Equal(Color.White.ToArgb(), background.ToArgb());
        Assert.Equal(Color.Black.ToArgb(), foreground.ToArgb());
    }
}
