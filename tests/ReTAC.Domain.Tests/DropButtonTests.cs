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
}
