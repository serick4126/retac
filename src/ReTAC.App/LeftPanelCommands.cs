using ReTAC.Domain.Commands;

namespace ReTAC.App;

/// <summary>R-96-2: 左パネルの状態はウィンドウごと。どの状態からどのコマンドで次の状態になるかは
/// 副作用（保存・フォーカス移動）と切り離した純粋な判定にし、MainForm はここへ問い合わせるだけにする。</summary>
public static class LeftPanelCommands
{
    public readonly record struct Result(bool Shown, LeftPanelViewKind View, bool Changed);

    /// <summary>
    /// 個別表示は非表示なら表示して対象ビューへ切り替え、表示中で別ビューならそのビューへ切り替える。
    /// 表示中で既に同じビューなら非表示にする（R-96-3。ビューは変えないので、次に表示したときのラジオは
    /// 最後のビューに残る＝R-96）。ただし <paramref name="fromSelector"/> が true（左パネル上端のビュー選択欄
    /// から呼ばれた場合）は何もしない。上端はビューの選択だけで、閉じる操作を置かない（Phase 10 の決定）。
    /// トグルは表示中なら隠し（ビューは変えない）、非表示なら最後のビューのまま表示する。
    /// </summary>
    public static Result Apply(CommandId command, bool shown, LeftPanelViewKind view, bool fromSelector = false) => command switch
    {
        CommandId.ToggleLeftPanel => new Result(!shown, view, true),
        CommandId.ShowDriveTree => Show(shown, view, LeftPanelViewKind.DriveTree, fromSelector),
        CommandId.ShowDesktopTree => Show(shown, view, LeftPanelViewKind.DesktopTree, fromSelector),
        CommandId.ShowBookmarksView => Show(shown, view, LeftPanelViewKind.Bookmarks, fromSelector),
        CommandId.ShowPreview => Show(shown, view, LeftPanelViewKind.Preview, fromSelector),
        _ => new Result(shown, view, false),
    };

    /// <summary>R-98 / Q63: ブックマークビューでも頭文字検索より優先する 5 コマンド。</summary>
    public static bool IsLeftPanelCommand(CommandId command) => command is CommandId.ToggleLeftPanel
        or CommandId.ShowDriveTree or CommandId.ShowDesktopTree or CommandId.ShowBookmarksView or CommandId.ShowPreview;

    private static Result Show(bool shown, LeftPanelViewKind view, LeftPanelViewKind target, bool fromSelector)
    {
        if (!shown || view != target) return new Result(true, target, true);
        return fromSelector ? new Result(shown, view, false) : new Result(false, view, true);
    }
}
