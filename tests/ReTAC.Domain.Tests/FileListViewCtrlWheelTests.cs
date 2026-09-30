using System.Drawing;
using System.Windows.Forms;
using ReTAC.App;
using ReTAC.Domain.Listing;
using SortOrder = ReTAC.Domain.Listing.SortOrder;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-128: Ctrl+ホイールでのビューの切り替えは、設定でオフにできる。オフのとき Ctrl+ホイールは何もしない
/// （ビューを切り替えず、スクロールもしない。回した分もためない）。ハンドルは作らない。
/// </summary>
public class FileListViewCtrlWheelTests
{
    private static readonly int Notch = SystemInformation.MouseWheelScrollDelta;

    private static (FileListView List, List<int> Switched) View(bool ctrlWheel)
    {
        var list = new FileListView { Size = new Size(600, 300) };
        list.SetView(FileViewMode.Details, Views(ctrlWheel), new Dictionary<string, int?>(), SortOrder.Default);
        list.SetEntries(Enumerable.Range(0, 300).Select(i => TestEntries.File($"f{i:D3}.txt")).ToList());
        var switched = new List<int>();
        list.ViewModeWheel += (_, notches) => switched.Add(notches);
        return (list, switched);
    }

    private static FileViewSettings Views(bool ctrlWheel) => new() { Common = new() { CtrlWheelSwitchesView = ctrlWheel } };

    [Fact]
    public void オンならCtrlホイールでビューの切り替えを知らせ_スクロールはしない()
    {
        var (list, switched) = View(ctrlWheel: true);
        using var _ = list;

        list.Wheel(-Notch, ctrl: true);

        Assert.Single(switched);
        Assert.Equal(default, list.ScrollPosition);
    }

    [Fact]
    public void オフならCtrlホイールは何もしない()
    {
        var (list, switched) = View(ctrlWheel: false);
        using var _ = list;

        list.Wheel(-Notch, ctrl: true);
        list.Wheel(-Notch * 3, ctrl: true);

        Assert.Empty(switched);
        Assert.Equal(default, list.ScrollPosition);   // スクロールにもしない
    }

    [Fact]
    public void オフにしたら_それまでに回した分を捨てる()
    {
        var (list, switched) = View(ctrlWheel: true);
        using var _ = list;
        list.Wheel(-Notch / 2, ctrl: true);   // ためる。まだ知らせない

        list.SetView(FileViewMode.Details, Views(ctrlWheel: false), new Dictionary<string, int?>(), SortOrder.Default);
        list.Wheel(-Notch / 2, ctrl: true);   // オフ。何も起きず、ためた分を捨てる

        list.SetView(FileViewMode.Details, Views(ctrlWheel: true), new Dictionary<string, int?>(), SortOrder.Default);
        list.Wheel(-Notch / 2, ctrl: true);   // 捨てていなければ、ここで 1 ノッチになる

        Assert.Empty(switched);
    }

    [Fact]
    public void オフでもCtrlなしのホイールはスクロールする()
    {
        var (list, switched) = View(ctrlWheel: false);
        using var _ = list;

        list.Wheel(-Notch, ctrl: false);

        Assert.Empty(switched);
        Assert.NotEqual(default, list.ScrollPosition);
    }
}
