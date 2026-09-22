using ReTAC.App;
using ReTAC.Domain.Entries;

namespace ReTAC.Domain.Tests;

public class TreeCommandTargetTests
{
    private static readonly Entry Folder = Entry.ForFolder(@"C:\a", "a", FileAttributes.Directory, DateTime.Now);

    [Fact]
    public void ツリー経由でなければnullでファイル表示パネルの対象を使わせる()
    {
        Assert.Null(TreeCommandTarget.Resolve(fromTreeCommandKey: false, Folder));
        Assert.Null(TreeCommandTarget.Resolve(fromTreeCommandKey: false, null));
    }

    [Fact]
    public void ツリー経由なら選択中の実フォルダ1件を対象にする()
    {
        var result = TreeCommandTarget.Resolve(fromTreeCommandKey: true, Folder);
        Assert.Equal([Folder], result);
    }

    [Fact]
    public void ツリー経由でも実フォルダが無ければ対象なし()
    {
        var result = TreeCommandTarget.Resolve(fromTreeCommandKey: true, null);
        Assert.Empty(result!);
    }
}
