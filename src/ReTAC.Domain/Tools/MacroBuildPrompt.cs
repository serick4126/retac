using System.Text;

namespace ReTAC.Domain.Tools;

/// <summary>
/// R-145: 外部ツールのマクロ構築プロンプト。使いたいコマンドと一緒に AI に送り、外部ツールの設定を組み立ててもらうための Markdown。
/// その時点の実装（マクロの一覧・説明のカタログ・設定例）から作るので、マクロや欄を足せば自動で載る（R-145-2）。
/// 利用者の今の設定は受け取らない（パスやフォルダ名を AI に渡さないため）。
/// </summary>
public static partial class MacroBuildPrompt
{
    public const string Title = "ReTAC 外部ツールのマクロ構築プロンプト";

    /// <summary>R-145 の節の見出し。並びはこの順（テストが確かめる）。</summary>
    public static IReadOnlyList<string> SectionHeadings { get; } =
    [
        "1. AI への指示",
        "2. ReTAC の言葉",
        "3. 外部ツールの設定",
        "4. マクロ",
        "5. 入力ダイアログ",
        "6. スクリプトを実行するとき",
        "7. 設定例",
    ];

    /// <param name="createdAt">作った日時（呼び出し側の現地時刻）</param>
    /// <param name="version">ReTAC の版</param>
    /// <param name="settingsMenuPath">設定画面の開き方。メニューの文言を持つ App が渡す（例: 「設定」メニューの「外部ツールの設定」）</param>
    public static string Generate(DateTime createdAt, string version, string settingsMenuPath)
    {
        var md = new StringBuilder();
        void Line(string text = "") => md.Append(text).Append('\n');

        Line($"# {Title}");
        Line();
        Line($"> 作成: {createdAt:yyyy-MM-dd HH:mm} ／ ReTAC {version}");
        Line(">");
        Line("> **利用者の方へ:** このプロンプトと、使いたいコマンドやしたいことを AI に送ると、ReTAC の外部ツールの設定を組み立ててもらえます。");
        Line();
        Line("ReTAC は Windows 用のファイラーです。外部ツールは、ReTAC から任意のプログラムを起動する機能で、ファイルリストで選んだ項目をマクロで引数に渡せます。");
        Line("以下は、AI が外部ツールの設定を組み立てるための資料です。この文書は ReTAC がその時点の実装から作っています。");
        Line();

        WriteInstructions(Line, settingsMenuPath);
        WriteTerms(Line);
        WriteSettings(Line, settingsMenuPath);
        WriteMacros(Line);
        WritePromptDialog(Line);
        WriteScripts(Line);
        WriteExamples(Line);
        return md.ToString().TrimEnd('\n') + "\n";
    }

    private static void WriteInstructions(Action<string> line, string settingsMenuPath)
    {
        line($"## {SectionHeadings[0]}");
        line("");
        line("あなたの役割は、利用者が実行したいコマンドやしたいことから、ReTAC の外部ツールの設定を組み立てることです。次の順に進めてください。");
        line("");
        line("1. **何を実行したいかを確かめる。** 使いたいコマンドが、このプロンプトと一緒にも、それより前にも送られていなければ、まず何を実行したいかを利用者に聞いてください。コマンドの形でなくても、使いたいツールとしたいこと（例:「マークしたファイルを 7-Zip で圧縮したい」）が分かれば足ります。");
        line("2. **起動の仕方を必ず確かめる。** 組み立てる前に、次のどちらにするかを利用者に聞いてください。");
        line("   - **直接実行する**: 引数を固定し、対象はマクロで渡す。起動のたびに何も入力しない。");
        line("   - **入力ダイアログ（GUI）を使う**: 起動の前にダイアログを出し、文字・フォルダ・ファイル・チェックボックス・選択肢を入力してから起動する。");
        line("   - どちらが合うか分からない利用者には、両方の違いと、そのコマンドならどちらが向くかを先に説明してください。");
        line($"   - 入力ダイアログを選んだら、「{SectionHeadings[4]}」の項目の種類と各欄の意味、「{ExternalToolHelp.PromptEditButton}」の画面の使い方を説明しながら組み立ててください。");
        line("3. **分からないことは推測しない。** ツールのパス（インストール先）、対象がファイルかフォルダか、マークした項目をまとめて 1 回で渡すか 1 件ずつ起動するか、などが決められないときは利用者に聞いてください。");
        line("4. **ツールの書き方は公式の資料を優先する。** オプションの意味や順番は、そのツールの公式の説明に従ってください。");
        line($"5. **使えるマクロは「{SectionHeadings[3]}」の表にあるものだけ。** 名前は VS Code の変数に似せてありますが、表に無いもの（`${{relativeFile}}`・`${{fileBasename}}`・`${{workspaceFolder}}` など）は使えません。知らない名前があると設定を保存できません。");
        line("6. **下の「答えの形」で答える。** オプションを ON にする理由や、確かめてほしいことも添えてください。");
        line("");
        line("### 答えの形");
        line("");
        line("利用者は、この答えを見ながら設定画面に手で入れます。引数はそのままコピーできるようにコードブロックで書いてください。");
        line("");
        line("````text");
        line("名前: （メニューに出す名前）");
        line("パス: （実行ファイル）");
        line("引数:");
        line("```");
        line("（引数欄にそのまま貼る文字）");
        line("```");
        line("オプション:");
        foreach (var option in ExternalToolHelp.Options) line($"  {option.Label}: ON / OFF");
        line("入力ダイアログ: 使わない / 使う（最初に利用者に確かめた結果。使うなら下の表）");
        line("  タイトル・前回の入力を初期値にする");
        line("  項目（表示順）: 種類・ラベル・使う欄の値");
        line("  引数の並び（送る順）: 固定の引数 / マクロを含む引数 / 項目");
        line($"設定の手順: {settingsMenuPath}を開き、どこに何を入れるか");
        line("注意: ON / OFF の理由、確かめてほしいこと");
        line("````");
        line("");
    }

    private static void WriteTerms(Action<string> line)
    {
        line($"## {SectionHeadings[1]}");
        line("");
        line("| 言葉 | 意味 |");
        line("|---|---|");
        foreach (var term in ExternalToolHelp.Terms) line($"| {Cell(term.Label == ".." ? "`..`" : term.Label)} | {Cell(term.Description)} |");
        line("");
    }

    private static void WriteSettings(Action<string> line, string settingsMenuPath)
    {
        line($"## {SectionHeadings[2]}");
        line("");
        line($"{settingsMenuPath}を開き、左の一覧の「追加」でツールを足して、右の欄を入れます。最後に設定画面の「OK」または「適用」で保存します。");
        line("ツールは常にカレントフォルダを作業ディレクトリとして起動します。");
        line("");
        line("### 欄");
        line("");
        line("| 欄 | 説明 |");
        line("|---|---|");
        foreach (var field in ExternalToolHelp.ToolFields) line($"| {Cell(field.Label)} | {Cell(field.Description)} |");
        line("");
        line("### オプション");
        line("");
        line("| オプション | ON | OFF | ON にするとき |");
        line("|---|---|---|---|");
        foreach (var option in ExternalToolHelp.Options)
            line($"| {Cell(option.Label)} | {Cell(option.On)} | {Cell(option.Off + (option.Note.Length > 0 ? " " + option.Note : ""))} | {Cell(option.When)} |");
        line("");
    }

    private static void WriteMacros(Action<string> line)
    {
        line($"## {SectionHeadings[3]}");
        line("");
        line("引数欄に書くと、起動のときに値に置き換わります。名前は大文字・小文字を区別します。知らない名前は誤りになります。");
        line("");
        foreach (var group in MacroCatalog.Entries.Where(MacroCatalog.IsMacro).GroupBy(e => e.Group))
        {
            line($"### {group.Key}");
            line("");
            line("| マクロ | 要約 | 詳細 |");
            line("|---|---|---|");
            foreach (var entry in group) line($"| `{entry.Insert}` | {Cell(entry.Summary)} | {Cell(entry.Description)} |");
            line("");
        }

        line("### 書き方の規則");
        line("");
        line("| 書き方 | 意味 |");
        line("|---|---|");
        // R-145-2: 規則の文はマクロの一覧と同じカタログから取る。ここに別の文を書かない
        foreach (var entry in MacroCatalog.Entries.Where(e => e.Group == MacroCatalog.SyntaxGroup))
            line($"| {Cell(entry.Label)} | {Cell(entry.Description)} |");
        line("");
    }

    private static void WritePromptDialog(Action<string> line)
    {
        line($"## {SectionHeadings[4]}");
        line("");
        line($"引数欄に `${{prompt}}` を書くと、起動の前に 1 つのダイアログで入力を求めます。キャンセルすると起動しません。中身は、設定画面の「{ExternalToolHelp.PromptEditButton}」で組み立てます。");
        line("`${prompt}` は前後に文字を付けず、引用符で囲まず、それだけで 1 つの引数として書きます。");
        line("");
        line("### 欄");
        line("");
        line("| 欄 | 説明 |");
        line("|---|---|");
        foreach (var field in ExternalToolHelp.PromptFields) line($"| {Cell(field.Label)} | {Cell(field.Description)} |");
        line("");
        line("### 種類と値");
        line("");
        line("| 名前 | 説明 |");
        line("|---|---|");
        foreach (var value in ExternalToolHelp.PromptValues) line($"| {Cell(value.Label)} | {Cell(value.Description)} |");
        line("");
        line("### マクロを展開する欄");
        line("");
        line("入力ダイアログでマクロを展開するのは、次の表で「展開する」の欄だけです。利用者がダイアログに入力した文字と前回の入力は、マクロとして解釈しません。");
        line("");
        line("| 欄 | マクロ |");
        line("|---|---|");
        foreach (var field in ExternalToolHelp.PromptFields.Where(f => f.Macros != MacroUse.NotText))
            line($"| {Cell(field.Label)} | {MacroUseText(field.Macros)} |");
        line("");
        line("### 組み立て方");
        line("");
        line($"1. 設定画面でツールを選び、「{ExternalToolHelp.PromptEditButton}」を押す。");
        line("2. 上の「表示する項目」の「追加 ▼」で種類を選び、ラベルと種類に応じた欄を入れる。作った項目は、下の「引数」の末尾にも追加される。");
        line("3. 固定のオプションやマクロを挟むときは、下の「追加 ▼」で「固定の引数...」または「マクロを含む引数...」を選ぶ。");
        line("4. 上下それぞれの「上へ」「下へ」で、表示する順と送る順を整える。");
        line("5. 「プレビュー」で実際のダイアログを確かめ、「OK」で閉じる。新しく作ったときは、引数欄に `${prompt}` が自動で入る。");
        line("");
        line("「インポート」にコマンドライン全体を貼り付けると、パスと引数が固定の引数の行として読み込まれます。行を選んで「変換 ▼」を押すと、5 種類の項目や `${file}` などに変えられます。続いた複数の行は 1 つのチェックボックスにまとめられます。利用者がふだん打っているコマンドラインがあるなら、この手順を案内すると早く組み立てられます。");
        line("");
    }

    private static void WriteScripts(Action<string> line)
    {
        line($"## {SectionHeadings[5]}");
        line("");
        foreach (var entry in MacroCatalog.Entries.Where(e => e.Group == MacroCatalog.ScriptGroup))
        {
            line($"### {entry.Label}");
            line("");
            // 「パス: …」「引数: …」の段落は設定の値なので、表示で \ が消えないようコードブロックにする
            foreach (var paragraph in entry.Description.Replace("\r\n", "\n").Split("\n\n"))
            {
                if (paragraph.StartsWith("パス:", StringComparison.Ordinal))
                {
                    line("```text");
                    line(paragraph);
                    line("```");
                }
                else
                {
                    line(Prose(paragraph));
                }
                line("");
            }
        }
    }

    private static void WriteExamples(Action<string> line)
    {
        line($"## {SectionHeadings[6]}");
        line("");
        line($"答えの形の手本です。例の対象はカレントフォルダ `{ExternalToolExamples.CurrentFolder}` の中の項目です。パスはインストール先に合わせて読み替えます。");
        line("");
        foreach (var example in ExternalToolExamples.All)
        {
            var tool = example.Tool;
            line($"### {example.Title}");
            line("");
            line(Prose(example.Note));
            line("");
            line("````text");
            line($"名前: {tool.Name}");
            line($"パス: {tool.Path}");
            line("引数:");
            line("```");
            line(tool.Arguments);
            line("```");
            line("オプション:");
            line($"  {ExternalToolHelp.LaunchPerItem.Label}: {OnOff(tool.LaunchPerItem)}");
            line($"  {ExternalToolHelp.ShowInPopup.Label}: {OnOff(tool.ShowInPopup)}");
            line($"  {ExternalToolHelp.KeepWindowOpen.Label}: {OnOff(tool.KeepWindowOpen)}");
            line($"  {ExternalToolHelp.ConfirmBeforeRun.Label}: {OnOff(tool.ConfirmBeforeRun)}");
            if (tool.Prompt is { } prompt)
            {
                line($"入力ダイアログ: 使う（タイトル: {prompt.Title}、前回の入力を初期値にする: {OnOff(prompt.RememberLast)}）");
                line("  項目（表示順）:");
                for (var i = 0; i < prompt.Items.Count; i++) line($"    {i + 1}. {DescribeItem(prompt.Items[i])}");
                line("  引数の並び（送る順）:");
                for (var i = 0; i < prompt.Arguments.Count; i++) line($"    {i + 1}. {DescribeArgument(prompt, prompt.Arguments[i])}");
            }
            else
            {
                line("入力ダイアログ: 使わない");
            }
            line("````");
            line("");
            var targets = example.Targets.Count == 0 ? "なし（この例では対象の項目を使いません）" : string.Join("・", example.Targets.Select(t => t.Name));
            line($"例の対象: {targets}" +(example.Input.Count > 0 && tool.Prompt is { } p ? "、入力: " + DescribeInput(p, example.Input) : ""));
            line("");
            line($"渡る引数: `{string.Join(" ", example.Expected.Select(LaunchRequest.Quote))}`");
            line("");
        }
    }

    private static string DescribeItem(PromptItem item)
    {
        var kind = ExternalToolHelp.PromptValues.First(v => v.Key == $"PromptItemKind.{item.Kind}").Label;
        var parts = new List<string> { $"{kind}「{item.Label}」" };
        if (item.Initial.Length > 0) parts.Add($"初期値: {item.Initial}");
        if (item.Prefix.Length > 0) parts.Add($"前に付ける: \"{item.Prefix}\"");
        if (item.Suffix.Length > 0) parts.Add($"後ろに付ける: \"{item.Suffix}\"");
        if (item.Required) parts.Add("入力を必須にする: ON");
        if (item.Kind == PromptItemKind.CheckBox)
        {
            parts.Add($"送る値: {item.Value}");
            parts.Add($"初期値をオンにする: {OnOff(item.InitialChecked)}");
        }
        foreach (var choice in item.Choices)
            parts.Add($"選択肢「{choice.Label}」送る値: {choice.Value}" + (choice.Id == item.InitialChoiceId ? "（初期）" : ""));
        return string.Join("、", parts);
    }

    private static string DescribeArgument(PromptDefinition prompt, PromptArgument argument) => argument.Kind switch
    {
        PromptArgumentKind.Fixed => $"固定の引数: {argument.Text}",
        PromptArgumentKind.Template => $"マクロを含む引数: {argument.Text}",
        _ => $"項目「{prompt.ItemOf(argument.ItemId)?.Label}」",
    };

    private static string DescribeInput(PromptDefinition prompt, IReadOnlyDictionary<int, PromptValue> input) =>
        string.Join("、", prompt.Items.Where(i => input.ContainsKey(i.Id)).Select(i =>
        {
            var value = input[i.Id];
            return i.Kind == PromptItemKind.CheckBox ? $"{i.Label}={OnOff(value.Checked)}" : $"{i.Label}=`{value.Text}`";
        }));

    private static string OnOff(bool on) => on ? "ON" : "OFF";

    private static string MacroUseText(MacroUse use) => use switch
    {
        MacroUse.Expanded => "展開する（`${prompt}` は書けない）",
        MacroUse.Literal => "展開しない（書いたとおりの文字。`${cwd}` と書いても `${cwd}` のまま渡る）",
        _ => "固定の引数は展開しない。マクロを含む引数は展開する（`${prompt}` は書けない）",
    };

    /// <summary>
    /// <c>\</c> か <c>${</c> を含むまとまり（パス・マクロ）をコード表記にする。
    /// Markdown として表示すると、コード表記の外の <c>\</c> は直後の記号を逃がす印として消える（<c>.\${x}</c> が <c>.${x}</c> になる）。
    /// AI に貼る文字は変えたくないので、<c>\\</c> と書き換えて逃がすのではなく、コード表記で包む。
    /// </summary>
    public static string Prose(string text) =>
        CodeRun().Replace(text, m => $"`{m.Value}`");

    [System.Text.RegularExpressions.GeneratedRegex(@"[^\s、。「」（）`]*(?:\\|\$\{)[^\s、。「」（）`]*")]
    private static partial System.Text.RegularExpressions.Regex CodeRun();

    /// <summary>表のセルの中で崩れる文字を逃がす。</summary>
    private static string Cell(string text) => Prose(text).Replace("|", "\\|").Replace("\r\n", "<br>").Replace("\n", "<br>");
}
