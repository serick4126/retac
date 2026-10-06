using ReTAC.App;
using ReTAC.Domain.Entries;
using ReTAC.Shell;

namespace ReTAC.Domain.Tests;

/// <summary>R-141: 「新しいReTACで開く」の出す条件・位置・代わりの表示。メニューもハンドルも作らない。</summary>
public class OpenInNewWindowTests
{
    [Fact]
    public void InsertPosition_AfterOpen() =>
        Assert.Equal(3, ShellContextMenu.InsertPosition([null, null, "open", "explore"], 1));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 3)]
    public void InsertPosition_NoOpen(int appItems, int expected) =>
        Assert.Equal(expected, ShellContextMenu.InsertPosition([null, "explore", "properties"], appItems));

    [Fact]
    public void InsertPosition_CaseInsensitive() =>
        Assert.Equal(2, ShellContextMenu.InsertPosition([null, "Open", "explore"], 0));

    [Fact]
    public void OpenInNewWindowApplies_Table()
    {
        var folder = TestEntries.Folder("a");
        var file = TestEntries.File("b.txt");
        Assert.True(MainForm.OpenInNewWindowApplies([folder]));
        Assert.False(MainForm.OpenInNewWindowApplies([file]));
        Assert.False(MainForm.OpenInNewWindowApplies([folder, folder]));
        Assert.False(MainForm.OpenInNewWindowApplies([]));
        Assert.False(MainForm.OpenInNewWindowApplies([TestEntries.Parent()]));
    }

    [Fact]
    public void Fallback_KeepsAfterOpen()
    {
        Assert.Equal(["x", "", "新規"], ShellContextMenu.FallbackItems(["x", ""], "新規"));
        Assert.Equal(["新規"], ShellContextMenu.FallbackItems([], "新規"));
        Assert.Equal(["x"], ShellContextMenu.FallbackItems(["x"], null));
    }

    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    public void ChooseMenuPath_AllFailuresFallBack(bool parsed, bool ui, bool isMenu, bool queried) =>
        Assert.Equal(ShellContextMenu.MenuPath.Fallback, ShellContextMenu.ChooseMenuPath(parsed, ui, isMenu, queried));

    [Fact]
    public void ChooseMenuPath_AllSuccessIsShell() =>
        Assert.Equal(ShellContextMenu.MenuPath.Shell, ShellContextMenu.ChooseMenuPath(true, true, true, true));

    [Fact]
    public void QueryFailure_FallsBackWithAfterOpen()
    {
        // 0x8000 + items.Count（ReTAC の項目の続き）を「選んだ」ことにする
        var result = ShellContextMenu.TrackAndInvoke(IntPtr.Zero, null, 0, 0, null, ["a", "b"], null, "新規",
            query: _ => false, track: _ => 0x8000u + 2);
        Assert.Equal(new ContextMenuResult(ContextMenuOutcome.AppItem, 2), result);
    }
}
