using System.Windows.Forms;
using ReTAC.App;
using ReTAC.Domain.Navigation;
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

    // R-146-1: 書庫の中の項目でも、フォルダの中央ではホールドで開く。追加の位置（onto が null）では開かない
    [Theory]
    [InlineData(BookmarkKind.Folder, true, true)]
    [InlineData(BookmarkKind.Folder, false, false)]
    [InlineData(BookmarkKind.Group, false, true)]
    [InlineData(BookmarkKind.Group, true, true)]
    public void HoldOpens_ByKindAndPayload(BookmarkKind kind, bool transferable, bool expected) =>
        Assert.Equal(expected, BookmarkDropZone.HoldOpens(
            new Bookmark("x", kind, kind == BookmarkKind.Group ? "" : @"C:\x", kind == BookmarkKind.Group ? [] : null), transferable));

    [Fact]
    public void HoldOpens_NotOnInsertPosition()
    {
        Assert.False(BookmarkDropZone.HoldOpens(null, transferable: true));
        Assert.False(BookmarkDropZone.HoldOpens(null, transferable: false));
    }

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
