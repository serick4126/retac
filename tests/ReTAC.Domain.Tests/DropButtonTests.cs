using System.Windows.Forms;
using ReTAC.App;
using ReTAC.Shell;

namespace ReTAC.Domain.Tests;

/// <summary>R-111-2 / T6: 落とした後の始末は、処理が例外を投げても必ず行う</summary>
[Collection(nameof(DragButtonState))]
public class DropButtonTests
{
    private static DragEventArgs Args(int keyState) =>
        new(null, keyState, 0, 0, DragDropEffects.Copy | DragDropEffects.Move, DragDropEffects.Move);

    [Fact]
    public void 右ボタンのドロップは処理が例外を投げてもNoneを返し印を消す()
    {
        DragButtonState.SourceRight = false;
        DragButtonState.Reset();
        var over = Args(2);
        DropButton.Over(over);
        var drop = Args(0);
        drop.Effect = DragDropEffects.Move;
        Assert.Throws<InvalidOperationException>(() => DropButton.Drop(drop, () => throw new InvalidOperationException()));
        Assert.Equal(DragDropEffects.None, drop.Effect);
        Assert.False(DragButtonState.Right);
    }

    [Fact]
    public void 左ボタンのドロップは効果を変えず印は消す()
    {
        DragButtonState.SourceRight = false;
        DragButtonState.Reset();
        DropButton.Enter(Args(1));
        var drop = Args(0);
        drop.Effect = DragDropEffects.Move;
        DropButton.Drop(drop, () => { });
        Assert.Equal(DragDropEffects.Move, drop.Effect);
        Assert.False(DragButtonState.Right);
    }

    // R-111-2: FileListView / DriveBar も含め、DropButton.Enter / Over を呼ぶ受け口はすべて
    // この Guard を通す（INV-RIGHT-DROP-SAME-ROUTE）。個別に例外を捕まえる受け口を増やさない
    [Fact]
    public void Guardは例外を漏らさず右ボタンの印を消しEffectをNoneにする()
    {
        DragButtonState.SourceRight = false;
        DragButtonState.Reset();
        var e = Args(2);   // MK_RBUTTON

        DropButton.Guard(e, () => { DropButton.Enter(e); throw new InvalidOperationException(); }, () => { });

        Assert.False(DragButtonState.Right);
        Assert.Equal(DragDropEffects.None, e.Effect);
    }
}
