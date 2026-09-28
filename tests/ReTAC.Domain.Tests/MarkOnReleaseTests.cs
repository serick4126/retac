using ReTAC.Domain.Selection;

namespace ReTAC.Domain.Tests;

/// <summary>R-11-2 / Q7: 行頭アイコン・Shift+クリックのマークは、動かさずに離した時点で変える</summary>
public class MarkOnReleaseTests
{
    private static ListState State() => new([TestEntries.File("a"), TestEntries.File("b"), TestEntries.File("c"), TestEntries.File("d")]);

    [Fact]
    public void 行頭アイコンを押して離すとマークが切り替わる()
    {
        var state = State();
        var click = new MarkOnRelease();
        click.Press(state, 2, onIcon: true, shift: false);
        Assert.Equal(2, state.CursorIndex);      // 押した時点でカーソルだけ移る
        Assert.Empty(state.Marks);
        Assert.True(click.Release(state));
        Assert.Equal(new[] { 2 }, state.Marks);
    }

    [Fact]
    public void 押したままドラッグを始めたらマークは変えない()
    {
        var state = State();
        var click = new MarkOnRelease();
        click.Press(state, 2, onIcon: true, shift: false);
        click.DragStarted();
        Assert.False(click.Release(state));      // 落とした後にボタンを離した通知が来ても変えない
        Assert.Empty(state.Marks);
    }

    [Fact]
    public void ドラッグをEscで取り消してもマークは変えない()
    {
        var state = State();
        state.ToggleMark(0);
        var click = new MarkOnRelease();
        click.Press(state, 0, onIcon: true, shift: false);
        click.DragStarted();                      // Esc で取り消すと DoDragDrop が戻るだけ。取り消しで別の通知は来ない
        Assert.False(click.Release(state));
        Assert.Equal(new[] { 0 }, state.Marks);           // 付いていたマークも外れない
    }

    [Fact]
    public void Shiftの範囲マークも離した時点でアイコンの上でも効く()
    {
        var state = State();
        state.MoveCursor(1);
        var click = new MarkOnRelease();
        click.Press(state, 3, onIcon: true, shift: true);
        Assert.Empty(state.Marks);
        Assert.True(click.Release(state));
        Assert.Equal(new[] { 1, 2, 3 }, state.Marks.Order());
    }

    [Fact]
    public void 名前の上をShiftなしで押して離してもマークは変えない()
    {
        var state = State();
        var click = new MarkOnRelease();
        click.Press(state, 2, onIcon: false, shift: false);
        Assert.False(click.Release(state));
        Assert.Empty(state.Marks);
        Assert.Equal(2, state.CursorIndex);
    }

    [Fact]
    public void 押している間に一覧が入れ替わったらCancelでマークを変えない()
    {
        // 自動更新などでボタンを押したまま _state が差し替わる場合。押した時点の添字は
        // もう別の項目を指しているので、離した時点でそれをマークしてはいけない
        var state = State();
        var click = new MarkOnRelease();
        click.Press(state, 2, onIcon: true, shift: false);
        click.Cancel();
        Assert.False(click.Release(state));
        Assert.Empty(state.Marks);
    }
}
