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

    [Fact]
    public void 落とした項目をコピーと移動に振り分ける()
    {
        string[] files = [@"D:\a\1.txt", @"C:\a\2.txt", @"C:\b", @"C:\b\3.txt", @"D:\a\4.txt"];
        var (copies, moves) = DropRules.Split(files, @"C:\b", ctrl: false, shift: false, copyAllowed: true, moveAllowed: true);
        Assert.Equal([@"D:\a\1.txt", @"D:\a\4.txt"], copies);   // 別のドライブ。並びはドロップ元の順
        Assert.Equal([@"C:\a\2.txt"], moves);                    // 自分自身・同じフォルダの項目は落とす
    }

    [Fact]
    public void 振り分けは修飾キーとドラッグ元が許す効果に従う()
    {
        string[] files = [@"C:\a\1.txt", @"D:\a\2.txt"];
        Assert.Equal((2, 0), Count(DropRules.Split(files, @"C:\b", ctrl: true, shift: false, true, true)));
        Assert.Equal((0, 2), Count(DropRules.Split(files, @"C:\b", ctrl: false, shift: true, true, true)));
        Assert.Equal((2, 0), Count(DropRules.Split(files, @"C:\b", false, false, copyAllowed: true, moveAllowed: false)));
        Assert.Equal((0, 1), Count(DropRules.Split(files, @"C:\b", false, false, copyAllowed: false, moveAllowed: true)));

        static (int, int) Count((IReadOnlyList<string> Copies, IReadOnlyList<string> Moves) split) => (split.Copies.Count, split.Moves.Count);
    }

    [Theory]
    [InlineData(DropAction.Move, true, true, DropAction.Move)]
    [InlineData(DropAction.Move, true, false, DropAction.Copy)]    // 移動を許さないドラッグ元ならコピーに落とす
    [InlineData(DropAction.Move, false, false, DropAction.None)]
    [InlineData(DropAction.Copy, true, false, DropAction.Copy)]
    [InlineData(DropAction.Copy, false, true, DropAction.None)]    // コピーを許さないなら移動にはしない
    [InlineData(DropAction.None, true, true, DropAction.None)]
    public void ドラッグ元が許す効果に合わせる(DropAction action, bool copy, bool move, DropAction expected)
    {
        Assert.Equal(expected, DropRules.Allow(action, copy, move));
    }
}
