using System.Windows.Forms;
using ReTAC.App;
using ReTAC.Shell;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-111-2: 受け口の例外は必ず捕まえて枠・線・ドラッグ画像を後始末するが、右ボタンの印（DragButtonState）も
/// 一緒に消さないと、直後の左ドロップが右ドロップ用のメニュー扱いになってしまう。
/// AddressBar.GuardDrop は private だが中身は BookmarkDropZone.Guarded なので、ここで一緒に確かめる。
/// </summary>
[Collection(nameof(DragButtonState))]
public class BookmarkDropZoneTests
{
    private static DragEventArgs Args(int keyState) =>
        new(null, keyState, 0, 0, DragDropEffects.Copy | DragDropEffects.Move, DragDropEffects.Move);

    [Fact]
    public void 例外が起きても右ボタンの印を消す()
    {
        DragButtonState.SourceRight = false;
        DragButtonState.Reset();
        DragButtonState.Enter(2);   // MK_RBUTTON: 右ボタンのドラッグ中という印を立てる
        var e = Args(0);
        BookmarkDropZone.Guarded(e, () => throw new InvalidOperationException(), () => { });
        Assert.Equal(DragDropEffects.None, e.Effect);
        Assert.False(DragButtonState.Right);   // 消えていないと、続く左ドロップが右ドロップのメニュー扱いになる
    }
}
