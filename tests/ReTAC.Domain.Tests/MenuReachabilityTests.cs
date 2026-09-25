using ReTAC.App;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Keys;
using System.Windows.Forms;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-12-2 / INV-MENU-REACHABLE: すべての CommandId がメニューから届くことを確かめる。
/// メニューバー（MenuBar.Create）を状態に依らない中身（NoDynamicContent）で組み立て、
/// 項目・サブメニューの Tag（CommandId）を再帰的に集める。動的な中身（並べ替えの現在値・
/// クイックアクセスの登録先など）は実行時に MainForm の状態から作るのでここでは対象にならないが、
/// その入れ物であるサブメニューの固定の末尾項目（組み立て時に足す。Tag を持つ）は開かなくても集まる。
/// </summary>
public class MenuReachabilityTests
{
    /// <summary>
    /// R-90: 「ブックマーク」メニューは MenuBar.Create の外、MainForm.BookmarkMenu が開くたびに組み立てる
    /// （中身がその窓のブックマーク・現在のフォルダに依るため）。ここに直接の項目を持つコマンドを手で列挙する。
    /// </summary>
    private static readonly CommandId[] BookmarkMenuCommands =
    [
        CommandId.BookmarkManage,            // 「ブックマークを管理...」
        CommandId.BookmarkAddCurrentFolder,  // 「現在のフォルダを追加」
        // 右クリックの「カーソル位置の項目を追加」からは AddBookmark を直接呼んでいて Execute を経由しない
        // 別経路で、BookmarkMenu に直接の項目が無かった。項目を足して届くようにした（R-12-2）
        CommandId.BookmarkAddCursorItem,
    ];

    /// <summary>
    /// R-12-2 の例外条件（直接の項目は無いが、同じ機能に別の項目から届く）を満たすコマンド。
    /// 1 件ずつ理由を書く。ここに載せてよいのは「サブメニューの中身が同じ機能を果たす」場合だけで、
    /// 単に「面倒だから」で足してはならない。
    /// </summary>
    private static readonly CommandId[] ReachableViaSameFunction =
    [
        // 編集＞ファイル名のコピー▸ が CopyFileNameWithPath / CopyFileNameOnly / CopyFileNameWithPathSlash の
        // 3 項目に展開されている。総称の CopyFileName 自体は直接の項目を持たない
        CommandId.CopyFileName,
        // フォルダ＞クイックアクセス▸ の動的な登録先一覧（開くたびに作る）が同じ機能を果たす
        CommandId.QuickAccess,
        // フォルダ＞フォルダ履歴▸ の動的な履歴一覧（開くたびに作る）が同じ機能を果たす
        CommandId.FolderHistory,
        // フォルダ＞ドライブの選択▸ の動的なドライブ一覧（開くたびに作る）が同じ機能を果たす
        CommandId.SelectDrive,
        // R-12-2 の例示そのもの: 数字キーでのドライブ移動も「ドライブの選択▸」から同じ切り替えができる
        CommandId.DriveByNumberKey,
        // R-107: バーへフォーカスして項目をたどるのと、「ブックマーク」メニューの「ブックマークバー」▸
        // をたどるのは、同じ並び・同じ項目をキーボードで選ぶという同じ機能なので、直接の項目は持たない
        CommandId.FocusBookmarkBar,
    ];

    [Fact]
    public void すべてのコマンドはメニューバーかブックマークメニューか同じ機能のサブメニューで届く()
    {
        using var menu = MenuBar.Create(_ => { }, new KeyMap([]), [], NoDynamicContent(),
            out _, out _, out _, out _, () => null);

        var reachable = CollectCommandTags(menu.Items.Cast<ToolStripItem>())
            .Concat(BookmarkMenuCommands)
            .Concat(ReachableViaSameFunction)
            .ToHashSet();

        var missing = Enum.GetValues<CommandId>().Where(id => !reachable.Contains(id)).ToList();

        Assert.True(missing.Count == 0,
            $"メニューから届かないコマンド: {string.Join(", ", missing)}。"
            + "R-12-2: 項目を足すか、同じ機能に届く場合だけ理由付きで一覧に加える（外す方向に直さない）");
    }

    private static IEnumerable<CommandId> CollectCommandTags(IEnumerable<ToolStripItem> items)
    {
        foreach (var item in items)
        {
            if (item.Tag is CommandId id) yield return id;
            if (item is ToolStripDropDownItem dropDown)
                foreach (var found in CollectCommandTags(dropDown.DropDownItems.Cast<ToolStripItem>()))
                    yield return found;
        }
    }

    /// <summary>状態で中身が変わるサブメニュー（R-104-1）の中身自体はここでは対象外。固定の末尾項目だけで足りる。</summary>
    private static MenuDynamicContent NoDynamicContent() =>
        new(() => [], () => [], () => [], () => [], () => { }, () => []);
}
