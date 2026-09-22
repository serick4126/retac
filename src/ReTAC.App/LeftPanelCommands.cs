using ReTAC.Domain.Commands;

namespace ReTAC.App;

/// <summary>R-96-2: 左パネルの状態はウィンドウごと。どの状態からどのコマンドで次の状態になるかは
/// 副作用（保存・フォーカス移動）と切り離した純粋な判定にし、MainForm はここへ問い合わせるだけにする。</summary>
public static class LeftPanelCommands
{
    public readonly record struct Result(bool Shown, LeftPanelViewKind View, bool Changed);

    /// <summary>
    /// 個別表示は非表示なら表示して対象ビューへ切り替え、表示中で既に同じビューなら何もしない。
    /// トグルは表示中なら隠し（ビューは変えない）、非表示なら最後のビューのまま表示する。
    /// </summary>
    public static Result Apply(CommandId command, bool shown, LeftPanelViewKind view) => command switch
    {
        CommandId.ToggleLeftPanel => new Result(!shown, view, true),
        CommandId.ShowDriveTree => Show(shown, view, LeftPanelViewKind.DriveTree),
        CommandId.ShowDesktopTree => Show(shown, view, LeftPanelViewKind.DesktopTree),
        CommandId.ShowBookmarksView => Show(shown, view, LeftPanelViewKind.Bookmarks),
        CommandId.ShowPreview => Show(shown, view, LeftPanelViewKind.Preview),
        _ => new Result(shown, view, false),
    };

    private static Result Show(bool shown, LeftPanelViewKind view, LeftPanelViewKind target) => shown && view == target
        ? new Result(shown, view, false)
        : new Result(true, target, true);
}
