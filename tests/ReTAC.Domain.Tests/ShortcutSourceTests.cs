using ReTAC.Domain.FileOps;

namespace ReTAC.Domain.Tests;

/// <summary>R-111-3: ショートカットの名前の元と作業フォルダ</summary>
public class ShortcutSourceTests
{
    [Theory]
    [InlineData(@"C:\a\b.txt", "b.txt", @"C:\a")]
    [InlineData(@"C:\a\dir\", "dir", @"C:\a")]
    [InlineData(@"C:\a\dir", "dir", @"C:\a")]
    [InlineData(@"C:\", "C", null)]
    [InlineData(@"C:", "C", null)]
    [InlineData(@"\\server\share\", "share", null)]
    [InlineData(@"\\server\share", "share", null)]
    [InlineData(@"\\server\share\x.txt", "x.txt", @"\\server\share")]
    [InlineData(@"\\server\share\dir\", "dir", @"\\server\share")]
    public void 名前に区切りを含まず作業フォルダはリンク元のフォルダ(string path, string name, string? folder)
    {
        Assert.Equal((name, folder), ShortcutSource.Of(path));
    }
}
