using System.Drawing;
using ReTAC.App.Rendering;

namespace ReTAC.Domain.Tests;

/// <summary>R-114 / Q36: 詳細表示の見出しの色。明るい配色でも暗い配色でも、地に対して見えること。</summary>
public class RowColorsHeaderTests
{
    private static double Distance(Color a, Color b) =>
        Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

    [Theory]
    [InlineData(255, 255, 255, 0, 0, 0)]
    [InlineData(20, 20, 24, 240, 240, 240)]
    public void 地はテーマの背景で他の色はすべて地と異なり押した色は乗せた色より遠い(int br, int bg, int bb, int fr, int fg, int fb)
    {
        var theme = Theme.Default with { Background = Color.FromArgb(br, bg, bb), Foreground = Color.FromArgb(fr, fg, fb) };
        var (back, fore, line, hot, pressed) = RowColors.Header(theme);
        Assert.Equal(theme.Background, back);
        foreach (var color in new[] { fore, line, hot, pressed }) Assert.NotEqual(back.ToArgb(), color.ToArgb());
        Assert.True(Distance(back, pressed) > Distance(back, hot));
        var dark = back.GetBrightness() < 0.5f;
        Assert.Equal(dark, hot.GetBrightness() > back.GetBrightness());
        Assert.Equal(dark, pressed.GetBrightness() > back.GetBrightness());
        Assert.Equal(!dark, hot.GetBrightness() < back.GetBrightness());
    }

    [Fact]
    public void 前景と背景が同じ独自配色でも例外にならない()
    {
        var same = Color.FromArgb(40, 40, 40);
        var (back, _, _, hot, pressed) = RowColors.Header(Theme.Default with { Background = same, Foreground = same });
        Assert.Equal(same, back);
        Assert.NotEqual(back.ToArgb(), hot.ToArgb());
        Assert.NotEqual(back.ToArgb(), pressed.ToArgb());
    }
}
