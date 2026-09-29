using System.Drawing;
using System.Windows.Forms;
using ReTAC.App;
using ReTAC.Domain.Entries;
using ReTAC.Domain.Listing;
using SortOrder = ReTAC.Domain.Listing.SortOrder;

namespace ReTAC.Domain.Tests;

/// <summary>R-76 / R-116 / R-120: ホイールの段・「..」の行・ホバーの追従。ハンドルは作らない。</summary>
public class FileListViewInputTests
{
    private static FileListView View(FileViewMode mode, FileViewSettings? views = null, IReadOnlyList<Entry>? entries = null)
    {
        var list = new FileListView { Size = new Size(600, 400) };
        list.SetView(mode, views ?? new FileViewSettings(), new Dictionary<string, int?>(), SortOrder.Default);
        list.SetEntries(entries ?? Enumerable.Range(0, 200).Select(i => TestEntries.File($"file{i:D3}.txt")).ToList());
        return list;
    }

    [Fact]
    public void 格子のホイールは1ノッチで1行だけ進む()
    {
        using var list = View(FileViewMode.LargeIcons);
        list.ScrollWheel(-1);
        Assert.Equal(1, list.ScrollPosition.Y);
    }

    [Fact]
    public void 詳細のホイールはこれまでどおりホイールの行数だけ進む()
    {
        using var list = View(FileViewMode.Details);
        list.ScrollWheel(-1);
        Assert.Equal(Math.Max(1, SystemInformation.MouseWheelScrollLines), list.ScrollPosition.Y);
    }

    [Fact]
    public void 親フォルダの行にはチェックボックスを常に表示でも出さない()
    {
        var views = new FileViewSettings { Icons = new() { CheckBoxes = CheckBoxMode.Always } };
        using var list = View(FileViewMode.MediumIcons, views, [TestEntries.Parent(), TestEntries.File("a.txt")]);
        Assert.False(list.ShowsCheckBox(0));
        Assert.True(list.ShowsCheckBox(1));
    }

    [Fact]
    public void 投げ縄は親フォルダの行を仮のマークにしない()
    {
        using var list = View(FileViewMode.MediumIcons, null, [TestEntries.Parent(), TestEntries.File("a.txt")]);
        var box = ToRect(list.Layout.ItemBounds(0));
        list.RaiseMouseDown(new MouseEventArgs(MouseButtons.Left, 1, 590, 390, 0));
        list.RaiseMouseMove(new MouseEventArgs(MouseButtons.Left, 1, box.X, box.Y, 0));
        Assert.True(list.LassoActive);
        Assert.False(list.IsMarkedForDisplay(0));
        Assert.True(list.IsMarkedForDisplay(1));
    }

    private static Rectangle ToRect((int X, int Y, int Width, int Height) b) => new(b.X, b.Y, b.Width, b.Height);

    [Fact]
    public void ハンドルが無ければホバーは項目に付かない()
    {
        using var list = View(FileViewMode.MediumIcons);
        list.RefreshHot();
        Assert.Equal(-1, list.HotIndex);
        list.ScrollWheel(-1);
        Assert.Equal(-1, list.HotIndex);
    }

    [Fact]
    public void スクロールのあとマウスの位置の項目へホバーを付け直す()
    {
        using var list = View(FileViewMode.MediumIcons);
        var point = new Point(10, 10);
        list.RefreshHot(point);
        Assert.Equal(0, list.HotIndex);
        list.ScrollWheel(-1);                 // 手前の行が消え、同じ位置の項目が変わる
        list.RefreshHot(point);
        Assert.Equal(list.Layout.IndexAt(10 + list.Layout.ScrollOffset(list.ScrollPosition).X,
            10 + list.Layout.ScrollOffset(list.ScrollPosition).Y, 200), list.HotIndex);
        Assert.NotEqual(0, list.HotIndex);
    }

    [Fact]
    public void 項目の外の点ではホバーが外れる()
    {
        using var list = View(FileViewMode.MediumIcons, null, [TestEntries.File("a.txt")]);
        list.RefreshHot(new Point(10, 10));
        Assert.Equal(0, list.HotIndex);
        list.RefreshHot(new Point(590, 390));
        Assert.Equal(-1, list.HotIndex);
    }

    [Fact]
    public void 一覧から詳細へ切り替えても画像の世代は進まない()
    {
        using var list = View(FileViewMode.List);
        var generation = list.ImageGeneration;
        list.SetView(FileViewMode.Details, new FileViewSettings(), new Dictionary<string, int?>(), SortOrder.Default);
        Assert.Equal(generation, list.ImageGeneration);
    }
}
