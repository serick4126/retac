using ReTAC.Domain.Entries;
using ReTAC.Domain.Selection;

namespace ReTAC.Domain.Tests;

/// <summary>B-09 / R-70: 再表示でカーソルの項目が消えていたときの行き先</summary>
public class CursorRestoreTests
{
    private static Entry[] List(params string[] names) =>
        [TestEntries.Parent(), .. names.Select(n => TestEntries.File(n))];

    [Fact]
    public void カーソルの項目が残っていればその項目()
    {
        var index = CursorRestore.IndexAfterReload(["c.txt", "b.txt", "a.txt"], List("a.txt", "b.txt", "c.txt", "d.txt"));
        Assert.Equal(3, index);
    }

    [Fact]
    public void カーソルの項目が消えたら1つ上()
    {
        var index = CursorRestore.IndexAfterReload(["c.txt", "b.txt", "a.txt"], List("a.txt", "b.txt", "d.txt"));
        Assert.Equal(2, index);   // b.txt
    }

    [Fact]
    public void 連続して消えたら残っている最も近い上()
    {
        // マークした b と c をまとめて移動し、カーソルは c にあった
        var index = CursorRestore.IndexAfterReload(["c.txt", "b.txt", "a.txt"], List("a.txt", "d.txt"));
        Assert.Equal(1, index);   // a.txt
    }

    [Fact]
    public void 上がすべて消えたら先頭()
    {
        var index = CursorRestore.IndexAfterReload(["c.txt", "b.txt", "a.txt"], List("d.txt"));
        Assert.Equal(0, index);   // 親フォルダ項目
    }

    [Fact]
    public void 名前は大文字小文字を区別しない()
    {
        var index = CursorRestore.IndexAfterReload(["a.txt"], List("A.TXT"));
        Assert.Equal(1, index);
    }

    [Fact]
    public void 一覧が空なら0()
    {
        Assert.Equal(0, CursorRestore.IndexAfterReload(["a.txt"], []));
    }

    [Fact]
    public void カーソルから上の名前を親フォルダ項目を除いて並べる()
    {
        var state = new ListState(List("a.txt", "b.txt", "c.txt", "d.txt"));
        state.MoveCursor(3);

        Assert.Equal(["c.txt", "b.txt", "a.txt"], CursorRestore.NamesFromCursorUpward(state));
    }

    [Fact]
    public void カーソルが親フォルダ項目なら名前は空()
    {
        var state = new ListState(List("a.txt"));
        Assert.Empty(CursorRestore.NamesFromCursorUpward(state));
    }
}
