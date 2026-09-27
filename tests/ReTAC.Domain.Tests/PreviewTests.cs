using System.IO;
using ReTAC.App;
using ReTAC.Domain.Entries;

namespace ReTAC.Domain.Tests;

/// <summary>R-99: プレビューの対象</summary>
public class PreviewTests
{
    [Fact]
    public void 対象はカーソル位置のファイルだけ()
    {
        Assert.Equal(@"C:\a\b.txt", PreviewTarget.Of(Entry.ForFile(@"C:\a\b.txt", "b.txt", FileAttributes.Normal, 1, DateTime.Now)));
        Assert.Null(PreviewTarget.Of(Entry.ForFolder(@"C:\a\c", "c", FileAttributes.Directory, DateTime.Now)));
        Assert.Null(PreviewTarget.Of(Entry.ForParent(@"C:\")));
        Assert.Null(PreviewTarget.Of(null));
    }

    [Theory]
    [InlineData(new byte[] { 0x7B, 0x22, 0x61, 0x22, 0x7D }, true)]   // {"a"}
    [InlineData(new byte[] { 0xE3, 0x81, 0x82 }, true)]               // UTF-8 の「あ」
    [InlineData(new byte[] { 0xFF, 0xFE, 0x42, 0x30 }, true)]         // UTF-16 は NUL を含むが BOM で見る
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00 }, false)]  // NUL を含むものはテキストではない
    [InlineData(new byte[0], true)]                                   // 空のファイル
    public void 登録の無いファイルは先頭にNULが無ければテキストとみなす(byte[] head, bool text) =>
        Assert.Equal(text, ReTAC.Shell.PreviewFallback.IsText(head));

    // WM_XBUTTONDOWN / UP と、mouseData の上位ワード（XBUTTON1 = 1, XBUTTON2 = 2）
    private const int XDown = 0x020B, XUp = 0x020C, LDown = 0x0201, LUp = 0x0202;
    private const uint X1 = 1u << 16, X2 = 2u << 16;

    [Fact]
    public void プレビューの上で押したX1を止めても外で押したX2の離すは止めない()
    {
        var blocked = 0;
        Assert.True(ReTAC.Shell.PreviewMouseBlocker.ShouldBlock(ref blocked, XDown, X1, () => true));
        Assert.False(ReTAC.Shell.PreviewMouseBlocker.ShouldBlock(ref blocked, XDown, X2, () => false));
        Assert.False(ReTAC.Shell.PreviewMouseBlocker.ShouldBlock(ref blocked, XUp, X2, () => false));
        Assert.True(ReTAC.Shell.PreviewMouseBlocker.ShouldBlock(ref blocked, XUp, X1, () => false));
        Assert.Equal(0, blocked);
    }

    [Fact]
    public void 外で押したボタンはプレビューの上で離しても止めない()
    {
        // 境界線のドラッグをプレビューの上で離したとき。止めると SplitContainer がドラッグを終えられない
        var blocked = 0;
        Assert.False(ReTAC.Shell.PreviewMouseBlocker.ShouldBlock(ref blocked, LDown, 0, () => false));
        Assert.False(ReTAC.Shell.PreviewMouseBlocker.ShouldBlock(ref blocked, LUp, 0, () => true));
    }
}
