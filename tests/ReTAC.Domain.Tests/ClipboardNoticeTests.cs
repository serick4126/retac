using ReTAC.App;

namespace ReTAC.Domain.Tests;

/// <summary>R-146: 名前・パスをクリップボードへ入れられたときだけ知らせる。</summary>
public class ClipboardNoticeTests
{
    [Fact]
    public void Copy_NotifiesOnlyWhenClipboardAccepted()
    {
        var notices = new List<string>();
        ClipboardNotice.CopyText("a.txt", _ => true, notices.Add);
        Assert.Equal([ClipboardNotice.CopiedMessage], notices);
        Assert.Equal("クリップボードにコピーしました", ClipboardNotice.CopiedMessage);
    }

    [Fact]
    public void Copy_NoNoticeWhenClipboardBusy()
    {
        var notices = new List<string>();
        ClipboardNotice.CopyText("a.txt", _ => false, notices.Add);   // 他のアプリが使用中（警告は tryClipboard の側が出す）
        Assert.Empty(notices);
    }

    [Fact]
    public void Copy_EmptyTextDoesNothing()
    {
        var called = false;
        var notices = new List<string>();
        ClipboardNotice.CopyText("", _ => called = true, notices.Add);
        Assert.False(called);
        Assert.Empty(notices);
    }
}
