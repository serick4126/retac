using ReTAC.Domain.FileOps;

namespace ReTAC.Domain.Tests;

/// <summary>D&amp;D の効果の判定（R-65）</summary>
public class DropRulesTests
{
    [Fact]
    public void 同じドライブなら移動で別のドライブならコピー()
    {
        Assert.Equal(DropAction.Move, DropRules.Decide(@"C:\a\x.txt", @"C:\b", false, false));
        Assert.Equal(DropAction.Copy, DropRules.Decide(@"C:\a\x.txt", @"D:\b", false, false));
    }

    [Fact]
    public void Ctrlはコピー_Shiftは移動を強制する()
    {
        Assert.Equal(DropAction.Copy, DropRules.Decide(@"C:\a\x.txt", @"C:\b", ctrl: true, shift: false));
        Assert.Equal(DropAction.Move, DropRules.Decide(@"C:\a\x.txt", @"D:\b", ctrl: false, shift: true));
    }

    [Fact]
    public void 同じフォルダへのドロップは何も起こさない()
    {
        Assert.Equal(DropAction.None, DropRules.Decide(@"C:\a\x.txt", @"C:\a", false, false));
        Assert.Equal(DropAction.None, DropRules.Decide(@"C:\a\x.txt", @"C:\a\", false, false));
    }

    [Fact]
    public void フォルダを自分自身の中へは落とせない()
    {
        Assert.Equal(DropAction.None, DropRules.Decide(@"C:\work", @"C:\work", false, false));
        Assert.Equal(DropAction.None, DropRules.Decide(@"C:\work", @"C:\work\inner", false, false));
    }

    [Fact]
    public void 名前が前方一致するだけの別フォルダへは落とせる()
    {
        Assert.Equal(DropAction.Move, DropRules.Decide(@"C:\work", @"C:\work2", false, false));
    }
}
