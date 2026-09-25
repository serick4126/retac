using System.Drawing;
using ReTAC.Shell;

namespace ReTAC.Domain.Tests;

public class NameSpaceTreePolicyTests
{
    [Theory]
    [InlineData(@"C:\a\b", @"C:\")]
    [InlineData(@"\\server\share\a\b", @"\\server\share")]
    public void 現在位置から唯一のルートを返す(string currentFolder, string expected)
    {
        Assert.Equal(expected, NameSpaceTreePolicy.RootOf(currentFolder));
    }

    [Theory]
    [InlineData(@"C:\", @"C:\a\b", true)]
    [InlineData(@"C:\a", @"c:\A\b\", true)]
    [InlineData(@"C:\a\", @"c:\A", true)]
    [InlineData(@"C:\foo", @"C:\foobar", false)]
    [InlineData(@"C:\a\b", @"C:\a", false)]
    [InlineData(@"D:\", @"C:\a", false)]
    [InlineData(@"\\server\share", @"\\SERVER\SHARE\a", true)]
    [InlineData(@"\\server\other", @"\\server\share\a", false)]
    public void 現在位置またはその祖先だけを判定する(string itemPath, string currentFolder, bool expected)
    {
        Assert.Equal(expected, NameSpaceTreePolicy.IsCurrentPathOrAncestor(itemPath, currentFolder));
    }

    [Theory]
    [InlineData(@"C:\", @"c:\", false)]
    [InlineData(@"\\server\share", @"\\SERVER\SHARE\", false)]
    [InlineData(@"C:\", @"D:\", true)]
    [InlineData(@"\\server\share", @"\\server\other", true)]
    public void ルートが変わる場合だけ作り直す(string oldRoot, string newRoot, bool expected)
    {
        Assert.Equal(expected, NameSpaceTreePolicy.MustRebuildRoot(oldRoot, newRoot));
    }

    // R-97 / Task2: デスクトップツリーは PC 全体で単一のルートを持ち、ドライブ/UNC共有が変わっても作り直さない。
    // ドライブツリーは現在のドライブ/UNC共有 1 つだけがルートなので、変わったら作り直す。
    [Theory]
    [InlineData(NameSpaceTreeRootKind.Drive, @"C:\a", @"D:\b", true)]
    [InlineData(NameSpaceTreeRootKind.Drive, @"C:\a", @"C:\b", false)]
    [InlineData(NameSpaceTreeRootKind.Desktop, @"C:\a", @"D:\b", false)]
    [InlineData(NameSpaceTreeRootKind.Desktop, @"C:\a", @"\\server\share\b", false)]
    public void 作り直しの要否はドライブツリーとデスクトップツリーで分かれる(
        NameSpaceTreeRootKind kind, string oldFolder, string newFolder, bool expected)
    {
        var oldRoot = NameSpaceTreePolicy.RootOf(kind, oldFolder);
        var newRoot = NameSpaceTreePolicy.RootOf(kind, newFolder);
        Assert.Equal(expected, NameSpaceTreePolicy.MustRebuildRoot(oldRoot, newRoot));
    }

    [Theory]
    [InlineData(@"C:\a\b", true)]
    [InlineData(@"\\server\share\a", false)]
    public void デスクトップツリーの自動選択はUNCのドライブ文字変換をしない(string currentFolder, bool expected)
    {
        Assert.Equal(expected, NameSpaceTreePolicy.CanAutoSelectInDesktopTree(currentFolder));
    }

    // R-97-2: マウスの確定は「名前・アイコンを、ドラッグへ移行せず離した」場合だけ。
    // 展開ボタン・右側余白は候補にしない。ダブルクリックの2回目は最初のクリックで確定済みなので確定し直さない。
    [Theory]
    [InlineData("名前", true, false, false, true, 0, 0, true)]
    [InlineData("アイコン", true, false, false, true, 0, 0, true)]
    [InlineData("展開ボタン", false, false, false, true, 0, 0, false)]
    [InlineData("右側余白", false, false, false, true, 0, 0, false)]
    [InlineData("ドラッグしきい値内", true, false, false, true, 1, 1, true)]
    [InlineData("ドラッグしきい値外", true, false, false, true, 100, 100, false)]
    [InlineData("ドラッグ開始", true, false, true, true, 0, 0, false)]
    [InlineData("ダブルクリック", true, true, false, true, 0, 0, false)]
    [InlineData("ボタンを離す前", true, false, false, false, 0, 0, false)]
    public void ツリーのクリックは名前アイコンを離した時だけ確定する(
        string _, bool onIconOrLabel, bool isDoubleClick, bool dragStarted, bool buttonReleased,
        int dx, int dy, bool expected)
    {
        var pending = new NameSpaceTreePolicy.PendingTreeClick(@"C:\a", new Point(0, 0), onIconOrLabel, isDoubleClick);
        var upPoint = new Point(dx, dy);
        Assert.Equal(expected, NameSpaceTreePolicy.ShouldCommit(pending, upPoint, dragStarted, buttonReleased));
    }

    [Theory]
    [InlineData("見張りの開始から期限内", 14_999, 0, 0, false)]
    [InlineData("1 段も進まず期限", 15_000, 0, 0, true)]
    [InlineData("前の選択の進みは数えない", 15_000, 0, -5_000, true)]
    [InlineData("進んでから期限内", 20_000, 0, 10_000, false)]
    [InlineData("進んでから期限", 25_000, 0, 10_000, true)]
    public void 展開の見張りの期限は最後に進んでから数える(
        string _, long now, long watchStarted, long lastProgress, bool expected)
    {
        Assert.Equal(expected, NameSpaceTreePolicy.SelectionTimedOut(now, watchStarted, lastProgress, 15_000));
    }

    [Theory]
    [InlineData("開いた", true, 1_000, 5_000, false, false)]
    [InlineData("命じていない", false, 0, 5_000, false, false)]
    [InlineData("間隔の手前", false, 1_000, 2_499, false, false)]
    [InlineData("間隔を過ぎても開かない", false, 1_000, 2_500, false, true)]
    [InlineData("命じ直しは 1 回だけ", false, 1_000, 2_500, true, false)]
    public void 開かない枝には間隔を空けて展開を命じ直す(
        string _, bool expanded, long requestedAt, long now, bool alreadyReissued, bool expected)
    {
        Assert.Equal(expected, NameSpaceTreePolicy.ShouldReissueExpand(expanded, requestedAt, now, 1_500, alreadyReissued));
    }
}
