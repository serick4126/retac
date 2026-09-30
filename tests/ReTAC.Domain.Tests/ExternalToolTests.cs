using ReTAC.App;
using ReTAC.Domain.Selection;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

/// <summary>プロパティ表示（`R`）で開く対象の決め方（R-68 / R-56-2 / R-56-3）。外部ツールは LaunchPlannerTests</summary>
public class ExternalToolTests
{
    [Fact]
    public void マークが複数あれば実効対象すべてを開く()
    {
        var state = new ListState([TestEntries.Parent(), TestEntries.File("a.txt"), TestEntries.File("b.txt"), TestEntries.File("c.txt")]);
        state.ToggleMark(1);
        state.ToggleMark(2);
        state.MoveCursor(3);

        Assert.Equal(["a.txt", "b.txt"], TestEntries.Names(LaunchTargets.For(state)));
    }

    [Fact]
    public void マークが無ければカーソルの1件()
    {
        var state = new ListState([TestEntries.Parent(), TestEntries.File("a.txt"), TestEntries.File("b.txt")]);
        state.MoveCursor(2);

        Assert.Equal(["b.txt"], TestEntries.Names(LaunchTargets.For(state)));
    }

    [Fact]
    public void カーソルが親フォルダ項目なら対象は空()
    {
        var state = new ListState([TestEntries.Parent(), TestEntries.File("a.txt")]);

        Assert.Empty(LaunchTargets.For(state));
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    public void 十件以上で事前確認が要る(int count, bool expected)
    {
        Assert.Equal(expected, LaunchTargets.NeedsConfirmation(count));
    }

    [Theory]
    [InlineData("\"C:\\tools\\my tool.exe\"", @"C:\tools\my tool.exe")]   // R-127: 「パスのコピー」の形
    [InlineData("  \"notepad.exe\"  ", "notepad.exe")]
    [InlineData(@"C:\tools\a.exe", @"C:\tools\a.exe")]
    [InlineData("  notepad.exe ", "notepad.exe")]
    [InlineData("", "")]
    public void 外部ツールの実行ファイルの欄は_囲みの引用符を外して保存する(string input, string expected) =>
        Assert.Equal(expected, ExternalToolPage.NormalizePath(input));
}
