using ReTAC.Domain.Entries;
using ReTAC.Domain.Selection;

namespace ReTAC.Domain.Tests;

/// <summary>マークと実効対象</summary>
public class ListStateTests
{
    private static ListState MakeState() => new(
    [
        TestEntries.Parent(),
        TestEntries.Folder("sub"),
        TestEntries.File("a.txt"),
        TestEntries.File("b.txt"),
        TestEntries.File("c.log"),
    ]);

    [Fact]
    public void 列移動は隣に項目が無ければ動かさない()
    {
        // 5 件・1 列 3 行として ← → を試す。端で丸めると先頭・末尾へ飛んでしまう
        var state = MakeState();

        state.MoveCursor(1);
        state.MoveCursorToNeighborColumn(-3);       // 左端で ←
        Assert.Equal(1, state.CursorIndex);

        state.MoveCursorToNeighborColumn(3);        // 右へ 1 列（1 → 4）
        Assert.Equal(4, state.CursorIndex);

        state.MoveCursorToNeighborColumn(3);        // 右端で →（7 は範囲外）
        Assert.Equal(4, state.CursorIndex);

        state.MoveCursorToNeighborColumn(-3);       // 戻れる
        Assert.Equal(1, state.CursorIndex);
    }

    [Fact]
    public void カーソル移動はマークを変更しない()
    {
        // R-11: 本システムの存在理由
        var state = MakeState();
        state.MoveCursor(2);
        state.ToggleMarkAndAdvance();   // a.txt をマーク
        var before = state.Marks.ToHashSet();

        state.MoveCursor(4);
        state.MoveCursor(1);
        state.MoveCursorBy(3);

        Assert.Equal(before, state.Marks.ToHashSet());
        Assert.Single(state.Marks);
    }

    [Fact]
    public void Spaceはマークをトグルしてカーソルを次へ進める()
    {
        // R-11-3
        var state = MakeState();
        state.MoveCursor(1);
        state.ToggleMarkAndAdvance();
        Assert.Equal(2, state.CursorIndex);
        Assert.Contains(1, state.Marks);

        state.ToggleMarkAndAdvance();
        Assert.Equal(3, state.CursorIndex);
        Assert.Equal([1, 2], state.Marks.Order());
    }

    [Fact]
    public void Spaceの連打で連続マークできる()
    {
        var state = MakeState();
        state.MoveCursor(1);
        for (var i = 0; i < 4; i++) state.ToggleMarkAndAdvance();
        Assert.Equal([1, 2, 3, 4], state.Marks.Order());
    }

    [Fact]
    public void 親フォルダ項目はマークできない()
    {
        // R-04
        var state = MakeState();
        state.MoveCursor(0);
        state.ToggleMarkAndAdvance();
        Assert.Empty(state.Marks);
        Assert.Equal(1, state.CursorIndex);
    }

    [Fact]
    public void カーソルは末尾で止まりラップアラウンドしない()
    {
        // 14.1 節「↑↓キーでラップアラウンドする」= OFF
        var state = MakeState();
        state.MoveCursor(4);
        state.MoveCursorBy(1);
        Assert.Equal(4, state.CursorIndex);
        state.MoveCursor(0);
        state.MoveCursorBy(-1);
        Assert.Equal(0, state.CursorIndex);
    }

    [Fact]
    public void 全選択と全解除がトグルする()
    {
        // 0x8307。フォルダもファイルと同じ扱い（14.1 節）
        var state = MakeState();
        state.ToggleAllMarks();
        Assert.Equal([1, 2, 3, 4], state.Marks.Order());
        state.ToggleAllMarks();
        Assert.Empty(state.Marks);
    }

    [Fact]
    public void 反転選択はマークの有無を入れ替える()
    {
        var state = MakeState();
        state.ToggleMark(2);
        state.InvertMarks();
        Assert.Equal([1, 3, 4], state.Marks.Order());
    }

    [Fact]
    public void ワイルドカードで選択はマークに追加する()
    {
        var state = MakeState();
        state.ToggleMark(1);
        state.MarkByWildcard("*.txt");
        Assert.Equal([1, 2, 3], state.Marks.Order());
    }

    [Fact]
    public void 同じ拡張子で選択はカーソル位置と同じ拡張子を追加する()
    {
        var state = MakeState();
        state.MoveCursor(2);            // a.txt
        state.MarkBySameExtension();
        Assert.Equal([2, 3], state.Marks.Order());
    }

    [Fact]
    public void フォルダ移動でマークは解除される()
    {
        // R-11-4
        var state = MakeState();
        state.ToggleAllMarks();
        state.ClearMarks();
        Assert.Empty(state.Marks);
    }

    [Fact]
    public void 実効対象はマークが0件ならカーソル位置1件である()
    {
        // R-10
        var state = MakeState();
        state.MoveCursor(3);
        var target = state.EffectiveTarget();
        Assert.Single(target);
        Assert.Equal("b.txt", target[0].Name);
    }

    [Fact]
    public void 実効対象はマークが1件以上ならマーク集合である()
    {
        var state = MakeState();
        state.ToggleMark(2);
        state.ToggleMark(4);
        state.MoveCursor(1);            // カーソルは別の場所にある
        Assert.Equal(["a.txt", "c.log"], TestEntries.Names(state.EffectiveTarget()));
    }

    [Fact]
    public void 実効対象は親フォルダ項目だけのときは空になる()
    {
        var state = new ListState([TestEntries.Parent()]);
        Assert.Empty(state.EffectiveTarget());
    }
}
