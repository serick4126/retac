using System.Drawing;
using ReTAC.App;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-11-2: Shift+押下でカーソルが動いていないときに、EnsureCursorVisible が古いカーソルの列
/// （横スクロールで画面外かもしれない）へ表示を戻してしまう不具合の再現テスト。
/// 押した瞬間の Control.ModifierKeys はテストから作れないので、PressLeft(location, shift) を直接呼ぶ。
/// </summary>
public class FileListViewPressTests
{
    /// <summary>
    /// 300x120・200 件で複数列にする。「左端は常に列の境界」（ColumnLayout）なので、
    /// スクロール後に画面の左上 (5,5) を押すと、常にそのときの一番左に見えている列の先頭に当たる。
    /// 列幅・行高の実測値を知らなくても、この 1 点だけで「見えている項目」を確実に拾える。
    /// </summary>
    private static FileListView CreateScrollableList()
    {
        var entries = Enumerable.Range(0, 200).Select(i => TestEntries.File($"file{i:D3}.txt")).ToList();
        var list = new FileListView { Size = new Size(300, 120) };
        list.SetEntries(entries);
        return list;
    }

    private static readonly Point TopLeft = new(5, 5);

    [Fact]
    public void Shift押下でカーソルが動かないときスクロールは戻らない()
    {
        using var list = CreateScrollableList();
        Assert.Equal(0, list.State.CursorIndex);

        // 列 0 が画面外になるまで右へスクロールする（負の delta で X が増える。WheelAccumulator.Add と同じ符号）
        list.ScrollColumns(-1000);
        var scrolled = list.ScrollPosition;
        Assert.True(scrolled.X > 0);

        // 今見えている項目（列 0 ではない）を Shift+押下する。カーソルは 0 のまま動かない
        list.PressLeft(TopLeft, shift: true);

        Assert.Equal(0, list.State.CursorIndex);
        // 不具合はここで EnsureCursorVisible が古いカーソル（列 0）へ戻してしまうこと
        Assert.Equal(scrolled, list.ScrollPosition);
    }

    [Fact]
    public void 非Shift押下ではカーソルが移りスクロールは列0へ戻らない()
    {
        using var list = CreateScrollableList();
        list.ScrollColumns(-1000);
        var scrolled = list.ScrollPosition;

        list.PressLeft(TopLeft, shift: false);

        Assert.NotEqual(0, list.State.CursorIndex);
        Assert.Equal(scrolled, list.ScrollPosition);   // 押した項目はすでに見えている列なのでスクロールは動かない
    }
}
