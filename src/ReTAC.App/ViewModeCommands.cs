using ReTAC.Domain.Commands;
using ReTAC.Domain.Listing;

namespace ReTAC.App;

/// <summary>R-112-4 / Q29: モードごとのコマンド。作ったモードの分だけ。</summary>
public static class ViewModeCommands
{
    public static readonly IReadOnlyList<CommandId> All = [CommandId.ViewList, CommandId.ViewDetails];

    public static FileViewMode? Target(CommandId command) => command switch
    {
        CommandId.ViewList => FileViewMode.List,
        CommandId.ViewDetails => FileViewMode.Details,
        _ => null,
    };
}
