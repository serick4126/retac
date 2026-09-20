using ReTAC.Shell;

namespace ReTAC.Domain.Tests;

public class ShellItemPathTests
{
    [Theory]
    [InlineData(@"C:\a\b", @"C:\")]
    [InlineData(@"c:\", @"C:\")]
    [InlineData(@"\\server\share\a\b", @"\\server\share")]
    [InlineData(@"\\server\share", @"\\server\share")]
    public void 絶対フォルダパスからナビゲーションルートを返す(string path, string expected)
    {
        Assert.Equal(expected, ShellItemPath.RootOf(path));
    }

    [Theory]
    [InlineData(@"a\b")]
    [InlineData(@"\folder")]
    [InlineData(@"\\server")]
    [InlineData(@"\\server\")]
    public void 不完全なパスを拒否する(string path)
    {
        Assert.Throws<ArgumentException>(() => ShellItemPath.RootOf(path));
    }

    [Fact]
    public void 実在するファイルパスを拒否する()
    {
        var path = Path.GetTempFileName();
        try
        {
            Assert.Throws<ArgumentException>(() => ShellItemPath.RootOf(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
