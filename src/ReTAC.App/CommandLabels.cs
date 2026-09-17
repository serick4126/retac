using ReTAC.Domain.Commands;
using ReTAC.Domain.Tools;

namespace ReTAC.App;

/// <summary>
/// コマンドの表示名と分類。
/// キー割り当ての設定に並べるためのもの。分類は探すためのもので、機能の区分ではない。
/// </summary>
public static class CommandLabels
{
    private static readonly (string Category, CommandId Command, string Label)[] Table =
    [
        ("ファイル操作", CommandId.OpenFile, "開く"),
        ("ファイル操作", CommandId.CopyToFolder, "フォルダへコピー"),
        ("ファイル操作", CommandId.MoveToFolder, "フォルダへ移動"),
        ("ファイル操作", CommandId.Delete, "削除"),
        ("ファイル操作", CommandId.Rename, "名前の変更"),
        ("ファイル操作", CommandId.ConcatFiles, "ファイルの連結"),
        ("ファイル操作", CommandId.ChangeAttributes, "属性の変更"),
        ("ファイル操作", CommandId.CreateShortcut, "ショートカットの作成"),
        ("ファイル操作", CommandId.CreateFolder, "フォルダ作成"),
        ("ファイル操作", CommandId.ShowProperties, "プロパティ"),

        ("選択", CommandId.ToggleAllMarks, "全選択＆選択解除"),
        ("選択", CommandId.InvertMarks, "反転選択"),
        ("選択", CommandId.MarkBySameExtension, "同じ拡張子を選択"),
        ("選択", CommandId.MarkByWildcard, "ワイルドカードで選択"),

        ("ナビゲーション", CommandId.GoParent, "親フォルダに戻る"),
        ("ナビゲーション", CommandId.GoRoot, "ルートフォルダに戻る"),
        ("ナビゲーション", CommandId.GoBack, "前のフォルダに戻る"),
        ("ナビゲーション", CommandId.GoForward, "次のフォルダに進む"),
        ("ナビゲーション", CommandId.FolderHistory, "フォルダ履歴"),
        ("ナビゲーション", CommandId.QuickAccess, "クイックアクセス"),
        ("ナビゲーション", CommandId.QuickAccessAdd, "クイックアクセスに追加"),
        ("ナビゲーション", CommandId.DirectJump, "ダイレクトジャンプ"),
        ("ナビゲーション", CommandId.SelectDrive, "ドライブの選択"),
        ("ナビゲーション", CommandId.DriveByNumberKey, "数字キーのドライブ移動"),
        ("ナビゲーション", CommandId.GoDesktop, "デスクトップへ移動"),
        ("ナビゲーション", CommandId.IncrementalSearch, "インクリメンタルサーチ"),

        ("表示", CommandId.Refresh, "最新の情報に更新"),
        ("表示", CommandId.ToggleDriveBar, "ドライブバーの表示切り替え"),
        ("表示", CommandId.ShowPopupMenu, "ポップアップメニューの表示"),
        ("表示", CommandId.ShowContextMenu, "コンテキストメニューの表示"),
        ("表示", CommandId.ShowFolderBackgroundMenu, "フォルダの背景メニューの表示"),
        ("表示", CommandId.NewWindow, "新しいウィンドウ"),
        ("表示", CommandId.Quit, "ReTAC の終了"),
        ("表示", CommandId.QuitAll, "ReTAC を完全に終了"),

        ("クリップボード", CommandId.ClipboardCopy, "コピー"),
        ("クリップボード", CommandId.ClipboardCut, "切り取り"),
        ("クリップボード", CommandId.ClipboardPaste, "貼り付け"),
        ("クリップボード", CommandId.CopyFileName, "ファイル名をコピー（形式を選ぶ）"),
        ("クリップボード", CommandId.CopyFileNameWithPath, "パスとファイル名をコピー"),
        ("クリップボード", CommandId.CopyFileNameOnly, "ファイル名のみコピー"),
        ("クリップボード", CommandId.CopyFileNameWithPathSlash, "/ 区切りのパスをコピー"),

        ("外部ツール", CommandId.RunCommandLine, "名前を指定し実行"),
        ("外部ツール", CommandId.ExternalToolQueue, "外部ツールキュー"),

        ("設定", CommandId.SortSettings, "ソートの設定"),
        ("設定", CommandId.FileTypeSettings, "表示するファイルタイプの設定"),
        ("設定", CommandId.QuickAccessSettings, "クイックアクセスの設定"),
        ("設定", CommandId.ExternalToolSettings, "外部ツールの設定"),
        ("設定", CommandId.ColorAndFontSettings, "配色・フォントの設定"),
        ("設定", CommandId.KeyAssignSettings, "キー割り当ての設定"),
        ("設定", CommandId.VisibleDriveSettings, "表示するドライブの設定"),
        ("設定", CommandId.EnvironmentSettings, "動作環境の設定"),
    ];

    private static readonly Dictionary<CommandId, string> Labels =
        Table.ToDictionary(row => row.Command, row => row.Label);

    public const string Unassigned = "（割り当てなし）";

    public static string Of(CommandId? command) =>
        command is { } id && Labels.TryGetValue(id, out var label) ? label : command?.ToString() ?? Unassigned;

    /// <summary>F-06: キーが指す先の表示名。外部ツールは「外部ツール：名前」。</summary>
    public static string Of(CommandTarget? target, IReadOnlyList<ExternalTool> tools) => target switch
    {
        null => Unassigned,
        BuiltinTarget builtin => Of(builtin.Command),
        ToolTarget tool => tools.FirstOrDefault(t => t.Id == tool.ToolId) is { } found
            ? $"外部ツール：{found.Name}"
            : "（削除された外部ツール）",
        _ => target.Serialize(),
    };

    /// <summary>設定ダイアログに並べる順（表示名の五十音ではなく、この表の並び順）。</summary>
    public static IEnumerable<CommandId> All => Table.Select(row => row.Command);

    /// <summary>分類ごとにまとめたもの。キー割り当ての一覧をグループ表示するために使う。</summary>
    public static IEnumerable<(string Category, CommandId Command, string Label)> Grouped => Table;
}
