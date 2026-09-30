using System.Drawing;
using System.Windows.Forms;
using ReTAC.App;
using ReTAC.Domain.Listing;
using SortOrder = ReTAC.Domain.Listing.SortOrder;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-120: 投げ縄はすべてのモードで。離した時点で確定。Esc・右ボタン・フォーカスの喪失・キャプチャの喪失・一覧の入れ替わりで取り消す。
/// どのテストもハンドルを作らない（最後に IsHandleCreated が false のままであることを確かめる）。
/// </summary>
public class FileListViewLassoTests
{
    private static FileListView View(FileViewMode mode, int count = 5)
    {
        var list = new FileListView { Size = new Size(600, 400) };
        list.SetView(mode, new FileViewSettings(), new Dictionary<string, int?>(), SortOrder.Default);
        list.SetEntries(Enumerable.Range(0, count).Select(i => TestEntries.File($"file{i:D2}.txt")).ToList());
        return list;
    }

    /// <summary>最後の項目より右下の、項目の無い所（どのモードでも空いている）。</summary>
    private static Point Empty(FileListView list)
    {
        var last = FileViewScroll.VisibleBounds(list.Layout, list.ScrollPosition, list.State.Count - 1);
        return new Point(Math.Min(590, last.X + last.Width + 2), Math.Min(390, last.Y + last.Height + 2));
    }

    private static Point FirstItem(FileListView list)
    {
        var first = FileViewScroll.VisibleBounds(list.Layout, list.ScrollPosition, 0);
        return new Point(first.X + 1, first.Y + 1);
    }

    private static MouseEventArgs Mouse(MouseButtons button, Point p) => new(button, 1, p.X, p.Y, 0);

    /// <summary>
    /// 空いた所で押して、最初の項目まで引いた状態（閾値は超えている）。実際の OnMouseDown / OnMouseMove を通す。
    /// Ctrl は ModifierKeys（実際のキーボード）なので作れない。Ctrl のテストだけ LassoPress を直接呼ぶ。
    /// </summary>
    private static void Drag(FileListView list)
    {
        list.RaiseMouseDown(Mouse(MouseButtons.Left, Empty(list)));
        list.RaiseMouseMove(Mouse(MouseButtons.Left, FirstItem(list)));
    }

    [Theory]
    [InlineData(FileViewMode.MediumIcons)]
    [InlineData(FileViewMode.SmallIcons)]
    [InlineData(FileViewMode.List)]
    [InlineData(FileViewMode.Details)]
    public void 空いた所からのドラッグで囲んだ項目を離した時点でマークする(FileViewMode mode)
    {
        using var list = View(mode);
        Drag(list);
        Assert.True(list.LassoActive);
        Assert.Empty(list.State.Marks);            // ドラッグ中は変えない
        Assert.True(list.IsMarkedForDisplay(0));   // 仮の強調
        list.RaiseMouseUp(Mouse(MouseButtons.Left, FirstItem(list)));   // OnMouseUp から確定へ届く
        Assert.Contains(0, list.State.Marks);
        Assert.False(list.LassoActive);
        Assert.False(list.IsHandleCreated);
    }

    [Fact]
    public void 閾値の手前で離したらマークしない()
    {
        using var list = View(FileViewMode.MediumIcons);
        var start = Empty(list);
        list.RaiseMouseDown(Mouse(MouseButtons.Left, start));
        list.RaiseMouseMove(Mouse(MouseButtons.Left, start with { X = start.X - 1 }));
        Assert.False(list.LassoActive);
        list.RaiseMouseUp(Mouse(MouseButtons.Left, start));
        Assert.Empty(list.State.Marks);
    }

    [Fact]
    public void Escで取り消す()
    {
        using var list = View(FileViewMode.MediumIcons);
        Drag(list);
        list.PressKey(System.Windows.Forms.Keys.Escape);
        Assert.False(list.LassoActive);
        list.LassoRelease();
        Assert.Empty(list.State.Marks);
        Assert.False(list.IsMarkedForDisplay(0));
    }

    [Fact]
    public void 右ボタンで取り消す()
    {
        using var list = View(FileViewMode.MediumIcons);
        Drag(list);
        list.RaiseMouseDown(Mouse(MouseButtons.Right, Empty(list)));   // OnMouseDown の右ボタンから取り消しへ届く
        Assert.False(list.LassoActive);
        list.RaiseMouseUp(Mouse(MouseButtons.Left, FirstItem(list)));
        Assert.Empty(list.State.Marks);
    }

    [Fact]
    public void フォーカスが外れたら取り消す()
    {
        using var list = View(FileViewMode.MediumIcons);
        Drag(list);
        list.RaiseLostFocus();                     // OnLostFocus から
        Assert.False(list.LassoActive);
    }

    [Fact]
    public void キャプチャを失ったら取り消す()
    {
        using var list = View(FileViewMode.MediumIcons);
        Drag(list);
        list.RaiseCaptureChanged();                // OnMouseCaptureChanged から
        Assert.False(list.LassoActive);
        Assert.False(list.IsHandleCreated);
    }

    [Fact]
    public void 自動スクロールの後は矩形が伸びる()
    {
        using var list = View(FileViewMode.LargeIcons, 200);
        var bottom = new Point(10, 395);                  // 下の端（自動スクロールの帯の中）
        list.RaiseMouseDown(Mouse(MouseButtons.Left, new Point(1, 1)));   // 外周の余白
        list.RaiseMouseMove(Mouse(MouseButtons.Left, bottom));
        var before = list.LassoRect;
        list.AutoScrollTick();
        Assert.True(list.ScrollPosition.Y > 0);
        Assert.True(list.LassoRect.Height > before.Height);
    }

    [Fact]
    public void Ctrlで始めたら解除し_途中の修飾キーでは変わらない()
    {
        using var list = View(FileViewMode.MediumIcons);
        list.State.ToggleMark(0);
        list.LassoPress(Empty(list), ctrl: true);  // Ctrl は ModifierKeys から作れないので、押した時点の値を直接渡す
        list.RaiseMouseMove(Mouse(MouseButtons.Left, FirstItem(list)));
        Assert.False(list.IsMarkedForDisplay(0));  // 仮に外れて見える
        list.RaiseMouseMove(Mouse(MouseButtons.Left, FirstItem(list)));   // 途中の移動は修飾キーを受け取らない
        list.RaiseMouseUp(Mouse(MouseButtons.Left, FirstItem(list)));
        Assert.DoesNotContain(0, list.State.Marks);
    }

    [Fact]
    public void 一覧が入れ替わったら投げ縄を取り消す()
    {
        using var list = View(FileViewMode.MediumIcons);
        Drag(list);
        list.SetEntries(Enumerable.Range(0, 5).Select(i => TestEntries.File($"new{i}.txt")).ToList());
        Assert.False(list.LassoActive);
        list.RaiseMouseUp(Mouse(MouseButtons.Left, FirstItem(list)));
        Assert.Empty(list.State.Marks);
    }

    private static void SwitchToDetails(FileListView list) =>
        list.SetView(FileViewMode.Details, new FileViewSettings(), new Dictionary<string, int?>(), SortOrder.Default);

    [Fact]
    public void 表示モードが変わったら投げ縄を取り消す_離しても別の配置の項目をマークしない()
    {
        using var list = View(FileViewMode.MediumIcons);
        Drag(list);
        SwitchToDetails(list);
        Assert.False(list.LassoActive);
        list.RaiseMouseUp(Mouse(MouseButtons.Left, FirstItem(list)));
        Assert.Empty(list.State.Marks);
        Assert.False(list.IsHandleCreated);
    }

    [Fact]
    public void Ctrlホイールで表示モードが変わったら投げ縄を取り消す()
    {
        using var list = View(FileViewMode.MediumIcons);
        // MainForm と同じく、届いた段でモードを進める（Ctrl は ModifierKeys なので、OnMouseWheel の手前の ModeWheel から届ける）
        list.ViewModeWheel += (_, notches) => list.SetView(FileViewModes.Step(FileViewMode.MediumIcons, notches),
            new FileViewSettings(), new Dictionary<string, int?>(), SortOrder.Default);
        Drag(list);
        list.ModeWheel(SystemInformation.MouseWheelScrollDelta);
        Assert.False(list.LassoActive);
        list.RaiseMouseUp(Mouse(MouseButtons.Left, FirstItem(list)));
        Assert.Empty(list.State.Marks);
    }

    /// <summary>右下から探した、投げ縄を始められる所（項目の無い所。詳細表示では名前以外も）。</summary>
    private static Point PressPoint(FileListView list)
    {
        var (ox, oy) = list.Layout.ScrollOffset(list.ScrollPosition);
        for (var y = 375; y > list.Layout.HeaderHeight; y -= 3)
            for (var x = 595; x > 0; x -= 5)
            {
                var (index, area) = list.Layout.HitTest(x + ox, y - list.Layout.HeaderHeight + oy, list.State.Count);
                if (index < 0 || area == FileViewArea.Other) return new Point(x, y);
            }
        throw new InvalidOperationException("投げ縄を始められる所が無い");
    }

    /// <summary>投げ縄を始められる所で押して中ほどまで引き、scroll で中身をずらして離す。マークされた項目の数を返す（scroll が null ならずらさない）。</summary>
    private static (int Marked, (int X, int Y, int Width, int Height) Rect, FileListView List) ScrollDuringLasso(FileViewMode mode, Point end, Action<FileListView>? scroll)
    {
        var list = View(mode, 300);
        list.RaiseMouseDown(Mouse(MouseButtons.Left, PressPoint(list)));
        list.RaiseMouseMove(Mouse(MouseButtons.Left, end));
        Assert.True(list.LassoActive);
        scroll?.Invoke(list);
        var rect = list.LassoRect;
        list.RaiseMouseUp(Mouse(MouseButtons.Left, end));
        Assert.False(list.LassoActive);
        Assert.False(list.IsHandleCreated);
        return (list.State.Marks.Count, rect, list);
    }

    [Theory]
    [InlineData(FileViewMode.LargeIcons, "wheel")]
    [InlineData(FileViewMode.LargeIcons, "vbar")]
    [InlineData(FileViewMode.LargeIcons, "tick")]
    [InlineData(FileViewMode.List, "wheel")]
    [InlineData(FileViewMode.List, "hbar")]
    [InlineData(FileViewMode.List, "tick")]
    [InlineData(FileViewMode.Details, "wheel")]
    [InlineData(FileViewMode.Details, "vbar")]
    public void スクロールしたら投げ縄の矩形が伸び_離すと今の矩形の項目をマークする(FileViewMode mode, string how)
    {
        // 自動スクロールは、その向きの端（帯の中）にマウスがあるときだけ動く。他は帯の外の中ほど
        var end = how != "tick" ? new Point(300, 200) : mode == FileViewMode.List ? new Point(595, 200) : new Point(300, 395);
        var (baseline, baseRect, baseList) = ScrollDuringLasso(mode, end, null);
        baseList.Dispose();
        Action<FileListView> scroll = how switch
        {
            "wheel" => l => l.ScrollWheel(-3),
            "vbar" => l => l.RaiseScrollBar(vertical: true, 3),
            "hbar" => l => l.RaiseScrollBar(vertical: false, 3),
            _ => l => l.AutoScrollTick(),
        };
        var (marked, rect, list) = ScrollDuringLasso(mode, end, l =>
        {
            scroll(l);
            Assert.True(l.ScrollPosition.X > 0 || l.ScrollPosition.Y > 0);
        });
        list.Dispose();
        Assert.NotEqual(baseRect, rect);        // 押した点は中身に付いているので、ずれたぶん矩形が変わる
        Assert.NotEqual(baseline, marked);      // 離すと、ずれたあとの矩形が囲む項目が対象になる
    }

    [Fact]
    public void スクロールバーで動かしたらホバーを付け直す()
    {
        using var list = View(FileViewMode.LargeIcons, 300);
        list.HotIndex = 2;
        list.RaiseScrollBar(vertical: true, 3);
        Assert.Equal(-1, list.HotIndex);   // ハンドルが無ければ RefreshHot は外す。Scroll から RefreshHot へ届いた印
        Assert.False(list.IsHandleCreated);
    }

    [Fact]
    public void 項目の上から始めたら投げ縄にならない()
    {
        using var list = View(FileViewMode.MediumIcons);
        // 先頭を親フォルダの行にする。普通の項目だと実際の D&D（OLE。STA が要る）が始まってしまう
        list.SetEntries([TestEntries.Parent(), .. Enumerable.Range(0, 4).Select(i => TestEntries.File($"file{i}.txt"))]);
        // R-120: 項目の余白（アイコン・名前の文字に当たらない所）は投げ縄の起点なので、アイコンの真ん中を押す
        var icon = FileViewScroll.ToVisible(list.Layout, list.ScrollPosition, list.Layout.IconBounds(0));
        list.RaiseMouseDown(Mouse(MouseButtons.Left, new Point(icon.X + icon.Width / 2, icon.Y + icon.Height / 2)));
        list.RaiseMouseMove(Mouse(MouseButtons.Left, Empty(list)));
        Assert.False(list.LassoActive);
    }

    [Fact]
    public void 一覧が入れ替わったらホバーの添字を捨てる()
    {
        using var list = View(FileViewMode.MediumIcons);
        list.HotIndex = 3;
        list.SetEntries(Enumerable.Range(0, 2).Select(i => TestEntries.File($"new{i}.txt")).ToList());
        Assert.Equal(-1, list.HotIndex);
    }

    [Fact]
    public void 投げ縄の自動スクロールは帯の深さで速くなり_D_Dは0_4秒のまま()
    {
        using var list = View(FileViewMode.LargeIcons, 200);
        var band = list.Layout.AutoScrollBand;
        list.RaiseMouseDown(Mouse(MouseButtons.Left, new Point(1, 1)));
        list.RaiseMouseMove(Mouse(MouseButtons.Left, new Point(10, 395)));
        Assert.Equal(FileViewScroll.LassoInterval(band, (0, 1), 10, 395, 600, 400), list.AutoScrollTimerInterval);
        Assert.InRange(list.AutoScrollTimerInterval, 31, 199);
        // 帯の 2 倍より深く（コントロールの外へ）引いたら、次の 1 段から最速
        list.RaiseMouseMove(Mouse(MouseButtons.Left, new Point(10, 400 + band * 3)));
        list.AutoScrollTick();
        Assert.Equal(FileViewScroll.LassoFastInterval, list.AutoScrollTimerInterval);
        list.RaiseMouseUp(Mouse(MouseButtons.Left, new Point(10, 500)));   // 終わったら止まる

        list.SetAutoScroll((0, 1));   // D&D の始め方（interval は渡さない）
        Assert.Equal(400, list.AutoScrollTimerInterval);
        list.SetAutoScroll((0, 0));
        Assert.False(list.IsHandleCreated);
    }

    // ---- Phase 16 から持ち越した点の固定（Phase 18） ----

    [Fact]
    public void 投げ縄の最中にホイールで動かしても_仮のマークは今の矩形に交わる項目と一致する()
    {
        using var list = new FileListView { Size = new Size(600, 300) };
        list.SetView(FileViewMode.Details, new FileViewSettings(), new Dictionary<string, int?>(), SortOrder.Default);
        list.SetEntries(Enumerable.Range(0, 200).Select(i => TestEntries.File($"f{i:D3}.txt")).ToList());
        var (rowX, row, rowWidth, _) = list.Layout.ItemBounds(1);    // row は 1 行の高さ
        var x = Math.Min(rowX + rowWidth - 5, list.Size.Width - 60); // 行の右端の近く = 名前以外の列
        var start = new Point(x, list.Layout.HeaderHeight + row * 2 + 2);
        list.LassoPress(start, ctrl: false);
        list.PressLeft(start, shift: false);
        list.RaiseMouseMove(new MouseEventArgs(MouseButtons.Left, 1, x - 30, list.Layout.HeaderHeight + row * 5 + 2, 0));
        Assert.True(list.LassoActive);

        // スクロールの前に仮のマークを一度作らせる（キャッシュ。AfterScroll が捨てないと古いまま残る）
        var (bx, by, bw, bh) = list.LassoRect;
        var before = list.Layout.IndexesIn(bx, by, bw, bh, list.State.Count).ToHashSet();
        for (var i = 0; i < list.State.Count; i++) Assert.Equal(before.Contains(i), list.IsMarkedForDisplay(i));

        list.ScrollWheel(-2);   // 中身がずれる。マウスは動かない

        var (rx, ry, rw, rh) = list.LassoRect;
        var covered = list.Layout.IndexesIn(rx, ry, rw, rh, list.State.Count).ToHashSet();
        Assert.True(covered.Count > 4);   // スクロールした分だけ矩形が伸びている
        for (var i = 0; i < list.State.Count; i++) Assert.Equal(covered.Contains(i), list.IsMarkedForDisplay(i));
    }

    [Fact]
    public void 名前以外から始めた投げ縄を離したあと_次に名前を押して動かすと_その項目のドラッグが始まる()
    {
        using var list = new FileListView { Size = new Size(600, 300) };
        list.SetView(FileViewMode.Details, new FileViewSettings(), new Dictionary<string, int?>(), SortOrder.Default);
        list.SetEntries(Enumerable.Range(0, 20).Select(i => TestEntries.File($"f{i:D3}.txt")).ToList());
        IReadOnlyList<string>? started = null;
        list.StartDragOverride = paths => started = paths;
        var (rowX, row, rowWidth, _) = list.Layout.ItemBounds(1);
        var x = Math.Min(rowX + rowWidth - 5, list.Size.Width - 60);
        var start = new Point(x, list.Layout.HeaderHeight + row * 2 + 2);
        list.LassoPress(start, ctrl: false);
        list.PressLeft(start, shift: false);
        Assert.True(list.PendingDragIndex >= 0);   // 名前以外の行の上で押した = D&D の始まりの候補が付いている
        var end = new Point(x - 30, list.Layout.HeaderHeight + row * 4 + 2);
        list.RaiseMouseMove(new MouseEventArgs(MouseButtons.Left, 1, end.X, end.Y, 0));
        list.RaiseMouseUp(new MouseEventArgs(MouseButtons.Left, 1, end.X, end.Y, 0));
        Assert.False(list.LassoActive);
        Assert.Null(started);
        // 離した時点で押した状態が消えている（次の MouseMove の分岐に頼らない）
        Assert.Equal(-1, list.PendingDragIndex);

        // 離した後に動かしても、前の投げ縄の状態でドラッグは始まらない
        list.RaiseMouseMove(new MouseEventArgs(MouseButtons.Left, 1, end.X + 80, end.Y, 0));
        Assert.Null(started);

        // 名前を押して動かすと、その項目（7 番）のドラッグが始まる（前の「名前以外」の状態が効いていない）。
        // 投げ縄で付いたマークは消しておく（マークがあると、ドラッグの対象がマークしたものになる）
        list.State.ClearMarks();
        var name = FileViewScroll.ToVisible(list.Layout, list.ScrollPosition, list.Layout.NameBounds(7));
        var press = new Point(name.X + 2, name.Y + 2);
        list.LassoPress(press, ctrl: false);
        list.PressLeft(press, shift: false);
        list.RaiseMouseMove(new MouseEventArgs(MouseButtons.Left, 1, press.X + 60, press.Y, 0));
        Assert.Equal([@"C:\work\f007.txt"], started);
    }
}
