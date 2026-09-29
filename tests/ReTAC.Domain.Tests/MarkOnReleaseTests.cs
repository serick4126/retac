using ReTAC.Domain.Listing;
using ReTAC.Domain.Selection;

namespace ReTAC.Domain.Tests;

/// <summary>R-11-2 / Q7: 行頭アイコン・Shift+クリックのマークは、動かさずに離した時点で変える</summary>
public class MarkOnReleaseTests
{
    private static ListState State() => new([TestEntries.File("a"), TestEntries.File("b"), TestEntries.File("c"), TestEntries.File("d")]);

    private static ListState State(int n) => new(Enumerable.Range(0, n).Select(i => TestEntries.File("f" + i)));

    [Fact]
    public void 行頭アイコンを押して離すとマークが切り替わる()
    {
        var state = State();
        var click = new MarkOnRelease();
        click.Press(state, 2, area: FileViewArea.MarkIcon, shift: false);
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
        click.Press(state, 2, area: FileViewArea.MarkIcon, shift: false);
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
        click.Press(state, 0, area: FileViewArea.MarkIcon, shift: false);
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
        click.Press(state, 3, area: FileViewArea.MarkIcon, shift: true);
        Assert.Equal(1, state.CursorIndex);   // R-11-2: Shift の押下ではカーソルを動かさない（離した時点の起点）
        Assert.Empty(state.Marks);
        Assert.True(click.Release(state));
        Assert.Equal(new[] { 1, 2, 3 }, state.Marks.Order());
        Assert.Equal(3, state.CursorIndex);   // マークしてから、カーソルが押した項目へ動く
    }

    [Fact]
    public void Shiftで押したままドラッグを始めるとカーソルもマークも変わらない()
    {
        var state = State();
        state.MoveCursor(1);
        var click = new MarkOnRelease();
        click.Press(state, 3, area: FileViewArea.MarkIcon, shift: true);
        click.DragStarted();
        Assert.False(click.Release(state));
        Assert.Empty(state.Marks);
        Assert.Equal(1, state.CursorIndex);   // 起点のまま。ドラッグでは動かさない
    }

    [Fact]
    public void Shift無しの押下は直ちにカーソルを動かす()
    {
        var state = State();
        state.MoveCursor(1);
        var click = new MarkOnRelease();
        click.Press(state, 3, area: FileViewArea.Name, shift: false);
        Assert.Equal(3, state.CursorIndex);   // Shift が無ければ従来どおり押した時点で動く
    }

    [Fact]
    public void 名前の上をShiftなしで押して離してもマークは変えない()
    {
        var state = State();
        var click = new MarkOnRelease();
        click.Press(state, 2, area: FileViewArea.Name, shift: false);
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
        click.Press(state, 2, area: FileViewArea.MarkIcon, shift: false);
        click.Cancel();
        Assert.False(click.Release(state));
        Assert.Empty(state.Marks);
    }

    // ---- 名前以外（詳細表示の Q24）----

    [Fact]
    public void 名前以外は押しただけではカーソルを動かさず離したら動かす()
    {
        var state = State(10);
        var mark = new MarkOnRelease();
        mark.Press(state, 5, FileViewArea.Other, shift: false);
        Assert.Equal(0, state.CursorIndex);
        Assert.False(mark.Release(state));   // マークは変えない
        Assert.Equal(5, state.CursorIndex);
        Assert.Empty(state.Marks);
    }

    [Fact]
    public void 名前以外で動かしたらカーソルも範囲マークも捨てる()
    {
        foreach (var shift in new[] { false, true })
        {
            var state = State(10);
            state.MoveCursor(2);
            var mark = new MarkOnRelease();
            mark.Press(state, 5, FileViewArea.Other, shift);
            mark.Moved();
            Assert.False(mark.Release(state));
            Assert.Equal(2, state.CursorIndex);
            Assert.Empty(state.Marks);
        }
    }

    [Fact]
    public void 名前以外でもShiftなら押す前のカーソルを起点に範囲マーク()
    {
        var state = State(10);
        state.MoveCursor(2);
        var mark = new MarkOnRelease();
        mark.Press(state, 5, FileViewArea.Other, shift: true);
        Assert.Equal(2, state.CursorIndex);
        Assert.True(mark.Release(state));
        Assert.Equal([2, 3, 4, 5], state.Marks.Order());
        Assert.Equal(5, state.CursorIndex);
    }

    [Fact]
    public void 押している最中に一覧が入れ替わったら離しても何もしない()
    {
        var state = State(10);
        var mark = new MarkOnRelease();
        mark.Press(state, 5, FileViewArea.Other, shift: false);
        mark.Cancel();
        Assert.False(mark.Release(state));
        Assert.Equal(0, state.CursorIndex);
    }

    // ---- ダブルクリック（Q32）: 押す→離す→押す→（開く）→離す ----

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void 行頭アイコンのダブルクリックでマークは元のまま(bool initiallyMarked, bool shift)
    {
        var state = State(10);
        if (initiallyMarked) state.ToggleMark(5);
        var mark = new MarkOnRelease();
        mark.Press(state, 5, FileViewArea.MarkIcon, shift);
        mark.Release(state);
        mark.Press(state, 5, FileViewArea.MarkIcon, shift);
        mark.Release(state);
        Assert.Equal(initiallyMarked, state.Marks.Contains(5));
        Assert.Equal(5, state.CursorIndex);
    }

    [Fact]
    public void Shiftのダブルクリックでは範囲マークが残る()
    {
        var state = State(10);
        state.MoveCursor(2);
        var mark = new MarkOnRelease();
        mark.Press(state, 5, FileViewArea.MarkIcon, shift: true);
        mark.Release(state);
        mark.Press(state, 5, FileViewArea.MarkIcon, shift: true);
        mark.Release(state);
        Assert.Equal([2, 3, 4, 5], state.Marks.Order());
        Assert.Equal(5, state.CursorIndex);
    }

    [Fact]
    public void チェックボックスは離した時点でマークを切り替える()
    {
        var state = State(5);
        var m = new MarkOnRelease();
        m.Press(state, 2, FileViewArea.CheckBox, shift: false);
        Assert.Equal(2, state.CursorIndex);   // 押した時点でカーソルは移る
        Assert.Empty(state.Marks);
        Assert.True(m.Release(state));
        Assert.Contains(2, state.Marks);
    }

    [Fact]
    public void チェックボックスを押したまま動かしたらマークは変えない()
    {
        var state = State(5);
        state.ToggleMark(2);
        var m = new MarkOnRelease();
        m.Press(state, 2, FileViewArea.CheckBox, shift: false);
        m.DragStarted();
        Assert.False(m.Release(state));
        Assert.Contains(2, state.Marks);
    }

    [Fact]
    public void Shiftを押したチェックボックスは範囲マーク()
    {
        var state = State(5);
        var m = new MarkOnRelease();
        m.Press(state, 3, FileViewArea.CheckBox, shift: true);
        Assert.True(m.Release(state));
        Assert.Equal([0, 1, 2, 3], state.Marks.Order());
    }
}
