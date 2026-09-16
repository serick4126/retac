using ReTAC.Domain.Selection;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

/// <summary>プロパティ表示（`R`）で開く対象の決め方（R-68 / R-56-2 / R-56-3）。外部ツールは LaunchPlannerTests</summary>
public class ExternalToolTests
{
    [Fact]
    public void 連続起動しない設定ではマークが何件あってもカーソル位置の1件()
    {
        var state = new ListState([TestEntries.Parent(), TestEntries.File("a.txt"), TestEntries.File("b.txt"), TestEntries.File("c.txt")]);
        state.ToggleMark(1);
        state.ToggleMark(2);
        state.MoveCursor(3);

        var targets = LaunchTargets.For(state, suppressMultiple: true);

        Assert.Equal(["c.txt"], TestEntries.Names(targets));
    }

    [Fact]
    public void 連続起動する設定なら実効対象すべてを渡す()
    {
        var state = new ListState([TestEntries.Parent(), TestEntries.File("a.txt"), TestEntries.File("b.txt")]);
        state.ToggleMark(1);
        state.ToggleMark(2);

        Assert.Equal(["a.txt", "b.txt"], TestEntries.Names(LaunchTargets.For(state, suppressMultiple: false)));
    }

    [Fact]
    public void カーソルが親フォルダ項目なら対象は空()
    {
        var state = new ListState([TestEntries.Parent(), TestEntries.File("a.txt")]);

        Assert.Empty(LaunchTargets.For(state, suppressMultiple: true));
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    public void 十件以上で事前確認が要る(int count, bool expected)
    {
        Assert.Equal(expected, LaunchTargets.NeedsConfirmation(count));
    }
}
