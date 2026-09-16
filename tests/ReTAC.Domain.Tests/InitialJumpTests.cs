using ReTAC.Domain.Selection;

namespace ReTAC.Domain.Tests;

/// <summary>Shift+英字の頭出し（卓駆の挙動。実機確認 E）</summary>
public class InitialJumpTests
{
    // 01.txt / A1.txt / A2.txt / A3.txt / B1.txt に親フォルダ項目を足したもの
    private static ListState Sample() => new(
    [
        TestEntries.Parent(),
        TestEntries.File("01.txt"),
        TestEntries.File("A1.txt"),
        TestEntries.File("A2.txt"),
        TestEntries.File("A3.txt"),
        TestEntries.File("B1.txt"),
    ]);

    [Fact]
    public void 頭文字が同じ項目を下方向にたどり末尾で先頭へ回る()
    {
        var state = Sample();
        state.MoveCursor(1);                                  // 01.txt
        Assert.Equal(2, state.IndexOfNextStartingWith('A'));  // A1
        state.MoveCursor(2);
        Assert.Equal(3, state.IndexOfNextStartingWith('A'));  // A2
        state.MoveCursor(3);
        Assert.Equal(4, state.IndexOfNextStartingWith('A'));  // A3
        state.MoveCursor(4);
        Assert.Equal(2, state.IndexOfNextStartingWith('A'));  // A1 へ戻る
    }

    [Fact]
    public void 大文字小文字は区別しない()
    {
        var state = Sample();
        state.MoveCursor(1);
        Assert.Equal(2, state.IndexOfNextStartingWith('a'));
    }

    [Fact]
    public void フォルダも同じ規則の対象になる()
    {
        var state = new ListState([TestEntries.File("B1.txt"), TestEntries.Folder("Archive"), TestEntries.File("A9.txt")]);
        Assert.Equal(1, state.IndexOfNextStartingWith('A'));
    }

    [Fact]
    public void 該当が無ければ移動しない()
    {
        var state = Sample();
        state.MoveCursor(1);
        Assert.Equal(-1, state.IndexOfNextStartingWith('Z'));
    }

    [Fact]
    public void 親フォルダ項目と日本語の名前には当たらない()
    {
        var state = new ListState([TestEntries.Parent(), TestEntries.File("あいうえお.txt")]);
        Assert.Equal(-1, state.IndexOfNextStartingWith('A'));
    }
}
