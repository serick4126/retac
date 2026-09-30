using ReTAC.App;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Keys;
using ReTAC.Domain.Listing;

namespace ReTAC.Domain.Tests;

public class ViewModeSwitchTests
{
    [Theory]
    [InlineData(CommandId.ViewList, FileViewMode.List)]
    [InlineData(CommandId.ViewDetails, FileViewMode.Details)]
    [InlineData(CommandId.ViewExtraLargeIcons, FileViewMode.ExtraLargeIcons)]
    [InlineData(CommandId.ViewLargeIcons, FileViewMode.LargeIcons)]
    [InlineData(CommandId.ViewMediumIcons, FileViewMode.MediumIcons)]
    [InlineData(CommandId.ViewSmallIcons, FileViewMode.SmallIcons)]
    [InlineData(CommandId.ViewTiles, FileViewMode.Tiles)]
    [InlineData(CommandId.ViewContent, FileViewMode.Content)]
    public void モードごとのコマンドがモードを指す(CommandId command, FileViewMode mode) =>
        Assert.Equal(mode, ViewModeCommands.Target(command));

    [Fact]
    public void 作ったモードにはすべてコマンドがある() =>
        Assert.All(FileViewModes.Built, mode => Assert.Contains(ViewModeCommands.All, c => ViewModeCommands.Target(c) == mode));

    [Fact]
    public void 表示モードのコマンドは8つで_モードの並びと同じ()
    {
        Assert.Equal(8, ViewModeCommands.All.Count);
        Assert.Equal(FileViewModes.Built, ViewModeCommands.All.Select(c => ViewModeCommands.Target(c)!.Value));
    }

    [Fact]
    public void 表示モードのコマンドはどれも既定のキーを持たない()
    {
        var bound = DefaultKeyMap.Create().Bindings.Values.OfType<BuiltinTarget>().Select(t => t.Command).ToHashSet();
        Assert.All(ViewModeCommands.All, command => Assert.DoesNotContain(command, bound));
    }
}
