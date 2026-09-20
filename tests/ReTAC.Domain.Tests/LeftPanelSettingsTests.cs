using System.Text.Json;
using ReTAC.App;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Keys;

namespace ReTAC.Domain.Tests;

public class LeftPanelSettingsTests
{
    private static readonly CommandId[] Commands =
    [
        CommandId.ShowDriveTree,
        CommandId.ShowDesktopTree,
        CommandId.ShowBookmarksView,
        CommandId.ShowPreview,
        CommandId.ToggleLeftPanel,
    ];

    [Fact]
    public void 左パネル設定は初回と欠けたJSONで既定値を使う()
    {
        AssertDefaults(new AppSettings());
        AssertDefaults(JsonSerializer.Deserialize<AppSettings>("{}")!);
    }

    [Fact]
    public void 左パネルビューの列挙順を固定する()
    {
        Assert.Equal(
            [LeftPanelViewKind.DriveTree, LeftPanelViewKind.DesktopTree, LeftPanelViewKind.Bookmarks, LeftPanelViewKind.Preview],
            Enum.GetValues<LeftPanelViewKind>());
    }

    [Fact]
    public void 左パネルコマンドのIDを固定する()
    {
        Assert.Equal([0x900C, 0x900D, 0x900E, 0x900F, 0x9010], Commands.Select(id => (int)id));
    }

    [Fact]
    public void 左パネルコマンドは既定キーを持たない()
    {
        var defaults = DefaultKeyMap.Create().Bindings.Values;
        Assert.All(Commands, id => Assert.DoesNotContain(new BuiltinTarget(id), defaults));
    }

    [Fact]
    public void 左パネルコマンドは表示分類と表示名を持つ()
    {
        var expected = new[]
        {
            (CommandId.ShowDriveTree, "ドライブツリーを表示"),
            (CommandId.ShowDesktopTree, "デスクトップツリーを表示"),
            (CommandId.ShowBookmarksView, "ブックマークを表示"),
            (CommandId.ShowPreview, "プレビューを表示"),
            (CommandId.ToggleLeftPanel, "左パネルの表示切り替え"),
        };

        Assert.All(expected, item =>
        {
            var row = CommandLabels.Grouped.Single(row => row.Command == item.Item1);
            Assert.Equal(("表示", item.Item2), (row.Category, row.Label));
        });
    }

    [Fact]
    public void 左パネルコマンドは互いに異なるグリフを持つ()
    {
        Assert.Equal(Commands.Length, Commands.Select(CommandGlyphs.GlyphOf).Distinct().Count());
    }

    private static void AssertDefaults(AppSettings settings)
    {
        Assert.True(settings.ShowLeftPanel);
        Assert.Equal(LeftPanelViewKind.DriveTree, settings.LeftPanelView);
        Assert.Equal(280, settings.LeftPanelWidth);
        Assert.Empty(settings.ExpandedBookmarkGroupIds);
    }
}
