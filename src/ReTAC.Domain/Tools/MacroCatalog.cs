namespace ReTAC.Domain.Tools;

/// <param name="Insert">引数欄に挿入する文字。挿入しない項目（書き方の説明・例）は null</param>
/// <param name="CaretOffset">挿入後にカーソルを置く位置（挿入した文字の先頭から）</param>
public sealed record MacroCatalogEntry(string Group, string Label, string? Insert, int CaretOffset, string Description);

/// <summary>
/// 外部ツールの設定ダイアログから開くマクロの一覧（F-04）。
/// 利用者自身も名前と使い方を覚えきれないので、選んで挿入できるようにする（予定 §3.1.1）。
/// スクリプトの例は例であって既定値ではない（予定 §1.4 A-4）。
/// </summary>
public static class MacroCatalog
{
    public const string MacroGroup = "マクロ";
    public const string SyntaxGroup = "書き方";
    public const string ScriptGroup = "スクリプトを実行する";

    private static MacroCatalogEntry Macro(string insert, string description) =>
        new(MacroGroup, insert, insert, insert.Length, description);

    public static IReadOnlyList<MacroCatalogEntry> Entries { get; } =
    [
        Macro("${file}", "実効対象のファイルのパス。マークがあればマークしたファイルすべて（一覧の表示順に 1 件ずつ別の引数）、無ければカーソル位置のファイル。フォルダと「..」は含めない。"),
        Macro("${path}", "実効対象の項目のパス。${file} と違いフォルダも含める。「..」はカレントフォルダになる。"),
        Macro("${fileBasenameNoExtension}", "${file} の、拡張子を除いた名前（パスなし）。例: 動画.avi → 動画"),
        Macro("${fileExtname}", "${file} の拡張子（. を含む）。例: 動画.avi → .avi"),
        Macro("${cursorFile}", "マークに関係なく、カーソル位置のファイルだけ。フォルダと「..」なら空。"),
        Macro("${cursorPath}", "マークに関係なく、カーソル位置の項目だけ。「..」ならカレントフォルダ。"),
        Macro("${cwd}", "カレントフォルダ。"),
        new(MacroGroup, "${prompt:タイトル}{既定値}", "${prompt:}{}", "${prompt:".Length,
            "起動の前に入力ダイアログを出し、入力した文字列に置き換える。タイトルと既定値は省略できる（タイトルを省くとツールの名前）。複数置くと左から順に聞き、1 つでもキャンセルすると起動しない。"),

        new(SyntaxGroup, "!（マクロの直後）", "!", 1,
            "マクロが空になったら、ツールを起動しない。例: ${file}!（フォルダの上では何もしない）"),
        new(SyntaxGroup, "\"…\"（引用符で囲む）", null, 0,
            "マクロが空になっても、空の値 \"\" として渡す。囲まなければ、その引数ごと取り除く。パスに空白があっても、引用符の有無にかかわらず 1 つの引数として渡る。! と両方付けたら ! が優先。"),
        new(SyntaxGroup, "$$", "$$", 2,
            "$ そのものを書く。${ で始まらない $（$env:PATH など）はそのまま渡るので、$${env:PATH} のように ${ を書きたいときだけ使う。"),
        new(SyntaxGroup, "作業ディレクトリ", null, 0,
            "ツールは常にカレントフォルダを作業ディレクトリとして起動する。"),

        new(ScriptGroup, "書き方の注意", null, 0,
            "スクリプトはパスではなく引数に書く。スクリプトの場所はフルパスで書く（作業ディレクトリはカレントフォルダなので、相対パスでは見つからない）。結果を見たいなら「終了後もウィンドウを閉じない」を ON にする。"),
        new(ScriptGroup, "Python", null, 0,
            "パス: py.exe\r\n引数: \"C:\\tools\\script.py\" ${file}\r\n\r\npy.exe は Windows 版の Python に付いてくる起動役で、スクリプトの 1 行目を見て使う Python を選ぶ。使う Python が決まっているなら、パスに python.exe を書いてもよい。どちらの場合も、パスに .py は書けない。"),
        new(ScriptGroup, "Python（仮想環境）", null, 0,
            "パス: C:\\work\\.venv\\Scripts\\python.exe\r\n引数: \"C:\\work\\script.py\" ${file}"),
        new(ScriptGroup, "PowerShell 7", null, 0,
            "パス: pwsh.exe\r\n引数: -File \"C:\\tools\\build.ps1\" ${file}"),
        new(ScriptGroup, "Windows PowerShell", null, 0,
            "パス: powershell.exe\r\n引数: -ExecutionPolicy Bypass -File \"C:\\tools\\build.ps1\" ${file}"),
        new(ScriptGroup, "Node.js", null, 0,
            "パス: node.exe\r\n引数: \"C:\\tools\\script.js\" ${file}"),
        new(ScriptGroup, "バッチファイル（.bat / .cmd）", null, 0,
            "パスにそのまま書けば実行される。%CD% はカレントフォルダで、バッチファイル自身の場所は %~dp0。受け取った引数は %~1 で引用符を外して使う。カレントフォルダがネットワーク上（\\\\サーバー\\共有）だと cmd.exe は C:\\Windows で動くので、バッチの中で pushd する。ファイル名に % を含むと cmd.exe が展開してしまうことがある。"),
    ];
}
