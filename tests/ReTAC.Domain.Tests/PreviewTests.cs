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
}
