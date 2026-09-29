using ReTAC.App;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Listing;

namespace ReTAC.Domain.Tests;

public class ViewModeSwitchTests
{
    [Theory]
    [InlineData(CommandId.ViewList, FileViewMode.List)]
    [InlineData(CommandId.ViewDetails, FileViewMode.Details)]
    public void モードごとのコマンドがモードを指す(CommandId command, FileViewMode mode) =>
        Assert.Equal(mode, ViewModeCommands.Target(command));

    [Fact]
    public void 作ったモードにはすべてコマンドがある() =>
        Assert.All(FileViewModes.Built, mode => Assert.Contains(ViewModeCommands.All, c => ViewModeCommands.Target(c) == mode));
}
