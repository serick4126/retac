using ReTAC.Domain.Entries;

namespace ReTAC.Domain.Tools;

/// <param name="Title">例の見出し</param>
/// <param name="Note">何をする設定か・なぜその ON/OFF か</param>
/// <param name="Targets">例の対象（マークした項目、またはカーソルの 1 件）。対象の項目を使わない例は空</param>
/// <param name="Input">入力ダイアログに入れる例の値（項目の番号ごと）。入力ダイアログを使わない例は空</param>
/// <param name="Expected">起動 1 回ぶんの、展開後の引数。テストがこの値と実際の展開を突き合わせる</param>
public sealed record ToolExample(
    string Title,
    string Note,
    ExternalTool Tool,
    IReadOnlyList<Entry> Targets,
    Entry? Cursor,
    IReadOnlyDictionary<int, PromptValue> Input,
    IReadOnlyList<string> Expected);

/// <summary>
/// R-145-2: マクロ構築プロンプトに載せる設定例。型の違う例に絞る（R-145）。
/// 例は文字ではなく設定として持ち、テストで解析・入力ダイアログの検査・展開した結果を確かめる。
/// 文字で持つと、構文は正しいまま送る値が変わる誤り（-m の後の空白を落とすなど）に気づけないため。
/// </summary>
public static class ExternalToolExamples
{
    public const string CurrentFolder = @"C:\work";

    private static readonly IReadOnlyDictionary<int, PromptValue> NoInput = new Dictionary<int, PromptValue>();

    private static Entry File(string name) => Entry.ForFile(CurrentFolder + @"\" + name, name, FileAttributes.Normal, 0, default);
    private static Entry Folder(string name) => Entry.ForFolder(CurrentFolder + @"\" + name, name, FileAttributes.Directory, default);

    public static IReadOnlyList<ToolExample> All { get; } = Build();

    private static List<ToolExample> Build()
    {
        var a = File("a.txt");
        var b = File("b.txt");
        var report = File("report.csv");
        var sub = Folder("sub");
        var archive = File("data.zip");

        return
        [
            new("テキスト エディタでマークしたファイルを開く",
                "${file} はマークしたファイルを 1 件ずつ別の引数で渡す。まとめて 1 回で開くので「マークした項目ごとに起動する」は OFF。",
                new ExternalTool { Name = "メモ帳", Path = "notepad.exe", Arguments = "${file}" },
                [a, b], a, NoInput,
                [@"C:\work\a.txt", @"C:\work\b.txt"]),

            new("Python スクリプトにファイルを 1 件ずつ渡す",
                "スクリプトはパスではなく引数にフルパスで書く。! でファイルが無いときは起動しない。1 件ずつ順に処理するので「マークした項目ごとに起動する」を ON、結果を読むので「終了後もウィンドウを閉じない」を ON。",
                new ExternalTool
                {
                    Name = "CSV を変換", Path = "py.exe", Arguments = "\"C:\\tools\\convert.py\" ${file}!",
                    LaunchPerItem = true, KeepWindowOpen = true,
                },
                [report], report, NoInput,
                [@"C:\tools\convert.py", @"C:\work\report.csv"]),

            new("マークしたフォルダとファイルを相対パスで ZIP に圧縮する",
                "ツールはカレントフォルダで起動するので、.\\${pathBasename} でカレントフォルダからの相対パスになる（対象がカレントフォルダ直下のときだけ正しい）。書庫の名前はカレントフォルダ名。1 回でまとめて渡すので「マークした項目ごとに起動する」は OFF。",
                new ExternalTool
                {
                    Name = "ZIP に圧縮", Path = @"C:\Windows\System32\tar.exe",
                    Arguments = "-a -cf \"${cwdBasename}.zip\" .\\${pathBasename}!",
                },
                [sub, a], sub, NoInput,
                ["-a", "-cf", "work.zip", @".\sub", @".\a.txt"]),

            new("カレントフォルダで Windows Terminal を開く",
                "${cwd} はカレントフォルダ。対話型のシェルは自分で終わらないので「終了後もウィンドウを閉じない」は不要。",
                new ExternalTool { Name = "ターミナル", Path = "wt.exe", Arguments = "-d ${cwd}" },
                [], null, NoInput,
                ["-d", @"C:\work"]),

            new("入力ダイアログでメッセージを入れて git commit する",
                "メッセージはテキストの項目で入力する。前に付ける文字を「-m 」（末尾に空白）にすると、-m とメッセージが別の引数になる。必須にして空のコミットを防ぐ。結果を読むので「終了後もウィンドウを閉じない」を ON、ページャーを止めるため --no-pager を先頭に置く。",
                new ExternalTool
                {
                    Name = "git commit", Path = "git.exe", Arguments = "--no-pager commit ${prompt}",
                    KeepWindowOpen = true,
                    Prompt = new PromptDefinition
                    {
                        Title = "コミット",
                        Items = [new PromptItem { Id = 1, Kind = PromptItemKind.Text, Label = "メッセージ", Prefix = "-m ", Required = true }],
                        Arguments = [PromptArgument.Item(1)],
                    },
                },
                [a], a,
                new Dictionary<int, PromptValue> { [1] = new("見出しを直す") },
                ["--no-pager", "commit", "-m", "見出しを直す"]),

            new("入力ダイアログで展開先と上書きを選んで WinRAR で展開する",
                "表示する順（展開先・上書き・バックグラウンド）と送る順（x・上書き・バックグラウンド・書庫・展開先）を別々に決める。引数欄は ${prompt} だけにし、書庫はマクロを含む引数の行 ${file} で間に挟む。展開先の後ろに \\ を付けてフォルダとして渡す。",
                new ExternalTool
                {
                    Name = "WinRAR で展開", Path = @"C:\Program Files\WinRAR\WinRAR.exe", Arguments = "${prompt}",
                    Prompt = new PromptDefinition
                    {
                        Title = "展開",
                        Items =
                        [
                            new PromptItem { Id = 1, Kind = PromptItemKind.Folder, Label = "展開先", Initial = "${cwd}", Suffix = "\\" },
                            new PromptItem { Id = 2, Kind = PromptItemKind.CheckBox, Label = "上書きする", Value = "-o+" },
                            new PromptItem { Id = 3, Kind = PromptItemKind.CheckBox, Label = "バックグラウンドで実行", Value = "-ibck", InitialChecked = true },
                        ],
                        Arguments =
                        [
                            PromptArgument.Fixed("x"), PromptArgument.Item(2), PromptArgument.Item(3),
                            PromptArgument.Template("${file}"), PromptArgument.Item(1),
                        ],
                    },
                },
                [archive], archive,
                new Dictionary<int, PromptValue> { [1] = new(@"D:\展開先"), [2] = new(Checked: false), [3] = new(Checked: true) },
                ["x", "-ibck", @"C:\work\data.zip", @"D:\展開先\"]),
        ];
    }
}
