namespace ReTAC.Domain.Tools;

/// <param name="Insert">引数欄に挿入する文字。挿入しない項目（書き方の説明・例）は null</param>
/// <param name="CaretOffset">挿入後にカーソルを置く位置（挿入した文字の先頭から）</param>
public sealed record MacroCatalogEntry(string Group, string Label, string? Insert, int CaretOffset, string Summary, string Description);

/// <summary>R-138: マクロを挿入する欄の文法。</summary>
public enum MacroUsage { Arguments, PromptArgument, InitialValue, ItemValue }

/// <summary>
/// 外部ツールの設定ダイアログから開くマクロの一覧（F-04）。
/// 利用者自身も名前と使い方を覚えきれないので、選んで挿入できるようにする（F-04）。
/// スクリプトの例は例であって既定値ではない。
/// </summary>
public static class MacroCatalog
{
    // R-144-1: マクロが増えて 1 つのグループでは探しにくいので、対象の系統ごとに分ける。見出しが区切りになる。
    // 見出しは値の形（パス・名前）ではなく対象で名乗る。${pathBasename} のように名前を返すものも同じ対象の系統に入るため。
    // 各グループの中は、ファイルだけを返すものを先に、同じ系統ではフルパス・拡張子なしのパス・名前・拡張子なしの名前・拡張子の順。
    public const string FileGroup = "対象のファイル（フォルダを除く）";
    public const string PathGroup = "対象の項目（フォルダも含む）";
    public const string CursorGroup = "カーソルの項目";
    public const string CwdGroup = "カレントフォルダ";
    public const string PromptGroup = "入力ダイアログ";
    public const string SyntaxGroup = "書き方";
    public const string ScriptGroup = "スクリプトを実行する";

    public static IReadOnlyList<string> MacroGroups { get; } = [FileGroup, PathGroup, CursorGroup, CwdGroup, PromptGroup];

    public static bool IsMacro(MacroCatalogEntry entry) => MacroGroups.Contains(entry.Group);

    private static MacroCatalogEntry Macro(string group, string insert, string summary, string description) =>
        new(group, insert, insert, insert.Length, summary, description);

    public static IReadOnlyList<MacroCatalogEntry> Entries { get; } =
    [
        Macro(FileGroup, "${file}", "対象ファイルのフルパス（フォルダ除外）", "実効対象のファイルのパス。マークがあればマークしたファイルすべて（一覧の表示順に 1 件ずつ別の引数）、無ければカーソル位置のファイル。フォルダと「..」は含めない。"),
        Macro(FileGroup, "${fileBasenameNoExtension}", "対象ファイルの拡張子なしの名前", "${file} の、拡張子を除いた名前（パスなし）。例: 動画.avi → 動画"),
        Macro(FileGroup, "${fileExtname}", "対象ファイルの拡張子（. を含む）", "${file} の拡張子（. を含む）。例: 動画.avi → .avi"),

        Macro(PathGroup, "${path}", "対象のフルパス（フォルダも含む）", "実効対象の項目のパス。${file} と違いフォルダも含める。「..」はカレントフォルダになる。"),
        Macro(PathGroup, "${pathBase}", "対象の拡張子なしのパス（フォルダはそのまま）", "実効対象の拡張子を除いたフルパス。フォルダはそのまま、「..」はカレントフォルダ。"),
        // R-144
        Macro(PathGroup, "${pathBasename}", "対象の名前（フォルダも含む）", "実効対象の項目の名前（パスなし）。${path} と同じ対象で、フォルダも含める。「..」なら空。例: .\\${pathBasename} でカレントフォルダからの相対パスになる（対象がカレントフォルダ直下のときだけ。ツリーで選んだフォルダなど、カレントフォルダの外の対象でも名前だけを返す）"),
        Macro(PathGroup, "${pathBasenameNoExtension}", "対象の拡張子なしの名前（フォルダはそのまま）", "${pathBasename} から拡張子を除いた名前。フォルダはそのまま。「..」なら空。"),

        Macro(CursorGroup, "${cursorFile}", "カーソルのファイルのフルパス", "マークに関係なく、カーソル位置のファイルだけ。フォルダと「..」なら空。"),
        Macro(CursorGroup, "${cursorFileBasenameNoExtension}", "カーソルのファイルの拡張子なしの名前", "カーソル位置のファイルの拡張子を除いた名前。フォルダと「..」なら空。"),
        Macro(CursorGroup, "${cursorPath}", "カーソルの項目のフルパス", "マークに関係なく、カーソル位置の項目だけ。「..」ならカレントフォルダ。"),
        Macro(CursorGroup, "${cursorPathBase}", "カーソルの拡張子なしのパス（フォルダはそのまま）", "カーソル位置の拡張子を除いたフルパス。フォルダはそのまま、「..」はカレントフォルダ。"),
        Macro(CursorGroup, "${cursorPathBasename}", "カーソルの項目の名前", "マークに関係なく、カーソル位置の項目の名前。「..」なら空。.\\ を付けた相対パスは、項目がカレントフォルダ直下のときだけ正しい。"),
        Macro(CursorGroup, "${cursorPathBasenameNoExtension}", "カーソルの項目の拡張子なしの名前", "カーソル位置の項目の拡張子を除いた名前。フォルダはそのまま。「..」なら空。"),

        Macro(CwdGroup, "${cwd}", "実行時のフォルダのフルパス", "カレントフォルダ。"),
        // R-137
        Macro(CwdGroup, "${cwdBasename}", "実行時のフォルダ名", "カレントフォルダの名前だけ。末尾の区切りを除き、ドットは名前に残す。ドライブルートは C:、共有ルートは共有名。"),

        // R-130
        Macro(PromptGroup, "${prompt}", "起動前に入力ダイアログを表示",
            "起動の前に入力ダイアログを出す。項目（テキスト・フォルダ・ファイル・チェックボックス・ドロップダウンリスト）と送る順は「入力ダイアログ編集」で設定する。引数欄に 1 つだけ、それだけで 1 つの引数として書く。設定していなければ、ツールの名前のテキストを 1 つ出す。"),

        new(SyntaxGroup, "!（マクロの直後）", "!", 1,
            "マクロが空なら起動を中止",
            "マクロが空になったら、ツールを起動しない。例: ${file}!（フォルダの上では何もしない）"),
        new(SyntaxGroup, "\"…\"（引用符で囲む）", null, 0,
            "空のマクロを含む引数を残す",
            "マクロが空になっても、空の値 \"\" として渡す。囲まなければ、その引数ごと取り除く。パスに空白があっても、引用符の有無にかかわらず 1 つの引数として渡る。! と両方付けたら ! が優先。"),
        new(SyntaxGroup, "$$", "$$", 2,
            "ドル記号を文字どおり書く",
            "$ そのものを書く。${ で始まらない $（$env:PATH など）はそのまま渡るので、$${env:PATH} のように ${ を書きたいときだけ使う。"),
        new(SyntaxGroup, "作業ディレクトリ", null, 0,
            "ツールが動くフォルダ",
            "ツールは常にカレントフォルダを作業ディレクトリとして起動する。"),

        new(ScriptGroup, "書き方の注意", null, 0,
            "スクリプトの場所と実行結果の表示",
            "スクリプトはパスではなく引数に書く。スクリプトの場所はフルパスで書く（作業ディレクトリはカレントフォルダなので、相対パスでは見つからない）。結果を見たいなら「終了後もウィンドウを閉じない」を ON にする。"),
        new(ScriptGroup, "Python", null, 0,
            "Python スクリプトの実行",
            "パス: py.exe\r\n引数: \"C:\\tools\\script.py\" ${file}\r\n\r\npy.exe は Windows 版の Python に付いてくる起動役で、スクリプトの 1 行目を見て使う Python を選ぶ。使う Python が決まっているなら、パスに python.exe を書いてもよい。どちらの場合も、パスに .py は書けない。"),
        new(ScriptGroup, "Python（仮想環境）", null, 0,
            "仮想環境の Python で実行",
            "パス: C:\\work\\.venv\\Scripts\\python.exe\r\n引数: \"C:\\work\\script.py\" ${file}"),
        new(ScriptGroup, "PowerShell 7", null, 0,
            "PowerShell 7 のスクリプトを実行",
            "パス: pwsh.exe\r\n引数: -File \"C:\\tools\\build.ps1\" ${file}"),
        new(ScriptGroup, "Windows PowerShell", null, 0,
            "Windows PowerShell のスクリプトを実行",
            "パス: powershell.exe\r\n引数: -ExecutionPolicy Bypass -File \"C:\\tools\\build.ps1\" ${file}"),
        new(ScriptGroup, "Node.js", null, 0,
            "JavaScript のスクリプトを実行",
            "パス: node.exe\r\n引数: \"C:\\tools\\script.js\" ${file}"),
        new(ScriptGroup, "バッチファイル（.bat / .cmd）", null, 0,
            "バッチファイルの実行とパスの注意",
            "パスにそのまま書けば実行される。%CD% はカレントフォルダで、バッチファイル自身の場所は %~dp0。受け取った引数は %~1 で引用符を外して使う。カレントフォルダがネットワーク上（\\\\サーバー\\共有）だと cmd.exe は C:\\Windows で動くので、バッチの中で pushd する。ファイル名に % を含むと cmd.exe が展開してしまうことがある。"),
    ];
    /// <summary>R-138: 除外と欄ごとの文法の案内を全入口で共有する。</summary>
    public static IReadOnlyList<MacroCatalogEntry> For(MacroUsage usage) =>
        [.. Entries.Where(e => (usage == MacroUsage.Arguments || e.Insert != "${prompt}")
            && (usage is MacroUsage.Arguments or MacroUsage.PromptArgument || e.Group != ScriptGroup))
            .Select(e => usage == MacroUsage.InitialValue && e.Label.Contains("引用符", StringComparison.Ordinal)
                ? e with { Summary = "空白と引用符はそのままの文字",
                    Description = "初期値では、空白・タブ・引用符はそのまま文字として残ります。引数への分割や引用符の除去は行いません。" }
                : e)];

    public static string ContextHelp(MacroUsage usage) =>
        "対象はマークした項目。マークがなければカーソルの項目。"
        + (usage == MacroUsage.InitialValue ? "\r\n複数の対象があるマクロは先頭の 1 件を使います。" : "");
}
