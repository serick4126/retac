using ReTAC.App;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace ReTAC.Domain.Tests;

public class CentralDisplayAreaTests
{
    [Fact]
    public void 通常幅では保存幅をそのまま表示する()
    {
        Assert.Equal(new CentralDisplayWidths(280, 280),
            CentralDisplayArea.DecideWidths(1000, 280, 160, 320, 4));
    }

    [Fact]
    public void 保存幅が最小値より小さければ最小値へ直す()
    {
        Assert.Equal(new CentralDisplayWidths(160, 160),
            CentralDisplayArea.DecideWidths(1000, 100, 160, 320, 4));
    }

    [Fact]
    public void 保存幅より右側の最小幅を優先する()
    {
        Assert.Equal(new CentralDisplayWidths(376, 500),
            CentralDisplayArea.DecideWidths(700, 500, 160, 320, 4));
    }

    [Fact]
    public void 両方の最小幅を満たせなければ左側だけ一時縮小する()
    {
        Assert.Equal(new CentralDisplayWidths(126, 280),
            CentralDisplayArea.DecideWidths(450, 280, 160, 320, 4));
    }

    [Fact]
    public void 広げ直すと一時縮小前の保存幅へ戻る()
    {
        var narrow = CentralDisplayArea.DecideWidths(450, 280, 160, 320, 4);
        var restored = CentralDisplayArea.DecideWidths(1000, narrow.PreservedLeft, 160, 320, 4);

        Assert.Equal(new CentralDisplayWidths(280, 280), restored);
    }

    [Theory]
    [InlineData(604, 280)]
    [InlineData(603, 279)]
    public void 右側の最小幅を求めるときsplitter幅も引く(int totalWidth, int visibleLeft)
    {
        Assert.Equal(new CentralDisplayWidths(visibleLeft, 280),
            CentralDisplayArea.DecideWidths(totalWidth, 280, 160, 320, 4));
    }

    [Fact]
    public void 左パネル幅を96Dpi論理値と画面幅の間で変換する()
    {
        Assert.Equal(420, CentralDisplayArea.ToDeviceWidth(280, 144));
        Assert.Equal(280, CentralDisplayArea.ToLogicalWidth(420, 144));
    }

    [Fact]
    public void ファイル表示パネルはリストと検索バーだけを右側に収容する()
    {
        var list = new FileListView();
        var search = new IncrementalSearchBar(list);
        using var panel = new FileDisplayPanel(list, search) { Size = new Size(640, 480) };
        search.Visible = true;
        panel.PerformLayout();

        Assert.Same(list, panel.CurrentView);
        Assert.Same(search, panel.SearchBar);
        Assert.Same(panel, list.Parent);
        Assert.Same(panel, search.Parent);
        Assert.Equal(DockStyle.Fill, list.Dock);
        Assert.Equal(DockStyle.Bottom, search.Dock);
        Assert.Equal(panel.ClientSize.Width, search.Width);
        Assert.Equal(panel.ClientSize.Height, search.Bottom);
        Assert.Equal(search.Top, list.Bottom);
    }

    [Fact]
    public void ウィンドウの一時縮小では保存幅を変えず広げると復元する()
    {
        var list = new FileListView();
        var search = new IncrementalSearchBar(list);
        var fileDisplay = new FileDisplayPanel(list, search);
        using var area = new CentralDisplayArea(fileDisplay, 280) { Size = new Size(1000, 500) };
        var left = new Panel();
        var notifications = 0;
        area.LeftWidthChanged += (_, _) => notifications++;

        area.ShowLeft(left);
        area.PerformLayout();
        area.SetSavedLeftWidth(300);
        area.SetSavedLeftWidth(280);
        area.Size = new Size(450, 500);
        area.PerformLayout();
        area.Size = new Size(1000, 500);
        area.PerformLayout();

        Assert.True(area.LeftPanelVisible);
        Assert.Equal(280, area.SavedLeftWidth);
        Assert.Equal(0, notifications);
        Assert.Same(left, area.LeftContent);
        Assert.Same(fileDisplay, search.Parent);

        area.HideLeft();
        area.PerformLayout();
        Assert.False(area.LeftPanelVisible);
        var split = area.Controls.OfType<SplitContainer>().Single();
        Assert.Equal(split.ClientSize.Width, split.Panel2.ClientSize.Width);
    }

    [Fact]
    public void 利用者の境界ドラッグだけが論理幅を一度通知する()
    {
        var list = new FileListView();
        var search = new IncrementalSearchBar(list);
        var fileDisplay = new FileDisplayPanel(list, search);
        using var area = new CentralDisplayArea(fileDisplay, 280) { Size = new Size(1000, 500) };
        area.ShowLeft(new Panel());
        area.PerformLayout();
        var notified = new List<int>();
        area.LeftWidthChanged += (_, width) => notified.Add(width);

        var split = area.Controls.OfType<SplitContainer>().Single();
        var target = CentralDisplayArea.ToDeviceWidth(240, area.DeviceDpi);
        var moving = new SplitterCancelEventArgs(0, 0, target, 0);
        Invoke(area, "OnSplitterMoving", split, moving);
        split.SplitterDistance = moving.SplitX;
        if (notified.Count == 0)
            Invoke(area, "OnSplitterMoved", split, new SplitterEventArgs(0, 0, moving.SplitX, 0));

        Assert.Equal([240], notified);
    }

    // R-96: SplitContainer はドラッグ中に SplitX を書き換えても読み返さないので、離した時点で最小幅へ戻す
    [Theory]
    [InlineData(0, 160)]
    [InlineData(1000, 1000 - 320 - 4)]
    public void 境界を最小幅を越えて動かすと最小幅で止まる(int dragLogical, int expectedLogical)
    {
        var list = new FileListView();
        var search = new IncrementalSearchBar(list);
        var fileDisplay = new FileDisplayPanel(list, search);
        using var area = new CentralDisplayArea(fileDisplay, 280) { Size = new Size(1000, 500) };
        area.ShowLeft(new Panel());
        area.PerformLayout();
        var split = area.Controls.OfType<SplitContainer>().Single();
        var dpi = area.DeviceDpi;
        area.Size = new Size(CentralDisplayArea.ToDeviceWidth(1000, dpi), 500);
        var notified = new List<int>();
        area.LeftWidthChanged += (_, width) => notified.Add(width);

        var drag = Math.Min(CentralDisplayArea.ToDeviceWidth(dragLogical, dpi), split.ClientSize.Width - split.SplitterWidth);
        Invoke(area, "OnSplitterMoving", split, new SplitterCancelEventArgs(0, 0, drag, 0));
        split.SplitterDistance = drag;
        if (notified.Count == 0)
            Invoke(area, "OnSplitterMoved", split, new SplitterEventArgs(0, 0, drag, 0));

        Assert.Equal(CentralDisplayArea.ToDeviceWidth(expectedLogical, dpi), split.SplitterDistance, tolerance: 1);
        Assert.Equal([expectedLogical], notified);
    }

    private static void Invoke(CentralDisplayArea area, string method, params object[] arguments)

    {
        typeof(CentralDisplayArea).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(area, arguments);
    }
}
