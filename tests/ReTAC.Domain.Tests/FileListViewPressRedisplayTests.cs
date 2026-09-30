using System.Drawing;
using System.Windows.Forms;
using ReTAC.App;
using ReTAC.Domain.Entries;
using ReTAC.Domain.Listing;
using SortOrder = ReTAC.Domain.Listing.SortOrder;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-124: 再表示の前に押していた操作は、押した項目を名前で探し直して続ける。見つからなければ捨てる。
/// 押した状態ごとに別の内部の状態を使っているので、1 つずつ確かめる。ハンドルは作らない。
/// </summary>
public class FileListViewPressRedisplayTests
{
    public enum Change { Same, Reordered, Gone }

    public static TheoryData<Change> Changes => new(Enum.GetValues<Change>());

    private const int Pressed = 3;
    private const string PressedName = "f03.txt";

    private static List<Entry> Files() => Enumerable.Range(0, 12).Select(i => TestEntries.File($"f{i:D2}.txt")).ToList();

    private static FileListView View(FileViewMode mode)
    {
        var list = new FileListView { Size = new Size(600, 400) };
        list.SetView(mode, new FileViewSettings(), new Dictionary<string, int?>(), SortOrder.Default);
        list.SetEntries(Files());
        return list;
    }

    /// <summary>再表示する。カーソルは MainForm と同じく名前で引き継ぐ（消えていたら先頭）。</summary>
    private static void Redisplay(FileListView list, Change change)
    {
        var cursorName = list.State.Cursor!.Name;
        var after = change switch
        {
            Change.Reordered => new[] { "a0.txt", "a1.txt" }.Select(n => TestEntries.File(n)).Concat(Files()).ToList(),   // 前に 2 件入る
            Change.Gone => Files().Where(e => e.Name != PressedName).ToList(),
            _ => Files(),
        };
        list.SetEntries(after, Math.Max(0, after.FindIndex(e => e.Name == cursorName)), keepScroll: true, revealCursor: false);
    }

    private static int Now(FileListView list) => list.State.Entries.ToList().FindIndex(e => e.Name == PressedName);

    private static Point In((int X, int Y, int Width, int Height) r) => new(r.X + 2, r.Y + 2);

    [Theory, MemberData(nameof(Changes))]
    public void 行頭アイコンを押したまま再表示されても_離すと押した項目のマークが変わる(Change change)
    {
        using var list = View(FileViewMode.List);
        list.PressLeft(In(list.Layout.IconBounds(Pressed)), shift: false);

        Redisplay(list, change);
        list.ReleaseLeft();

        if (change == Change.Gone) Assert.Empty(list.State.Marks);
        else Assert.Equal([Now(list)], list.State.Marks);
    }

    [Theory, MemberData(nameof(Changes))]
    public void チェックボックスを押したまま再表示されても_離すと押した項目のマークが変わる(Change change)
    {
        using var list = View(FileViewMode.MediumIcons);
        list.PressLeft(In(list.Layout.CheckBoxBounds(Pressed)!.Value), shift: false);

        Redisplay(list, change);
        list.ReleaseLeft();

        if (change == Change.Gone) Assert.Empty(list.State.Marks);
        else Assert.Equal([Now(list)], list.State.Marks);
    }

    [Theory, MemberData(nameof(Changes))]
    public void 右ボタンを押したまま再表示されても_押した項目を指したまま(Change change)
    {
        using var list = View(FileViewMode.List);
        list.PressRight(In(list.Layout.NameBounds(Pressed)));
        Assert.Equal(Pressed, list.RightDownIndex);

        Redisplay(list, change);

        if (change == Change.Gone) Assert.Null(list.RightDownIndex);   // メニューも右ボタンのドラッグも起きない
        else Assert.Equal(Now(list), list.RightDownIndex);
    }

    [Fact]
    public void 項目の無い所で右ボタンを押していたら_再表示のあとも背景のまま()
    {
        using var list = View(FileViewMode.List);
        list.PressRight(new Point(590, 10));   // 12 件は 1 列に収まる。その右は空いた所
        Assert.Equal(-1, list.RightDownIndex);

        Redisplay(list, Change.Reordered);

        Assert.Equal(-1, list.RightDownIndex);
    }

    [Theory, MemberData(nameof(Changes))]
    public void 名前を押したまま再表示されても_動かすと押した項目のドラッグが始まる(Change change)
    {
        using var list = View(FileViewMode.List);
        IReadOnlyList<string>? started = null;
        list.StartDragOverride = paths => started = paths;
        var point = In(list.Layout.NameBounds(Pressed));
        list.PressLeft(point, shift: false);

        Redisplay(list, change);
        list.RaiseMouseMove(new MouseEventArgs(MouseButtons.Left, 1, point.X + 60, point.Y, 0));

        if (change == Change.Gone) Assert.Null(started);
        else Assert.Equal([$@"C:\work\{PressedName}"], started);
        Assert.Empty(list.State.Marks);   // ドラッグでマークは変わらない
    }

    [Fact]
    public void 投げ縄の最中の再表示では_投げ縄が続き_仮のマークを新しい一覧で求め直す()
    {
        using var list = View(FileViewMode.Details);
        var (rowX, row, rowWidth, _) = list.Layout.ItemBounds(1);    // row は 1 行の高さ（1 番の行の y）
        var x = Math.Min(rowX + rowWidth - 5, list.Size.Width - 40); // 行の右端の近く = 名前以外の列（行の幅の中・見えている範囲の中）
        var start = new Point(x, list.Layout.HeaderHeight + row * 4 + 2);
        list.LassoPress(start, ctrl: false);
        list.PressLeft(start, shift: false);
        list.RaiseMouseMove(new MouseEventArgs(MouseButtons.Left, 1, x - 30, list.Layout.HeaderHeight + row * 6 + 2, 0));
        Assert.True(list.LassoActive);

        Redisplay(list, Change.Reordered);

        Assert.True(list.LassoActive);
        var (rx, ry, rw, rh) = list.LassoRect;
        var covered = list.Layout.IndexesIn(rx, ry, rw, rh, list.State.Count);
        Assert.NotEmpty(covered);
        Assert.All(covered, i => Assert.True(list.IsMarkedForDisplay(i)));
        list.LassoRelease();
        Assert.Equal(covered.Order(), list.State.Marks.Order());
    }

    [Fact]
    public void 投げ縄の最中にカーソルを見せる再表示が来ても_矩形は画面の同じ所に残る()
    {
        // カーソルを見せるためにスクロールした分も、投げ縄の矩形に入れる（付け替えは、スクロール位置が決まってから行う）
        using var list = new FileListView { Size = new Size(600, 300) };
        list.SetView(FileViewMode.Details, new FileViewSettings(), new Dictionary<string, int?>(), SortOrder.Default);
        var entries = Enumerable.Range(0, 200).Select(i => TestEntries.File($"f{i:D3}.txt")).ToList();
        list.SetEntries(entries);
        list.ScrollWheel(-5);                                          // カーソル（0 番）は画面の外
        var (rowX, row, rowWidth, _) = list.Layout.ItemBounds(1);
        var x = Math.Min(rowX + rowWidth - 5, list.Size.Width - 40);
        var start = new Point(x, list.Layout.HeaderHeight + row * 2 + 2);
        list.LassoPress(start, ctrl: false);
        list.PressLeft(start, shift: false);
        list.RaiseMouseMove(new MouseEventArgs(MouseButtons.Left, 1, x - 30, list.Layout.HeaderHeight + row * 4 + 2, 0));
        Assert.True(list.LassoActive);
        var before = FileViewScroll.ToVisible(list.Layout, list.ScrollPosition, list.LassoRect);

        list.SetEntries(entries, 0, keepScroll: true, revealCursor: true);   // カーソルを見せるために、先頭までスクロールする

        Assert.Equal(default, list.ScrollPosition);
        Assert.True(list.LassoActive);
        Assert.Equal(before, FileViewScroll.ToVisible(list.Layout, list.ScrollPosition, list.LassoRect));
        var (rx, ry, rw, rh) = list.LassoRect;
        var covered = list.Layout.IndexesIn(rx, ry, rw, rh, list.State.Count).ToHashSet();
        Assert.NotEmpty(covered);
        for (var i = 0; i < list.State.Count; i++) Assert.Equal(covered.Contains(i), list.IsMarkedForDisplay(i));
    }

    [Fact]
    public void 別のフォルダへ移ったら_押していた操作は捨てる()
    {
        using var list = View(FileViewMode.List);
        list.PressLeft(In(list.Layout.IconBounds(Pressed)), shift: false);

        list.SetEntries(Files());   // keepScroll なし = 別のフォルダ
        list.ReleaseLeft();

        Assert.Empty(list.State.Marks);
    }
}
