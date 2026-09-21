using ReTAC.Shell;

namespace ReTAC.Domain.Tests;

public class NameSpaceTreePolicyTests
{
    [Theory]
    [InlineData(@"C:\a\b", @"C:\")]
    [InlineData(@"\\server\share\a\b", @"\\server\share")]
    public void 現在位置から唯一のルートを返す(string currentFolder, string expected)
    {
        Assert.Equal(expected, NameSpaceTreePolicy.RootOf(currentFolder));
    }

    [Theory]
    [InlineData(@"C:\", @"C:\a\b", true)]
    [InlineData(@"C:\a", @"c:\A\b\", true)]
    [InlineData(@"C:\a\", @"c:\A", true)]
    [InlineData(@"C:\foo", @"C:\foobar", false)]
    [InlineData(@"C:\a\b", @"C:\a", false)]
    [InlineData(@"D:\", @"C:\a", false)]
    [InlineData(@"\\server\share", @"\\SERVER\SHARE\a", true)]
    [InlineData(@"\\server\other", @"\\server\share\a", false)]
    public void 現在位置またはその祖先だけを判定する(string itemPath, string currentFolder, bool expected)
    {
        Assert.Equal(expected, NameSpaceTreePolicy.IsCurrentPathOrAncestor(itemPath, currentFolder));
    }

    [Theory]
    [InlineData(@"C:\", @"c:\", false)]
    [InlineData(@"\\server\share", @"\\SERVER\SHARE\", false)]
    [InlineData(@"C:\", @"D:\", true)]
    [InlineData(@"\\server\share", @"\\server\other", true)]
    public void ルートが変わる場合だけ作り直す(string oldRoot, string newRoot, bool expected)
    {
        Assert.Equal(expected, NameSpaceTreePolicy.MustRebuildRoot(oldRoot, newRoot));
    }
}
