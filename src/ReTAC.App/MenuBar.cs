using System.Windows.Forms;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Keys;
using ReTAC.Domain.Tools;

namespace ReTAC.App;

/// <summary>Q10: 表示中かどうかのチェックは親項目（Root）に付く。子のラジオは今のビューを示すだけ。</summary>
public sealed record LeftPanelMenuItems(
    ToolStripMenuItem Root,
    IReadOnlyDictionary<LeftPanelViewKind, ToolStripMenuItem> Views);

/// <summary>
/// R-104-1: 状態で中身が変わるサブメニューを、開くたびに MainForm の状態から作るための関数の束。
/// ブックマークメニュー（R-90）と同じ考え方だが、こちらは 1 つの MenuStrip に複数あるので record にまとめる。
/// </summary>
/// <param name="SortItems">並べ替えのラジオ（ソートキー・並べ方）</param>
/// <param name="FileTypeItems">表示するファイルタイプのチェック</param>
/// <param name="QuickAccessItems">クイックアクセスの登録先一覧</param>
/// <param name="FolderHistoryItems">フォルダ履歴</param>
/// <param name="ClearFolderHistory">「履歴のクリア」。コマンドではないので Tag は持たない</param>
/// <param name="DriveItems">表示するドライブ（デスクトップを含む）</param>
public sealed record MenuDynamicContent(
    Func<IReadOnlyList<ToolStripItem>> SortItems,
    Func<IReadOnlyList<ToolStripItem>> FileTypeItems,
    Func<IReadOnlyList<ToolStripItem>> QuickAccessItems,
    Func<IReadOnlyList<ToolStripItem>> FolderHistoryItems,
    Action ClearFolderHistory,
    Func<IReadOnlyList<ToolStripItem>> DriveItems);

/// <summary>R-96: 相互排他的にチェックする左パネルビュー項目。</summary>
public sealed class RadioToolStripMenuItem : ToolStripMenuItem
{
    public RadioToolStripMenuItem(string text) : base(text) { }

    protected override void OnCheckedChanged(EventArgs e)
    {
        if (Checked && Owner is { } owner)
            foreach (var other in owner.Items.OfType<RadioToolStripMenuItem>().Where(item => item != this))
                other.Checked = false;
        base.OnCheckedChanged(e);
    }
}

/// <summary>
/// メニューバー。<b>マウスだけで操作を完結させるための経路</b>である。
/// A-02（キーボードだけで完結する）と対になるものであって、それを置き換えるものではない。
///
/// 構成は卓駆のメインメニューから
/// スコープ内のコマンドだけを拾った。項目はすべて既存の
/// <see cref="MainForm"/> のコマンド実行経路（R-12）を呼ぶだけで、独自の処理を持たない。
/// </summary>
public static class MenuBar
{
    /// <param name="dispatch">コマンドの実行。MainForm の Execute に繋ぐ</param>
    /// <param name="keyMap">項目の右側に割り当てキーを出すために引く</param>
    /// <param name="tools">F-07: 「ツール」メニューの先頭に登録順で並べる（「ポップアップに表示する」は効かない）</param>
    /// <param name="dynamicContent">R-104-1: 状態で中身が変わるサブメニューの中身を、開くたびに MainForm から取り直す</param>
    /// <param name="driveBarItem">R-77: 「表示 ＞ ドライブバー」。チェックの付け外しは呼び出し側が行う</param>
    /// <param name="addressBarItem">R-86: 「表示 ＞ アドレスバー」。チェックの付け外しは呼び出し側が行う</param>
    /// <param name="bookmarkBarItem">R-89: 「表示 ＞ ブックマークバー」。チェックの付け外しは呼び出し側が行う</param>
    /// <param name="leftPanelItems">R-96 / Q10: 「表示 ＞ 左パネル」。チェックは親項目に付く。状態の反映は呼び出し側が行う</param>
    /// <param name="undoDescription">R-82: 最新の記録の説明。無ければ null</param>
    public static MenuStrip Create(Action<CommandTarget> dispatch, KeyMap keyMap, IReadOnlyList<ExternalTool> tools,
                                   MenuDynamicContent dynamicContent,
                                   out ToolStripMenuItem driveBarItem, out ToolStripMenuItem addressBarItem, out ToolStripMenuItem bookmarkBarItem,
                                   out LeftPanelMenuItems leftPanelItems,
                                   Func<string?> undoDescription)
    {
        var keys = KeyLabels(keyMap);
        // 狭いウィンドウでも入りきらない項目を「»」に回す。既定（false）では項目が消え、メニューに届かなくなる（実機指摘）
        var menu = new MenuStrip { CanOverflow = true };
        ToolStripExtras.WidenOverflow(menu);
        // ラムダ（ローカル関数）の中で out 引数を使えないので、いったん変数に受ける
        var driveBar = Item("ドライブバー(&D)", CommandId.ToggleDriveBar);
        var addressBar = Item("アドレスバー(&A)", CommandId.ToggleAddressBar);
        var bookmarkBar = Item("ブックマークバー(&B)", CommandId.ToggleBookmarkBar);
        // Q10: チェックは親（leftPanel）に付ける。この項目は切り替えの操作に特化し、チェックは持たない
        var leftToggle = Item("左パネルの表示切り替え(&L)", CommandId.ToggleLeftPanel);
        var leftViews = new Dictionary<LeftPanelViewKind, ToolStripMenuItem>
        {
            [LeftPanelViewKind.DriveTree] = RadioItem("ドライブツリー(&D)", CommandId.ShowDriveTree),
            [LeftPanelViewKind.DesktopTree] = RadioItem("デスクトップツリー(&T)", CommandId.ShowDesktopTree),
            [LeftPanelViewKind.Bookmarks] = RadioItem("ブックマーク(&B)", CommandId.ShowBookmarksView),
            [LeftPanelViewKind.Preview] = RadioItem("プレビュー(&P)", CommandId.ShowPreview),
        };
        var leftPanel = Top("左パネル(&L)", [leftToggle, Separator(), .. leftViews.Values]);
        // R-82 / R-83: Ctrl+Z は固定のキーなので、キーマップの逆引きでは出ない。表示を直接与える
        var undo = Item("元に戻す(&U)", CommandId.Undo);
        undo.ShortcutKeyDisplayString = "Ctrl+Z";

        // R-104-1: 3 つの既存コマンドをサブメニューに展開する。CopyFileName 自体は直接の項目を持たなくなるが、
        // R-12-2 の例外条件（選択肢はサブメニューから届く）を満たす
        var copyFileName = Top("ファイル名のコピー(&B)",
            Item("パス＋名前(&P)", CommandId.CopyFileNameWithPath),
            Item("名前のみ(&N)", CommandId.CopyFileNameOnly),
            Item("/ 区切りのパス(&S)", CommandId.CopyFileNameWithPathSlash));

        var edit = Top("編集(&E)",
            undo,
            Separator(),
            Item("切り取り(&X)", CommandId.ClipboardCut),
            Item("コピー(&C)", CommandId.ClipboardCopy),
            Item("貼り付け(&P)", CommandId.ClipboardPaste),
            Separator(),
            copyFileName,
            Separator(),
            Item("全選択＆選択解除(&O)", CommandId.ToggleAllMarks),
            Item("反転選択(&R)", CommandId.InvertMarks),
            Item("同じ拡張子を選択(&D)", CommandId.MarkBySameExtension),
            // 既定のキー割り当てが無く `G` のポップアップにも無いので、
            // ここに置かないと自分で割り当てない限り実行できない
            Item("ワイルドカードで選択(&W)...", CommandId.MarkByWildcard),
            Separator(),
            Item("インクリメンタルサーチ(&F)", CommandId.IncrementalSearch));
        edit.DropDownOpening += (_, _) =>
        {
            var description = undoDescription();
            undo.Enabled = description is not null;
            undo.Text = description is null ? "元に戻す(&U)" : $"{description.Replace("&", "&&")}を元に戻す(&U)";
        };

        // R-104-1: 中身が状態で変わるサブメニュー。固定の末尾（Tag にコマンドを持つ）は組み立て時に作り、
        // 動的な項目はその前に、開くたびに差し込む（DynamicSubmenu）
        var sortMenu = DynamicSubmenu("並べ替え(&S)", dynamicContent.SortItems,
            Item("ソートの設定(&O)...", CommandId.SortSettings));
        var fileTypeMenu = DynamicSubmenu("表示するファイルタイプ(&F)", dynamicContent.FileTypeItems,
            Item("表示するファイルタイプの設定(&T)...", CommandId.FileTypeSettings));
        var quickAccessMenu = DynamicSubmenu("クイックアクセス(&Q)", dynamicContent.QuickAccessItems,
            Item("このフォルダを追加(&A)", CommandId.QuickAccessAdd),
            Item("クイックアクセスの設定(&Q)...", CommandId.QuickAccessSettings));
        // 「履歴のクリア」はコマンドではない（ShowFolderHistory のポップアップと同じ、履歴だけの操作）ので Tag は持たない
        var folderHistoryClear = new ToolStripMenuItem("履歴のクリア(&E)");
        folderHistoryClear.Click += (_, _) => dynamicContent.ClearFolderHistory();
        var folderHistoryMenu = DynamicSubmenu("フォルダ履歴(&H)", dynamicContent.FolderHistoryItems, folderHistoryClear);
        var driveSelectMenu = DynamicSubmenu("ドライブの選択(&V)", dynamicContent.DriveItems);

        menu.Items.AddRange(
        [
            Top("ファイル(&F)",
                Item("開く(&O)", CommandId.OpenFile),
                Separator(),
                Item("フォルダへコピー(&F)...", CommandId.CopyToFolder),
                Item("フォルダへ移動(&M)...", CommandId.MoveToFolder),
                Item("削除(&D)", CommandId.Delete),
                Item("ショートカットの作成(&L)...", CommandId.CreateShortcut),
                Separator(),
                Item("名前の変更(&N)...", CommandId.Rename),
                Item("属性の変更(&A)...", CommandId.ChangeAttributes),
                Separator(),
                // R-104: プロパティ・右クリックメニューをツールメニューから移す。Q7: 「右クリックメニュー」の呼び方に揃える
                Item("プロパティ(&R)", CommandId.ShowProperties),
                Item("右クリックメニュー(&C)", CommandId.ShowContextMenu),
                Separator(),
                Item("ファイルの連結(&G)...", CommandId.ConcatFiles),
                Separator(),
                Item("名前を指定して実行(&E)...", CommandId.RunCommandLine),
                Separator(),
                Item("新しいウィンドウ(&W)", CommandId.NewWindow),
                Separator(),
                Item("ReTAC の終了(&X)", CommandId.Quit),
                Item("ReTAC を完全に終了(&Q)", CommandId.QuitAll)),

            edit,

            Top("フォルダ(&D)",
                Item("フォルダ作成(&M)...", CommandId.CreateFolder),
                // R-104 / Q7: 現在のフォルダの右クリックメニューをここへ移す（呼び方は Q7 のとおり）
                Item("現在のフォルダの右クリックメニュー(&B)", CommandId.ShowFolderBackgroundMenu),
                Separator(),
                Item("親フォルダへ(&U)", CommandId.GoParent),
                Item("ルートフォルダに戻る(&R)", CommandId.GoRoot),
                Separator(),
                Item("前のフォルダに戻る(&P)", CommandId.GoBack),
                Item("次のフォルダに進む(&A)", CommandId.GoForward),
                Separator(),
                // R-104-1: クイックアクセス・フォルダ履歴をサブメニューに。設定 ＞ クイックアクセスに追加はここへ統合
                quickAccessMenu,
                folderHistoryMenu,
                Item("ダイレクトジャンプ(&J)...", CommandId.DirectJump),
                Separator(),
                // R-104-1: 表示するドライブをサブメニューに
                driveSelectMenu,
                Item("デスクトップへ移動(&K)", CommandId.GoDesktop)),

            Top("表示(&V)",
                Item("最新の情報に更新(&R)", CommandId.Refresh),
                Separator(),
                driveBar,
                addressBar,
                bookmarkBar,
                leftPanel,
                Separator(),
                // R-104-1: ソート・ファイルタイプをサブメニューに
                sortMenu,
                fileTypeMenu),

            Top("ツール(&T)", ToolsMenu()),

            Top("設定(&O)",
                Item("設定(&S)...", CommandId.OpenSettings),
                Separator(),
                Item("動作環境の設定(&E)...", CommandId.EnvironmentSettings),
                Item("配色・フォントの設定(&C)...", CommandId.ColorAndFontSettings),
                Item("キー割り当ての設定(&K)...", CommandId.KeyAssignSettings),
                Item("外部ツールの設定(&T)...", CommandId.ExternalToolSettings),
                Item("表示するドライブの設定(&D)...", CommandId.VisibleDriveSettings),
                Item("クイックアクセスの設定(&Q)...", CommandId.QuickAccessSettings)),

            Top("ヘルプ(&H)",
                // R-105: 新しいコマンド。既定のキーなし
                Item("GitHub のページを開く(&G)", CommandId.OpenGitHub),
                Separator(),
                Item("バージョン情報(&A)...", CommandId.About)),
        ]);

        driveBarItem = driveBar;
        addressBarItem = addressBar;
        bookmarkBarItem = bookmarkBar;
        leftPanelItems = new LeftPanelMenuItems(leftPanel, leftViews);
        return menu;

        ToolStripItem[] ToolsMenu()
        {
            // R-104: プロパティ・右クリックメニュー系はファイル・フォルダメニューへ移した
            var items = new List<ToolStripItem>(tools.Select(ToolItem));
            if (items.Count > 0) items.Add(Separator());
            items.Add(Item("外部ツールキュー(&Q)...", CommandId.ExternalToolQueue));
            items.Add(Separator());
            items.Add(Item("ポップアップメニュー(&P)", CommandId.ShowPopupMenu));
            return [.. items];
        }

        ToolStripMenuItem Item(string text, CommandId command, bool radio = false)
        {
            var target = new BuiltinTarget(command);
            var item = radio ? new RadioToolStripMenuItem(text) : new ToolStripMenuItem(text);
            item.Tag = command;
            // 実際のキー処理はファイルリストが受け持つ。ここは割り当ての表示だけ
            if (keys.TryGetValue(target, out var label)) item.ShortcutKeyDisplayString = label;
            item.Click += (_, _) => dispatch(target);
            return item;
        }

        ToolStripMenuItem RadioItem(string text, CommandId command)
        {
            return Item(text, command, radio: true);
        }

        ToolStripMenuItem ToolItem(ExternalTool tool)
        {
            // F-07: 名前は利用者が自由に付けるので & をニーモニックにしない（文字が重なりうる）
            var target = new ToolTarget(tool.Id);
            var item = new ToolStripMenuItem(tool.Name.Replace("&", "&&"));
            if (keys.TryGetValue(target, out var label)) item.ShortcutKeyDisplayString = label;
            item.Click += (_, _) => dispatch(target);
            return item;
        }
    }

    private static ToolStripMenuItem Top(string text, params ToolStripItem[] children)
    {
        var item = new ToolStripMenuItem(text);
        item.DropDownItems.AddRange(children);
        // R-88: 開くたびに掛ける（DPI の違うモニターへ移しても追従させるため）。
        // 「編集」の「元に戻す」の文言を変える処理（R-84）とは独立に働き、文言とキーの表示には触れない
        item.DropDownOpening += (_, _) => MenuSpacing.Apply(item.DropDownItems, item.Owner?.DeviceDpi ?? 96);
        return item;
    }

    /// <summary>
    /// R-104-1: 状態で中身が変わるサブメニュー。<paramref name="tail"/>（Tag にコマンドを持つ固定項目）は組み立て時に
    /// 一度だけ足し、開くたびに <paramref name="dynamicItems"/> の結果へ作り直して <paramref name="tail"/> の前へ差し込む。
    /// </summary>
    private static ToolStripMenuItem DynamicSubmenu(string text, Func<IReadOnlyList<ToolStripItem>> dynamicItems, params ToolStripItem[] tail)
    {
        var menu = new ToolStripMenuItem(text);
        menu.DropDownItems.AddRange(tail);
        menu.DropDownOpening += (_, _) =>
        {
            var items = new List<ToolStripItem>(dynamicItems());
            if (items.Count > 0 && tail.Length > 0) items.Add(Separator());
            items.AddRange(tail);
            MenuSpacing.Apply(items, menu.Owner?.DeviceDpi ?? 96);
            menu.DropDownItems.Clear();
            menu.DropDownItems.AddRange([.. items]);
        };
        return menu;
    }

    private static ToolStripSeparator Separator() => new();

    /// <summary>キーマップを逆引きして「C」「Shift+Enter」「Ctrl+E」のような表示用の文字列にする。</summary>
    internal static Dictionary<CommandTarget, string> KeyLabels(KeyMap keyMap)
    {
        var labels = new Dictionary<CommandTarget, string>();
        foreach (var (binding, target) in keyMap.Bindings)
        {
            // R-73: マウスのボタンはキーボードのショートカット欄に出さない。
            // ここに出るのは設定ファイル用の表記（XButton1）で、利用者には読めない
            if (binding.VirtualKey is Vk.MButton or Vk.XButton1 or Vk.XButton2) continue;

            var label = KeySlots.Label(binding);
            // 同じコマンドに複数の割り当てがあるときは短い方を出す（W と F5 なら W）
            if (!labels.TryGetValue(target, out var existing) || label.Length < existing.Length)
                labels[target] = label;
        }
        return labels;
    }
}
