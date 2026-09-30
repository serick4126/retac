using System.Drawing;
using ReTAC.App;
using ReTAC.Domain.Entries;
using ReTAC.Domain.Listing;
using SortOrder = ReTAC.Domain.Listing.SortOrder;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-123: 同じフォルダの再表示（自動更新・W・F5・ファイル操作のあと）では、見ている所を動かさない。
/// カーソルが画面外でもカーソルの位置へ戻さない。8 つの表示モードすべてで確かめる。ハンドルは作らない。
/// </summary>
public class FileListViewRedisplayTests
{
    public static TheoryData<FileViewMode> Modes => new(Enum.GetValues<FileViewMode>());

    private static List<Entry> Files(int count, string prefix = "f") =>
        Enumerable.Range(0, count).Select(i => TestEntries.File($"{prefix}{i:D4}.txt")).ToList();

    /// <summary>600 件。3 ノッチ分スクロールして、カーソル（0 番）を画面の外にする。</summary>
    private static (FileListView List, List<Entry> Entries) Scrolled(FileViewMode mode)
    {
        var list = new FileListView { Size = new Size(600, 300) };
        list.TypeText = _ => "テキスト ドキュメント";
        list.SetView(mode, new FileViewSettings(), new Dictionary<string, int?>(), SortOrder.Default);
        var entries = Files(600);
        list.SetEntries(entries);
        list.ScrollWheel(-3);
        Assert.NotEqual(default, list.ScrollPosition);
        Assert.False(list.IsCursorVisible);
        return (list, entries);
    }

    /// <summary>スクロールする軸での、項目の画面の中の位置（一覧は x、ほかは y）。</summary>
    private static int Along(FileListView list, int index)
    {
        var bounds = FileViewScroll.VisibleBounds(list.Layout, list.ScrollPosition, index);
        return list.Layout.ScrollBars.Vertical ? bounds.Y : bounds.X;
    }

    private static int IndexOf(FileListView list, string name) =>
        list.State.Entries.ToList().FindIndex(e => e.Name == name);

    [Theory, MemberData(nameof(Modes))]
    public void 同じ中身の再表示では動かず_カーソルへ戻らない(FileViewMode mode)
    {
        var (list, entries) = Scrolled(mode);
        using var _ = list;
        var before = list.ScrollPosition;

        list.SetEntries(entries, 0, keepScroll: true, revealCursor: false);

        Assert.Equal(before, list.ScrollPosition);
        Assert.False(list.IsCursorVisible);
    }

    [Theory, MemberData(nameof(Modes))]
    public void 見えている所より前に足しても_先頭の項目は同じ段に残る(FileViewMode mode)
    {
        var (list, entries) = Scrolled(mode);
        using var _ = list;
        var anchor = list.State.Entries[list.FirstVisibleIndex].Name;
        var along = Along(list, list.FirstVisibleIndex);

        var after = Files(7, "a").Concat(entries).ToList();   // 7 件が前に入る
        list.SetEntries(after, 7, keepScroll: true, revealCursor: false);

        Assert.Equal(along, Along(list, IndexOf(list, anchor)));
        Assert.False(list.IsCursorVisible);
    }

    [Theory, MemberData(nameof(Modes))]
    public void 見えている所より前を消しても_先頭の項目は同じ段に残る(FileViewMode mode)
    {
        var (list, entries) = Scrolled(mode);
        using var _ = list;
        var anchor = list.State.Entries[list.FirstVisibleIndex].Name;
        var along = Along(list, list.FirstVisibleIndex);

        var after = entries.Take(1).Concat(entries.Skip(3)).ToList();   // 1・2 番を消す（0 番のカーソルと、1 列の並べて表示で先頭に見える 3 番は残す）
        list.SetEntries(after, 0, keepScroll: true, revealCursor: false);

        Assert.Equal(along, Along(list, IndexOf(list, anchor)));
    }

    [Theory, MemberData(nameof(Modes))]
    public void 見えている所より後ろで足しても消しても動かない(FileViewMode mode)
    {
        var (list, entries) = Scrolled(mode);
        using var _ = list;
        var before = list.ScrollPosition;

        list.SetEntries(entries.Concat(Files(20, "z")).ToList(), 0, keepScroll: true, revealCursor: false);
        Assert.Equal(before, list.ScrollPosition);

        list.SetEntries(entries.Take(590).ToList(), 0, keepScroll: true, revealCursor: false);
        Assert.Equal(before, list.ScrollPosition);
    }

    [Theory, MemberData(nameof(Modes))]
    public void 先頭の項目が消えたら_その前の残っている項目が同じ段に来る(FileViewMode mode)
    {
        var (list, entries) = Scrolled(mode);
        using var _ = list;
        var first = list.FirstVisibleIndex;
        var previous = entries[first - 1].Name;
        var along = Along(list, first);

        var after = entries.Where((_, i) => i != first).ToList();
        list.SetEntries(after, 0, keepScroll: true, revealCursor: false);

        Assert.Equal(along, Along(list, IndexOf(list, previous)));
    }

    [Theory, MemberData(nameof(Modes))]
    public void 項目が減って範囲の外になったら範囲に収まる(FileViewMode mode)
    {
        var (list, entries) = Scrolled(mode);
        using var _ = list;

        list.SetEntries(entries.Take(2).ToList(), 0, keepScroll: true, revealCursor: false);

        Assert.Equal(default, list.ScrollPosition);
    }

    [Theory, MemberData(nameof(Modes))]
    public void 空の一覧から_空の一覧への再表示でも落ちない(FileViewMode mode)
    {
        var (list, entries) = Scrolled(mode);
        using var _ = list;

        list.SetEntries([], 0, keepScroll: true, revealCursor: false);
        Assert.Equal(default, list.ScrollPosition);

        list.SetEntries(entries, 0, keepScroll: true, revealCursor: false);
        Assert.Equal(default, list.ScrollPosition);
    }

    [Theory, MemberData(nameof(Modes))]
    public void 先頭にいるときは_前に足されても先頭のまま(FileViewMode mode)
    {
        using var list = new FileListView { Size = new Size(600, 300) };
        list.SetView(mode, new FileViewSettings(), new Dictionary<string, int?>(), SortOrder.Default);
        var entries = Files(600);
        list.SetEntries(entries);

        // 1 件が前に入る。カーソル（先頭だった項目）は 1 つ後ろへ動くが、見えているまま。
        // 先頭の項目を保つ規則だけだと、1 段スクロールして新しい項目を隠してしまう
        list.SetEntries(Files(1, "a").Concat(entries).ToList(), 1, keepScroll: true, revealCursor: false);

        Assert.Equal(default, list.ScrollPosition);
        Assert.True(list.IsCursorVisible);
    }

    [Theory, MemberData(nameof(Modes))]
    public void カーソルが見えていたなら_並びが変わっても見えるまま(FileViewMode mode)
    {
        var (list, entries) = Scrolled(mode);
        using var _ = list;
        list.MoveCursorTo(list.FirstVisibleIndex);             // 見えている項目へカーソルを置く
        Assert.True(list.IsCursorVisible);
        var name = list.State.Cursor!.Name;

        var reversed = entries.AsEnumerable().Reverse().ToList();   // 並べ替え
        list.SetEntries(reversed, reversed.FindIndex(e => e.Name == name), keepScroll: true, revealCursor: false);

        Assert.True(list.IsCursorVisible);
    }

    [Theory, MemberData(nameof(Modes))]
    public void カーソルの項目が消えても_見えていなかったなら動かない(FileViewMode mode)
    {
        var (list, entries) = Scrolled(mode);
        using var _ = list;
        var anchor = list.State.Entries[list.FirstVisibleIndex].Name;
        var along = Along(list, list.FirstVisibleIndex);

        // カーソル（0 番）が消え、R-70 で先頭へ移る（MainForm が求めた添字を渡す）
        list.SetEntries(entries.Skip(1).ToList(), 0, keepScroll: true, revealCursor: false);

        Assert.Equal(along, Along(list, IndexOf(list, anchor)));
        Assert.False(list.IsCursorVisible);
    }

    [Theory, MemberData(nameof(Modes))]
    public void コマンドがカーソルを置いた再表示は_その項目を見せる(FileViewMode mode)
    {
        var (list, entries) = Scrolled(mode);
        using var _ = list;

        list.SetEntries(entries, 599, keepScroll: true, revealCursor: true);   // 名前の変更・新規作成の直後

        Assert.True(list.IsCursorVisible);
    }

    [Theory, MemberData(nameof(Modes))]
    public void 別のフォルダを開いたら先頭から(FileViewMode mode)
    {
        var (list, entries) = Scrolled(mode);
        using var _ = list;

        list.SetEntries(entries);   // keepScroll なし

        Assert.Equal(default, list.ScrollPosition);
    }

    [Fact]
    public void 種類名が届いて列が広がっても_カーソルが見えていなければ動かない()
    {
        var names = new Dictionary<string, string>();
        using var list = new FileListView { Size = new Size(700, 300) };
        list.TypeText = e => names.GetValueOrDefault(e.Extension, "");
        list.SetView(FileViewMode.Details, new FileViewSettings(), new Dictionary<string, int?>(), SortOrder.Default);
        list.SetEntries(Enumerable.Range(0, 200).Select(i => TestEntries.File($"f{i:D3}.x")).ToList());
        list.ScrollWheel(-5);
        var before = list.ScrollPosition;
        Assert.False(list.IsCursorVisible);

        names[".x"] = new string('あ', 60);
        list.QueueResolvedType(ReTAC.Shell.ShellFileType.KeyOf(@"C:\work\f000.x", false));
        list.FlushResolvedTypes();

        Assert.True(list.Layout.ScrollBars.Horizontal);   // 列が広がった
        Assert.Equal(before, list.ScrollPosition);
    }
}
