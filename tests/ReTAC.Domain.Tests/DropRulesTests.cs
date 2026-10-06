using ReTAC.Domain.FileOps;
using ReTAC.Domain.Navigation;

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

    /// <summary>R-97-3 / §9: ツリーへのドラッグ中の宛先判定（実フォルダ・仮想項目・自分自身・自分の子孫）。</summary>
    [Fact]
    public void ツリーの宛先判定_実フォルダ_仮想項目_自分自身_子孫()
    {
        Assert.Equal(DropAction.Move, DropRules.DecideForTree(@"C:\a\x.txt", @"C:\b", false, false));   // 実フォルダ
        Assert.Equal(DropAction.None, DropRules.DecideForTree(@"C:\a\x.txt", null, false, false));      // 仮想項目(パス無し)
        Assert.Equal(DropAction.None, DropRules.DecideForTree(@"C:\work", @"C:\work", false, false));   // 自分自身
        Assert.Equal(DropAction.None, DropRules.DecideForTree(@"C:\work", @"C:\work\inner", false, false));   // 自分の子孫
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

    // R-140: 指定 3 種 × キー（なし・Ctrl・Shift・両方）× 同じ／別のドライブ × 許す効果（コピーだけ・移動だけ・両方）
    public static IEnumerable<object[]> FixedCases()
    {
        foreach (var mode in Enum.GetValues<BookmarkDropMode>())
        foreach (var (ctrl, shift) in new[] { (false, false), (true, false), (false, true), (true, true) })
        foreach (var dest in new[] { @"C:\b", @"D:\b" })
        foreach (var (copy, move) in new[] { (true, false), (false, true), (true, true) })
        {
            DropAction expected;
            if (mode == BookmarkDropMode.Standard || ctrl || shift)
                expected = DropRules.Allow(DropRules.Decide(@"C:\a\x.txt", dest, ctrl, shift), copy, move);
            else
                expected = mode == BookmarkDropMode.Copy
                    ? (copy ? DropAction.Copy : DropAction.None)
                    : (move ? DropAction.Move : DropAction.None);
            yield return [mode, ctrl, shift, dest, copy, move, expected];
        }
    }

    [Theory]
    [MemberData(nameof(FixedCases))]
    public void Fixed_Table(BookmarkDropMode mode, bool ctrl, bool shift, string dest, bool copy, bool move, DropAction expected)
    {
        Assert.Equal(expected, DropRules.DecideFixed(mode, @"C:\a\x.txt", dest, ctrl, shift, copy, move));
    }

    [Fact]
    public void Fixed_固定の移動は許されなければ落とせない_コピーへ格下げしない()
    {
        Assert.Equal(DropAction.None, DropRules.DecideFixed(BookmarkDropMode.Move, @"C:\a\x.txt", @"C:\b", false, false, copyAllowed: true, moveAllowed: false));
    }

    [Fact]
    public void Fixed_固定のコピーは許されなければ落とせない()
    {
        Assert.Equal(DropAction.None, DropRules.DecideFixed(BookmarkDropMode.Copy, @"C:\a\x.txt", @"D:\b", false, false, copyAllowed: false, moveAllowed: true));
    }

    [Theory]
    [InlineData(BookmarkDropMode.Copy)]
    [InlineData(BookmarkDropMode.Move)]
    public void Fixed_同じフォルダと自分の中は指定があっても落とせない(BookmarkDropMode mode)
    {
        Assert.Equal(DropAction.None, DropRules.DecideFixed(mode, @"C:\a\x.txt", @"C:\a", false, false, true, true));
        Assert.Equal(DropAction.None, DropRules.DecideFixed(mode, @"C:\a", @"C:\a\sub", false, false, true, true));
    }

    [Fact]
    public void SplitFixed_同じドライブと別のドライブが混ざっても全部同じ動作()
    {
        string[] files = [@"C:\a\x.txt", @"D:\y.txt"];
        var (copies, moves) = DropRules.SplitFixed(BookmarkDropMode.Copy, files, @"C:\b", false, false, true, true);
        Assert.Equal(2, copies.Count);
        Assert.Empty(moves);
        (copies, moves) = DropRules.SplitFixed(BookmarkDropMode.Move, files, @"C:\b", false, false, true, true);
        Assert.Empty(copies);
        Assert.Equal(2, moves.Count);
    }
}
