using ReTAC.Domain.Commands;
using ReTAC.Domain.Listing;

namespace ReTAC.App;

/// <summary>R-112-4 / Q29: モードごとのコマンド。作ったモードの分だけ。</summary>
public static class ViewModeCommands
{
    public static readonly IReadOnlyList<CommandId> All =
    [
        CommandId.ViewExtraLargeIcons, CommandId.ViewLargeIcons, CommandId.ViewMediumIcons, CommandId.ViewSmallIcons,
        CommandId.ViewList, CommandId.ViewDetails,
    ];

    public static FileViewMode? Target(CommandId command) => command switch
    {
        CommandId.ViewExtraLargeIcons => FileViewMode.ExtraLargeIcons,
        CommandId.ViewLargeIcons => FileViewMode.LargeIcons,
        CommandId.ViewMediumIcons => FileViewMode.MediumIcons,
        CommandId.ViewSmallIcons => FileViewMode.SmallIcons,
        CommandId.ViewList => FileViewMode.List,
        CommandId.ViewDetails => FileViewMode.Details,
        _ => null,
    };
}
