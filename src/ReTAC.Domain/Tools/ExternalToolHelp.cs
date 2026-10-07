namespace ReTAC.Domain.Tools;

/// <param name="Key">説明する設定の欄（<c>型名.プロパティ名</c>）か列挙の値（<c>型名.値</c>）。網羅のテストが型から作る名前と突き合わせる</param>
/// <param name="Label">画面に出る名前（アクセスキーなし）</param>
/// <param name="Macros">文字の欄でマクロを展開するか（R-136）。文字の欄でなければ <see cref="MacroUse.NotText"/></param>
public sealed record HelpEntry(string Key, string Label, string Description, MacroUse Macros = MacroUse.NotText);

/// <summary>R-136: 入力ダイアログの文字の欄がマクロを展開するか。欄を足したら必ず分類する（網羅のテストが止める）。</summary>
public enum MacroUse
{
    /// <summary>文字の欄ではない</summary>
    NotText,
    /// <summary>マクロを展開する。${prompt} は入れ子にできない</summary>
    Expanded,
    /// <summary>マクロを展開せず、書いたとおりの文字として使う</summary>
    Literal,
    /// <summary>引数の行の種類で決まる（固定の引数は文字どおり、マクロを含む引数は展開する）</summary>
    ByKind,
}

/// <param name="On">ON にしたときの動き</param>
/// <param name="Off">OFF にしたときの動き</param>
/// <param name="Note">補足。無ければ空</param>
/// <param name="When">どんなときに ON にするか。マクロ構築プロンプトで AI が利用者に案内するために使う</param>
public sealed record ToolOptionHelp(string Key, string Label, string On, string Off, string Note, string When)
{
    /// <summary>設定画面のオプションの「?」に出す文。</summary>
    public string ToolTip => $"ON: {On}\nOFF: {Off}" + (Note.Length > 0 ? $"\n\n{Note}" : "");
}

/// <summary>
/// R-145-2: 外部ツールの設定の欄・オプション・入力ダイアログの説明。
/// 設定画面とマクロ構築プロンプトが同じ文を使う。手で書いた別の説明を持つと、欄を足したときに片方だけ書き漏れるため。
/// 欄や列挙の値を足したら、ここにも足す（網羅のテストが止める）。
/// </summary>
public static class ExternalToolHelp
{
    public const string SettingsTitle = "外部ツールの設定";
    public const string PromptEditButton = "入力ダイアログ編集";
    public const string MacroButton = "マクロ";

    public static IReadOnlyList<HelpEntry> ToolFields { get; } =
    [
        new("ExternalTool.Name", "名前", "メニューとポップアップに出る名前。空では保存できない。"),
        new("ExternalTool.Path", "パス",
            "実行ファイル。git.exe のように名前だけでもよい（PATH と App Paths から探す）。.py などの実行できない種類のファイルは書けないので、インタープリターをパスに書き、スクリプトは引数に書く。"),
        new("ExternalTool.Arguments", "引数",
            "ツールに渡す引数。半角の空白とタブで区切り、マクロを書ける。「マクロ」ボタンで一覧から挿入できる。誤り（知らないマクロ・閉じていない引用符など）があると保存できない。"),
        new("ExternalTool.Prompt", PromptEditButton,
            "起動の前に出す入力ダイアログの定義。「入力ダイアログ編集」ボタンで組み立てる。引数欄の ${prompt} の位置に、定義した引数の並びが入る。"),
    ];

    public static ToolOptionHelp LaunchPerItem { get; } = new("ExternalTool.LaunchPerItem", "マークした項目ごとに起動する",
        "マークした数だけ、1 件ずつ順に起動する。前の 1 件が終わってから次を起動する。",
        "1 回だけ起動し、マークした項目をまとめて引数に渡す。",
        "",
        "ツールが一度に 1 つのファイルしか受け取らないとき、重い処理を 1 件ずつ順に進めたいとき。まとめて渡せるツール（エディタ・圧縮・git add など）は OFF。");

    public static ToolOptionHelp ShowInPopup { get; } = new("ExternalTool.ShowInPopup", "ポップアップに表示する",
        "コマンド「ポップアップメニューの表示」の「外部ツール ▶」に出る（既定のキーは G）。",
        "出ない。メニュー「ツール」とキー割り当てからは、どちらでも起動できる。",
        "",
        "よく使うツール。既定は ON。");

    public static ToolOptionHelp KeepWindowOpen { get; } = new("ExternalTool.KeepWindowOpen", "終了後もウィンドウを閉じない",
        "ツールが終わってもコンソールを閉じず、キーを押すまで結果を残す。",
        "終わると同時に閉じる。",
        "コンソールを使わないツールには効かない。",
        "コンソールのツール（git.exe・py.exe・.bat など）で結果やエラーを読みたいとき。GUI のアプリと、自分で終わらない対話型のシェルには不要。");

    public static ToolOptionHelp ConfirmBeforeRun { get; } = new("ExternalTool.ConfirmBeforeRun", "実行前に確認する",
        "実際に渡すコマンドラインを見せ、OK を押すまで起動しない。",
        "そのまま起動する。",
        "",
        "作業内容が消えるなど取り消せない操作をするとき。初めて設定したツールの引数を確かめたいとき。");

    public static IReadOnlyList<ToolOptionHelp> Options { get; } = [LaunchPerItem, ShowInPopup, KeepWindowOpen, ConfirmBeforeRun];

    public static IReadOnlyList<HelpEntry> PromptFields { get; } =
    [
        new("PromptDefinition.Title", "タイトル", "入力ダイアログの題名。空ならツールの名前。", MacroUse.Literal),
        new("PromptDefinition.RememberLast", "前回の入力を初期値にする",
            "ON なら、次からは前回 OK した値で開く。前回の文字は空欄も含めて優先し、マクロとして解釈し直さない。既定は OFF。"),
        new("PromptDefinition.Items", "表示する項目（表示順）", "画面に並ぶ入力の項目。10 個まで。上から表示する順に並べる。"),
        new("PromptDefinition.Arguments", "引数（送る順）",
            "ツールへ送る引数の行。表示する順とは別に、送る順に並べる。項目の間に ${file} などを挟める。すべての項目を、どこかの行で 1 回送る必要がある。" +
            "行はそれぞれ別の引数になり、行どうし（項目と項目、項目と固定の引数）を 1 つの引数につなげることはできない。" +
            "保存先のフォルダとファイル名のように 1 つの引数にしたい値は、1 つの項目（テキストかファイル）にまとめ、初期値をマクロで組み立てる（例: ${cwd}\\${pathBasenameNoExtension}）。"),

        new("PromptItem.Kind", "種類", "テキスト・フォルダ・ファイル・チェックボックス・ドロップダウンリストのどれか。作った後には変えられない。"),
        new("PromptItem.Label", "ラベル", "項目の横に出る名前。空にできない。", MacroUse.Literal),
        new("PromptItem.Initial", "初期値",
            "テキスト・フォルダ・ファイルの最初の値。マクロを書ける（マクロごとに対象の先頭 1 件を使う。空白・引用符は文字として残る）。利用者が入力した文字と前回の入力は、マクロとして解釈し直さない。", MacroUse.Expanded),
        new("PromptItem.Prefix", "前に付ける",
            "テキスト・フォルダ・ファイルの値の前に付ける文字。空白で終われば別の引数、終わらなければ値とつながる（-o なら -oE:\\展開先、-m の後に空白なら -m と値の 2 つ）。", MacroUse.Literal),
        new("PromptItem.Suffix", "後ろに付ける",
            "テキスト・フォルダ・ファイルの値の後ろに付ける文字。空白で始まれば別の引数、始まらなければ値とつながる。つながるのは同じ項目の値だけで、次の行（ほかの項目など）とはつながらない。", MacroUse.Literal),
        new("PromptItem.Required", "入力を必須にする",
            "テキスト・フォルダ・ファイルで ON なら、空欄では OK できない。OFF なら、テキスト・ファイルの空欄は付ける文字ごと送らない。"),
        new("PromptItem.FolderEmptyBehavior", "空欄のとき", "必須でないフォルダが空欄のときに送るもの（下の値を参照）。"),
        new("PromptItem.Value", "送る値", "チェックボックスが ON のときに送る値。引数欄と同じ書き方で、マクロも書ける。OFF なら何も送らない。", MacroUse.Expanded),
        new("PromptItem.InitialChecked", "初期値をオンにする", "チェックボックスを最初から ON にする。"),
        new("PromptItem.Choices", "選択肢", "ドロップダウンリストの選択肢（表示名と送る値）の並び。"),
        new("PromptItem.InitialChoiceId", "初期", "ドロップダウンリストで最初に選んでおく選択肢。"),

        new("PromptChoice.Label", "表示名", "選択肢の名前。同じ項目の中で重ねられない。", MacroUse.Literal),
        new("PromptChoice.Value", "送る値（選択肢）",
            "その選択肢を選んだときに送る値。引数欄と同じ書き方で、マクロも書ける。空なら何も送らない（「指定しない」の選択肢に使える）。", MacroUse.Expanded),

        new("PromptArgument.Kind", "引数の行の種類", "固定の引数・マクロを含む引数・項目のどれか（下の値を参照）。"),
        new("PromptArgument.Text", "引数の行の文字", "固定の引数なら送る値そのもの、マクロを含む引数なら引数欄と同じ書き方の文字列。", MacroUse.ByKind),
    ];

    public static IReadOnlyList<HelpEntry> PromptValues { get; } =
    [
        new("PromptItemKind.Text", "テキスト", "1 行の文字を入力する。"),
        new("PromptItemKind.Folder", "フォルダ", "フォルダのパスを入力する。履歴（↑）・クイックアクセス（↓）・参照から選べる。相対パスはカレントフォルダ基準になる。"),
        new("PromptItemKind.File", "ファイル", "ファイルのパスを入力する。参照から選べる。"),
        new("PromptItemKind.CheckBox", "チェックボックス", "ON / OFF を選ぶ。ON のときだけ送る値を送る。"),
        new("PromptItemKind.DropDown", "ドロップダウンリスト", "選択肢から 1 つ選ぶ。選んだ選択肢の送る値を送る。"),

        new("PromptArgumentKind.Fixed", "固定の引数", "書いた値をそのまま 1 つの引数として送る。マクロも引用符も解釈しない（空なら空の引数）。"),
        new("PromptArgumentKind.Template", "マクロを含む引数", "引数欄と同じ書き方。${file} などのマクロを展開して送る（${prompt} は書けない）。"),
        new("PromptArgumentKind.Item", "項目", "表示する項目の 1 つ。その項目の入力から作った引数を送る。前後の行とはつながらない。"),

        new("PromptFolderEmptyBehavior.CurrentFolder", "現在のフォルダパスを送信", "空欄ならカレントフォルダのパスを送る（既定）。"),
        new("PromptFolderEmptyBehavior.SendEmpty", "そのまま送信", "空欄なら長さ 0 の値を 1 つの引数として送る。前後に付ける文字も使う。"),
    ];

    /// <summary>マクロ構築プロンプトで、利用者の言い方を AI が読み違えないようにするための言葉。</summary>
    public static IReadOnlyList<HelpEntry> Terms { get; } =
    [
        new("Term.CurrentFolder", "カレントフォルダ", "ファイルリストで今開いているフォルダ。外部ツールは常にここを作業ディレクトリとして起動する。"),
        new("Term.Cursor", "カーソル", "ファイルリストで今選んでいる 1 件。"),
        new("Term.Mark", "マーク", "複数の項目を選ぶために付ける印。"),
        new("Term.Target", "対象（実効対象）", "マークがあればマークしたすべての項目（一覧の表示順）、無ければカーソルの 1 件。"),
        new("Term.Parent", "..", "一覧の先頭の親フォルダの項目。カーソルが .. のとき、${path} はカレントフォルダになる。"),
        new("Term.Tree", "ツリーの対象", "左のツリーにフォーカスがあるときは、ツリーで選んだフォルダ 1 件が対象になる。カレントフォルダの外にあることがある。"),
    ];
}
