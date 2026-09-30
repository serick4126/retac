using ReTAC.Domain.Listing;
using ReTAC.Domain.Selection;

namespace ReTAC.Domain.Tests;

/// <summary>R-120: 投げ縄の入力。閾値を超えるまで始めない。追加か解除かは押した時点で決まる。取り消したらマークを変えない。</summary>
public class LassoGestureTests
{
    private static ListState State(int n) => new(Enumerable.Range(0, n).Select(i => TestEntries.File($"f{i}.txt")));

    private static GridLayout Grid() => GridLayout.Compute(new GridLayoutInput
    {
        EntryCount = 23, Arrangement = GridArrangement.IconTop, IconSize = 32, LineHeight = 14, NameLines = 2,
        TextWidth = 36, PaddingX = 6, PaddingY = 4, Gap = 4, CheckBoxSize = 12,
        ClientWidth = 290, ClientHeight = 150, VerticalBarWidth = 17, EdgeBand = 24,
    });

    [Fact]
    public void 閾値を超えるまでは始めない()
    {
        var gesture = new LassoGesture();
        gesture.Press(0, 0, 0, 0, startsLasso: true, ctrl: false);
        Assert.False(gesture.Move(3, 3, 3, 3, dragWidth: 4, dragHeight: 4));
        Assert.Null(gesture.Active);
        Assert.True(gesture.Move(4, 0, 4, 0, 4, 4));
        Assert.NotNull(gesture.Active);
    }

    [Fact]
    public void 項目の上で押したら始めない()
    {
        var gesture = new LassoGesture();
        gesture.Press(0, 0, 0, 0, startsLasso: false, ctrl: false);
        Assert.False(gesture.Move(50, 50, 50, 50, 4, 4));
        Assert.Null(gesture.Active);
    }

    [Fact]
    public void 離すと確定し_始めていなければ何もしない()
    {
        var state = State(23);
        var gesture = new LassoGesture();
        gesture.Press(0, 0, 0, 0, startsLasso: true, ctrl: false);
        Assert.False(gesture.Release(state, Grid()));   // 閾値の手前で離した
        Assert.Empty(state.Marks);
        gesture.Press(0, 0, 0, 0, startsLasso: true, ctrl: false);
        gesture.Move(60, 20, 60, 20, 4, 4);
        Assert.True(gesture.Release(state, Grid()));
        Assert.Equal([0, 1], state.Marks.Order());
        Assert.Null(gesture.Active);
    }

    [Fact]
    public void 取り消したら離してもマークを変えない()
    {
        var state = State(23);
        var gesture = new LassoGesture();
        gesture.Press(0, 0, 0, 0, startsLasso: true, ctrl: false);
        gesture.Move(60, 20, 60, 20, 4, 4);
        gesture.Cancel();
        Assert.False(gesture.Release(state, Grid()));
        Assert.Empty(state.Marks);
    }

    [Fact]
    public void 押した時点のCtrlで決まり_後から変わらない()
    {
        // Move と Release は修飾キーを受け取らない。押した時点の Ctrl だけが効く
        var state = State(23);
        state.ToggleMark(0);
        var gesture = new LassoGesture();
        gesture.Press(0, 0, 0, 0, startsLasso: true, ctrl: true);
        gesture.Move(60, 20, 60, 20, 4, 4);
        Assert.True(gesture.Active!.Remove);
        gesture.Release(state, Grid());
        Assert.Empty(state.Marks);
    }

    [Fact]
    public void 自動スクロールで中身がずれたら矩形を伸ばす()
    {
        var gesture = new LassoGesture();
        gesture.Press(5, 5, 5, 5, startsLasso: true, ctrl: false);
        gesture.Move(5, 140, 5, 140, 4, 4);
        gesture.Scrolled(5, 140 + 76);                   // 1 行ぶんスクロールした。マウスは同じ位置
        Assert.Equal(140 + 76 - 5 + 1, gesture.Active!.Rect.Height);
    }

    [Fact]
    public void 矩形を動かすと_始めた点も今の点も同じだけ動く()
    {
        var gesture = new LassoGesture();
        gesture.Press(10, 10, 10, 110, startsLasso: true, ctrl: false);
        Assert.True(gesture.Move(60, 60, 60, 160, 4, 4));
        Assert.Equal((10, 110, 51, 51), gesture.Active!.Rect);

        gesture.Shift(0, 40);   // 前に項目が入って、中身が 40px 下へずれた

        Assert.Equal((10, 150, 51, 51), gesture.Active!.Rect);
    }

    [Fact]
    public void 押しただけの保留も動かす()
    {
        var gesture = new LassoGesture();
        gesture.Press(10, 10, 10, 110, startsLasso: true, ctrl: false);
        gesture.Shift(0, 40);
        Assert.True(gesture.Move(60, 60, 60, 200, 4, 4));
        Assert.Equal((10, 150, 51, 51), gesture.Active!.Rect);
    }
}
