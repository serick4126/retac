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
}
