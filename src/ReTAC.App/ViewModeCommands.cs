using ReTAC.Domain.Commands;
using ReTAC.Domain.Listing;

namespace ReTAC.App;

/// <summary>R-112-4 / Q29: モードごとのコマンド。8 つのモードすべて。</summary>
public static class ViewModeCommands
{
    public static readonly IReadOnlyList<CommandId> All =
    [
        CommandId.ViewExtraLargeIcons, CommandId.ViewLargeIcons, CommandId.ViewMediumIcons, CommandId.ViewSmallIcons,
        CommandId.ViewList, CommandId.ViewDetails, CommandId.ViewTiles, CommandId.ViewContent,
    ];

    public static FileViewMode? Target(CommandId command) => command switch
    {
        CommandId.ViewExtraLargeIcons => FileViewMode.ExtraLargeIcons,
        CommandId.ViewLargeIcons => FileViewMode.LargeIcons,
        CommandId.ViewMediumIcons => FileViewMode.MediumIcons,
        CommandId.ViewSmallIcons => FileViewMode.SmallIcons,
        CommandId.ViewList => FileViewMode.List,
        CommandId.ViewDetails => FileViewMode.Details,
        CommandId.ViewTiles => FileViewMode.Tiles,
        CommandId.ViewContent => FileViewMode.Content,
        _ => null,
    };
}
