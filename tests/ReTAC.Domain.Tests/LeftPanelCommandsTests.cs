using ReTAC.App;
using ReTAC.Domain.Commands;

namespace ReTAC.Domain.Tests;

/// <summary>R-96-2: 5コマンドから次のウィンドウ状態を決める純粋な判定。副作用（保存・フォーカス）はMainForm側。</summary>
public class LeftPanelCommandsTests
{
    [Fact]
    public void 非表示から個別表示コマンドで表示しそのビューへ切り替える()
    {
        var result = LeftPanelCommands.Apply(CommandId.ShowPreview, shown: false, view: LeftPanelViewKind.DriveTree);

        Assert.True(result.Shown);
        Assert.Equal(LeftPanelViewKind.Preview, result.View);
        Assert.True(result.Changed);
    }

    [Fact]
    public void 表示中に別ビューの個別表示コマンドでビューだけ切り替える()
    {
        var result = LeftPanelCommands.Apply(CommandId.ShowBookmarksView, shown: true, view: LeftPanelViewKind.DriveTree);

        Assert.True(result.Shown);
        Assert.Equal(LeftPanelViewKind.Bookmarks, result.View);
        Assert.True(result.Changed);
    }

    [Fact]
    public void 表示中に同じビューの個別表示コマンドは何もしない()
    {
        var result = LeftPanelCommands.Apply(CommandId.ShowDriveTree, shown: true, view: LeftPanelViewKind.DriveTree);

        Assert.False(result.Changed);
        Assert.True(result.Shown);
        Assert.Equal(LeftPanelViewKind.DriveTree, result.View);
    }

    [Fact]
    public void トグルは表示中なら隠しビューは変えない()
    {
        var result = LeftPanelCommands.Apply(CommandId.ToggleLeftPanel, shown: true, view: LeftPanelViewKind.Preview);

        Assert.False(result.Shown);
        Assert.Equal(LeftPanelViewKind.Preview, result.View);
        Assert.True(result.Changed);
    }

    [Fact]
    public void トグルは非表示なら最後のビューのまま表示する()
    {
        var result = LeftPanelCommands.Apply(CommandId.ToggleLeftPanel, shown: false, view: LeftPanelViewKind.Bookmarks);

        Assert.True(result.Shown);
        Assert.Equal(LeftPanelViewKind.Bookmarks, result.View);
        Assert.True(result.Changed);
    }

    [Theory]
    [InlineData(CommandId.ShowDriveTree, LeftPanelViewKind.DriveTree)]
    [InlineData(CommandId.ShowDesktopTree, LeftPanelViewKind.DesktopTree)]
    [InlineData(CommandId.ShowBookmarksView, LeftPanelViewKind.Bookmarks)]
    [InlineData(CommandId.ShowPreview, LeftPanelViewKind.Preview)]
    public void 個別表示コマンドは対応するビューを指す(CommandId command, LeftPanelViewKind expected)
    {
        var result = LeftPanelCommands.Apply(command, shown: false, view: LeftPanelViewKind.DriveTree);
        Assert.Equal(expected, result.View);
    }
}
