using ReTAC.Domain.Listing;

namespace ReTAC.Domain.Tests;

/// <summary>R-39: 親フォルダに戻る（BackSpace）の行き先。ドライブルートでは何も起きない（行き先が無い）。</summary>
public class FolderParentTests
{
    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"D:\")]
    [InlineData(@"\\server\share\")]
    public void ドライブルートでは親フォルダが無い(string root)
    {
        Assert.Null(FolderEnumerator.ParentOf(root));
    }

    [Theory]
    [InlineData(@"C:\Users", @"C:\")]
    [InlineData(@"C:\Users\serick", @"C:\Users")]
    [InlineData(@"C:\Users\serick\", @"C:\Users")]
    [InlineData(@"\\server\share\docs", @"\\server\share")]
    public void ルートでなければ一つ上のフォルダへ戻る(string folder, string parent)
    {
        Assert.Equal(parent, FolderEnumerator.ParentOf(folder));
    }
}
