using System.Windows.Forms;
using ReTAC.App;
using ReTAC.Domain.FileOps;
using ReTAC.Domain.Navigation;

namespace ReTAC.Domain.Tests;

/// <summary>R-140: ドロップ時の動作の指定が、判定（DropFeedback）と右ボタンの読み替え（MainForm）で正しく扱われる。</summary>
public class DropFeedbackTests
{
    private const int Ctrl = 8, Shift = 4;
    private const DragDropEffects Both = DragDropEffects.Copy | DragDropEffects.Move;
    private static readonly string[] Files = [@"C:\a\x.txt"];

    [Fact]
    public void Feedback_UsesFixedMode()
    {
        // 同じドライブは標準なら移動。常にコピーの指定ならコピー
        Assert.Equal(DropAction.Copy, DropFeedback.LeftDecision(Files, @"C:\b", 0, Both, BookmarkDropMode.Copy));
        Assert.Equal(DropAction.Move, DropFeedback.LeftDecision(Files, @"D:\b", 0, Both, BookmarkDropMode.Move));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(Ctrl, 0)]
    [InlineData(Shift, 0)]
    public void Feedback_StandardUnchanged(int keyState, int _)
    {
        foreach (var dest in new[] { @"C:\b", @"D:\b" })
            Assert.Equal(
                DropRules.Allow(DropRules.Decide(Files[0], dest, (keyState & Ctrl) != 0, (keyState & Shift) != 0), true, true),
                DropFeedback.LeftDecision(Files, dest, keyState, Both, BookmarkDropMode.Standard));
    }

    [Theory]
    [InlineData(BookmarkDropMode.Standard)]
    [InlineData(BookmarkDropMode.Copy)]
    [InlineData(BookmarkDropMode.Move)]
    public void Apply_RightDragIgnoresMode(BookmarkDropMode mode)
    {
        // R-111-2 / R-140: 右ボタンは常に標準の判定。カーソルとメニューの既定がずれない
        foreach (var dest in new[] { @"C:\b", @"D:\b" })
            Assert.Equal(Run(dest, BookmarkDropMode.Standard, right: true), Run(dest, mode, right: true));
        // 左ボタンは指定に従う（同じドライブでも常にコピー）
        Assert.Equal((DragDropEffects.Copy, DropImageType.Copy), Run(@"C:\b", BookmarkDropMode.Copy, right: false));
    }

    private static (DragDropEffects, DropImageType) Run(string dest, BookmarkDropMode mode, bool right)
    {
        var data = new DataObject(DataFormats.FileDrop, Files);
        var e = new DragEventArgs(data, 0, 0, 0, Both, DragDropEffects.None);
        DropFeedback.Apply(e, dest, "x", mode, right);
        return (e.Effect, e.DropImageType);
    }

    [Theory]
    [InlineData(DropChoice.Copy)]
    [InlineData(DropChoice.Move)]
    public void AfterMenu_ResetsMode(DropChoice choice)
    {
        // 右ボタンでは、選んだ操作に従う（指定に上書きされない。R-111-2 / R-140）
        var (ctrl, shift, mode) = MainForm.AfterMenu(choice);
        Assert.Equal(DropRouting.ModifiersFor(choice), (ctrl, shift));
        Assert.Equal(BookmarkDropMode.Standard, mode);
    }

    // R-146-1 / S1: 書庫の中の項目（FileDrop が無く Shell IDList がある）は、修飾キー・指定・ボタンにかかわらずコピーか不可。移動を返さない
    [Theory]
    [InlineData(0, BookmarkDropMode.Standard, false)]
    [InlineData(Shift, BookmarkDropMode.Standard, false)]
    [InlineData(Ctrl, BookmarkDropMode.Standard, false)]
    [InlineData(0, BookmarkDropMode.Move, false)]
    [InlineData(Shift, BookmarkDropMode.Move, true)]
    [InlineData(0, BookmarkDropMode.Standard, true)]
    public void Feedback_VirtualIsCopyOnly(int keyState, BookmarkDropMode mode, bool right)
    {
        var data = new DataObject();
        data.SetData(ReTAC.Shell.VirtualItems.ShellIdListFormat, new MemoryStream(new byte[8]));
        foreach (var dest in new[] { @"C:\b", @"D:\b" })
        {
            var e = new DragEventArgs(data, keyState, 0, 0, Both, DragDropEffects.None);
            DropFeedback.Apply(e, dest, "b", mode, right);
            Assert.Equal(DragDropEffects.Copy, e.Effect);
            Assert.Equal(DropImageType.Copy, e.DropImageType);
        }
        var moveOnly = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Move, DragDropEffects.None);
        DropFeedback.Apply(moveOnly, @"C:\b", "b", mode, right);
        Assert.Equal(DragDropEffects.None, moveOnly.Effect);   // コピーを許さないなら不可。移動へ切り替えない
    }

    [Fact]
    public void Feedback_FileDropUnchangedWhenShellIdListAlsoPresent()
    {
        // エクスプローラーのディスク上の項目は両方を持つ。今までどおり FileDrop で判定する（同じドライブなら移動）
        var data = new DataObject(DataFormats.FileDrop, Files);
        data.SetData(ReTAC.Shell.VirtualItems.ShellIdListFormat, new MemoryStream(new byte[8]));
        var e = new DragEventArgs(data, 0, 0, 0, Both, DragDropEffects.None);
        DropFeedback.Apply(e, @"C:\b", "b", BookmarkDropMode.Standard, right: false);
        Assert.Equal(DragDropEffects.Move, e.Effect);
    }
}
