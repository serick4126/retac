using System.Windows.Forms;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Keys;
using ReTAC.Domain.Tools;

namespace ReTAC.App;

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
    /// <param name="driveBarItem">R-77: 「表示 ＞ ドライブバー」。チェックの付け外しは呼び出し側が行う</param>
    /// <param name="addressBarItem">R-86: 「表示 ＞ アドレスバー」。チェックの付け外しは呼び出し側が行う</param>
    /// <param name="undoDescription">R-82: 最新の記録の説明。無ければ null</param>
    public static MenuStrip Create(Action<CommandTarget> dispatch, KeyMap keyMap, IReadOnlyList<ExternalTool> tools,
                                   out ToolStripMenuItem driveBarItem, out ToolStripMenuItem addressBarItem,
                                   Func<string?> undoDescription)
    {
        var keys = KeyLabels(keyMap);
        var menu = new MenuStrip();
        // ラムダ（ローカル関数）の中で out 引数を使えないので、いったん変数に受ける
        var driveBar = Item("ドライブバー(&D)", CommandId.ToggleDriveBar);
        var addressBar = Item("アドレスバー(&A)", CommandId.ToggleAddressBar);
        // R-82 / R-83: Ctrl+Z は固定のキーなので、キーマップの逆引きでは出ない。表示を直接与える
        var undo = Item("元に戻す(&U)", CommandId.Undo);
        undo.ShortcutKeyDisplayString = "Ctrl+Z";

        var edit = Top("編集(&E)",
            undo,
            Separator(),
            Item("切り取り(&X)", CommandId.ClipboardCut),
            Item("コピー(&C)", CommandId.ClipboardCopy),
            Item("貼り付け(&P)", CommandId.ClipboardPaste),
            Separator(),
            Item("ファイル名のコピー(&B)...", CommandId.CopyFileName),
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

        menu.Items.AddRange(
        [
            Top("ファイル(&F)",
                Item("開く(&O)", CommandId.OpenFile),
                Separator(),
                Item("フォルダへコピー(&C)...", CommandId.CopyToFolder),
                Item("フォルダへ移動(&M)...", CommandId.MoveToFolder),
                Item("削除(&D)", CommandId.Delete),
                Item("ショートカットの作成(&L)...", CommandId.CreateShortcut),
                Separator(),
                Item("名前の変更(&N)...", CommandId.Rename),
                Item("属性の変更(&A)...", CommandId.ChangeAttributes),
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
                Separator(),
                Item("親フォルダへ(&U)", CommandId.GoParent),
                Item("ルートフォルダに戻る(&R)", CommandId.GoRoot),
                Separator(),
                Item("前のフォルダに戻る(&B)", CommandId.GoBack),
                Item("次のフォルダに進む(&A)", CommandId.GoForward),
                Separator(),
                Item("クイックアクセス(&Q)", CommandId.QuickAccess),
                Item("フォルダ履歴(&H)", CommandId.FolderHistory),
                Item("ダイレクトジャンプ(&J)...", CommandId.DirectJump),
                Separator(),
                Item("ドライブの選択(&V)", CommandId.SelectDrive),
                Item("デスクトップへ移動(&K)", CommandId.GoDesktop)),

            Top("表示(&V)",
                Item("最新の情報に更新(&R)", CommandId.Refresh),
                Separator(),
                driveBar,
                addressBar,
                Separator(),
                Item("ソートの設定(&S)...", CommandId.SortSettings),
                Item("表示するファイルタイプ(&T)...", CommandId.FileTypeSettings)),

            Top("ツール(&T)", ToolsMenu()),

            Top("設定(&O)",
                Item("配色・フォントの設定(&C)...", CommandId.ColorAndFontSettings),
                Item("キー割り当ての設定(&K)...", CommandId.KeyAssignSettings),
                Item("表示するドライブの設定(&D)...", CommandId.VisibleDriveSettings),
                Item("外部ツールの設定(&T)...", CommandId.ExternalToolSettings),
                Item("クイックアクセスの設定(&Q)...", CommandId.QuickAccessSettings),
                Item("クイックアクセスに追加(&A)", CommandId.QuickAccessAdd),
                Separator(),
                Item("動作環境の設定(&E)...", CommandId.EnvironmentSettings)),

            Top("ヘルプ(&H)",
                Item("バージョン情報(&A)...", CommandId.About)),
        ]);

        driveBarItem = driveBar;
        addressBarItem = addressBar;
        return menu;

        ToolStripItem[] ToolsMenu()
        {
            var items = new List<ToolStripItem>(tools.Select(ToolItem));
            if (items.Count > 0) items.Add(Separator());
            items.Add(Item("プロパティ(&R)", CommandId.ShowProperties));
            items.Add(Item("コンテキストメニュー(&C)", CommandId.ShowContextMenu));
            items.Add(Item("フォルダのコンテキストメニュー(&B)", CommandId.ShowFolderBackgroundMenu));
            items.Add(Item("ポップアップメニュー(&P)", CommandId.ShowPopupMenu));
            items.Add(Separator());
            items.Add(Item("外部ツールキュー(&Q)...", CommandId.ExternalToolQueue));
            return [.. items];
        }

        ToolStripMenuItem Item(string text, CommandId command)
        {
            var target = new BuiltinTarget(command);
            var item = new ToolStripMenuItem(text);
            // 実際のキー処理はファイルリストが受け持つ。ここは割り当ての表示だけ
            if (keys.TryGetValue(target, out var label)) item.ShortcutKeyDisplayString = label;
            item.Click += (_, _) => dispatch(target);
            return item;
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
        return item;
    }

    private static ToolStripSeparator Separator() => new();

    /// <summary>キーマップを逆引きして「C」「Shift+Enter」「Ctrl+E」のような表示用の文字列にする。</summary>
    private static Dictionary<CommandTarget, string> KeyLabels(KeyMap keyMap)
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
