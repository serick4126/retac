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
        list.RaiseMouseDown(Mouse(MouseButtons.Left, FirstItem(list)));
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
}
