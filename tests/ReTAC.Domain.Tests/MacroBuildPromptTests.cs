using System.Reflection;
using System.Text;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

/// <summary>R-145 / R-145-2: 外部ツールのマクロ構築プロンプト。</summary>
public class MacroBuildPromptTests
{
    private const string MenuPath = "「設定」メニューの「外部ツールの設定」";
    private static readonly DateTime At = new(2026, 10, 7, 12, 34, 0);

    private static string Prompt() => MacroBuildPrompt.Generate(At, "3.4.0", MenuPath);

    [Fact]
    public void HeadingsInOrder_WithDateAndVersion()
    {
        var text = Prompt();
        Assert.StartsWith($"# {MacroBuildPrompt.Title}\n", text);
        Assert.Contains("2026-10-07 12:34", text);
        Assert.Contains("ReTAC 3.4.0", text);
        var positions = MacroBuildPrompt.SectionHeadings.Select(h => text.IndexOf($"\n## {h}\n", StringComparison.Ordinal)).ToList();
        Assert.All(positions, p => Assert.True(p > 0));
        Assert.Equal(positions.Order(), positions);
    }

    [Fact]
    public void Instructions_AskCommandAndLaunchStyleFirst()
    {
        var text = Prompt();
        var instructions = text[text.IndexOf("## 1.", StringComparison.Ordinal)..text.IndexOf("## 2.", StringComparison.Ordinal)];
        Assert.Contains("何を実行したいかを利用者に聞いて", instructions);
        Assert.Contains("直接実行する", instructions);
        Assert.Contains("入力ダイアログ（GUI）を使う", instructions);
        Assert.True(instructions.IndexOf("何を実行したいか", StringComparison.Ordinal) < instructions.IndexOf("起動の仕方", StringComparison.Ordinal));
        // 実機確認（2026-10-07）で、AI が返事を待たずに「とりあえず」の設定案を出した
        Assert.Contains("返事を待ってから", instructions);
        Assert.Contains("設定案を出さない", instructions);
        // 実機確認（2026-10-07）: 7z x に複数の書庫をまとめて渡す設定が返った
        Assert.Contains("1 回の起動で複数の対象を受け取れるか", instructions);
        Assert.Contains("入力ダイアログは最初に 1 回だけ", text);
        Assert.Contains(MenuPath, instructions);
        // VS Code の変数名を流用しているので、AI は表に無い ${relativeFile} などを作りがち
        Assert.Contains("表にあるものだけ", instructions);
        foreach (var name in new[] { "relativeFile", "fileBasename", "workspaceFolder" })
            Assert.DoesNotContain(name, ArgumentTemplate.Names.Keys);
    }

    [Fact]
    public void EveryMacroAppears()
    {
        var text = Prompt();
        foreach (var name in ArgumentTemplate.Names.Keys) Assert.Contains("`${" + name + "}`", text);
    }

    /// <summary>生成処理が表のセルに入れるときと同じ形。</summary>
    private static string AsCell(string text) => MacroBuildPrompt.Prose(text).Replace("|", "\\|").Replace("\r\n", "<br>").Replace("\n", "<br>");

    [Fact]
    public void MacroAndSyntaxDescriptionsAppear()
    {
        var text = Prompt();
        foreach (var entry in MacroCatalog.Entries.Where(e => e.Group != MacroCatalog.ScriptGroup))
            Assert.Contains(AsCell(entry.Description), text);
    }

    [Fact]
    public void ScriptNotesAppear_SettingsInCodeBlocks()
    {
        var text = Prompt();
        foreach (var entry in MacroCatalog.Entries.Where(e => e.Group == MacroCatalog.ScriptGroup))
            foreach (var paragraph in entry.Description.Replace("\r\n", "\n").Split("\n\n"))
                Assert.Contains(paragraph.StartsWith("パス:", StringComparison.Ordinal) ? $"```text\n{paragraph}\n```" : MacroBuildPrompt.Prose(paragraph), text);
    }

    // R-145-2: 書き方の規則は生成処理に直接書かず、マクロの一覧と同じカタログに持つ
    [Fact]
    public void SyntaxRulesComeFromCatalog()
    {
        var labels = MacroCatalog.Entries.Where(e => e.Group == MacroCatalog.SyntaxGroup).Select(e => e.Label).ToList();
        foreach (var rule in new[] { "引数の区切り", "複数の値", "混ぜられない組み合わせ", "長いパス" }) Assert.Contains(rule, labels);
        // 初期値は区切らず先頭 1 件を使うので、引数へ区切る欄だけの規則は初期値の一覧に出さない（R-136）
        Assert.DoesNotContain(MacroCatalog.For(MacroUsage.InitialValue), e => e.ArgumentsOnly);
        Assert.Contains(MacroCatalog.For(MacroUsage.ItemValue), e => e.ArgumentsOnly);
    }

    /// <summary>
    /// Markdown として表示したときに文字が消えないこと。コード表記の外の \ は直後の記号を逃がす印として消える
    /// （.\${pathBasename} が .${pathBasename} に、C:\work\.venv が C:\work.venv になる）。
    /// </summary>
    [Fact]
    public void BackslashesSurviveMarkdownRendering()
    {
        var text = Prompt();
        // 囲みは開いたときと同じ数のバッククォートだけの行で閉じる（答えの形は ```` の中に ``` を入れ子にしている）
        string? fence = null;
        foreach (var raw in text.Split('\n'))
        {
            if (fence is not null)
            {
                if (raw == fence) fence = null;
                continue;
            }
            if (raw.StartsWith("```", StringComparison.Ordinal))
            {
                fence = raw[..(raw.Length - raw.TrimStart('`').Length)];
                continue;
            }
            var outsideCode = System.Text.RegularExpressions.Regex.Replace(raw, "`[^`]*`", "").Replace("\\|", "");
            Assert.False(outsideCode.Contains('\\'), $"コード表記の外に \\ がある: {raw}");
        }
        Assert.Contains("`.\\${pathBasename}`", text);
        Assert.Contains("```text\nパス: C:\\work\\.venv\\Scripts\\python.exe", text);
        Assert.Contains("`\\\\サーバー\\共有`", text);
    }

    // R-136: 入力ダイアログの文字の欄は、マクロを展開するかどうかを必ず分類し、プロンプトに出す
    [Theory]
    [InlineData(typeof(PromptDefinition))]
    [InlineData(typeof(PromptItem))]
    [InlineData(typeof(PromptChoice))]
    [InlineData(typeof(PromptArgument))]
    public void EveryPromptTextFieldDeclaresMacroUse(Type type)
    {
        var text = Prompt();
        var table = text[text.IndexOf("### マクロを展開する欄", StringComparison.Ordinal)..];
        var fields = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is not null && p.PropertyType == typeof(string) && !p.Name.StartsWith("Last", StringComparison.Ordinal));
        foreach (var field in fields)
        {
            var entry = ExternalToolHelp.PromptFields.Single(f => f.Key == $"{type.Name}.{field.Name}");
            Assert.NotEqual(MacroUse.NotText, entry.Macros);
            Assert.Contains($"| {entry.Label} |", table);
        }
        Assert.Equal(MacroUse.Literal, ExternalToolHelp.PromptFields.Single(f => f.Key == "PromptItem.Prefix").Macros);
        Assert.Equal(MacroUse.Expanded, ExternalToolHelp.PromptFields.Single(f => f.Key == "PromptItem.Initial").Macros);
    }

    // R-145-2: 設定画面の「?」は、カタログへ移す前の文言をそのまま保つ
    [Fact]
    public void OptionToolTipsKeepCurrentWording()
    {
        Assert.Equal("ON: マークした数だけ、1 件ずつ順に起動する。前の 1 件が終わってから次を起動する。\nOFF: 1 回だけ起動し、マークした項目をまとめて引数に渡す。", ExternalToolHelp.LaunchPerItem.ToolTip);
        Assert.Equal("ON: コマンド「ポップアップメニューの表示」の「外部ツール ▶」に出る（既定のキーは G）。\nOFF: 出ない。メニュー「ツール」とキー割り当てからは、どちらでも起動できる。", ExternalToolHelp.ShowInPopup.ToolTip);
        Assert.Equal("ON: ツールが終わってもコンソールを閉じず、キーを押すまで結果を残す。\nOFF: 終わると同時に閉じる。\n\nコンソールを使わないツールには効かない。", ExternalToolHelp.KeepWindowOpen.ToolTip);
        Assert.Equal("ON: 実際に渡すコマンドラインを見せ、OK を押すまで起動しない。\nOFF: そのまま起動する。", ExternalToolHelp.ConfirmBeforeRun.ToolTip);
    }

    // 実機確認（2026-10-07）: 別の AI が「後ろに付ける \」で次の項目とつながると読み、引数を分けた設定を返した
    [Fact]
    public void PromptSaysRowsAreSeparateArguments()
    {
        var text = Prompt();
        var dialog = text[text.IndexOf("## 5.", StringComparison.Ordinal)..text.IndexOf("## 6.", StringComparison.Ordinal)];
        Assert.Contains("1 つの引数につなげることはできない", dialog);
        Assert.Contains("次の行（ほかの項目など）とはつながらない", dialog);
        Assert.Contains("### 入力ダイアログで保存先と名前を決めて ZIP に圧縮する\n", text);
    }

    /// <summary>プロンプトの説明の裏付け: フォルダの項目の「後ろに付ける \」は、次の項目とつながらない。</summary>
    [Fact]
    public void ItemRowsDoNotJoin()
    {
        var prompt = new PromptDefinition
        {
            Items =
            [
                new PromptItem { Id = 1, Kind = PromptItemKind.Folder, Label = "保存先フォルダ", Suffix = "\\", Required = true },
                new PromptItem { Id = 2, Kind = PromptItemKind.Text, Label = "ZIPファイル名", Required = true },
            ],
            Arguments = [PromptArgument.Fixed("-cf"), PromptArgument.Item(1), PromptArgument.Item(2)],
        };
        var values = new Dictionary<int, PromptValue> { [1] = new(@"D:\backup"), [2] = new("test.zip") };
        Assert.Equal(new[] { "-cf", @"D:\backup\", "test.zip" },
            PromptExpander.Expand(new PromptInput(prompt, values), new MacroContext([], null, @"C:\work")));
    }

    [Fact]
    public void ExampleWithoutTargetsSaysSo()
    {
        Assert.Contains("例の対象: なし（この例では対象の項目を使いません）", Prompt());
    }

    [Fact]
    public void EndsWithSingleNewline()
    {
        var text = Prompt();
        Assert.EndsWith("\n", text);
        Assert.False(text.EndsWith("\n\n", StringComparison.Ordinal));
    }

    // R-145-2: 欄や値を足したのに説明を書かなければ、ここで止まる
    public static TheoryData<Type, string[]> SettingTypes => new()
    {
        { typeof(ExternalTool), ["Id"] },
        { typeof(PromptDefinition), [] },
        { typeof(PromptItem), ["Id", "LastText", "LastChecked", "LastChoiceId"] },
        { typeof(PromptChoice), ["Id"] },
        { typeof(PromptArgument), ["ItemId"] },
    };

    [Theory]
    [MemberData(nameof(SettingTypes))]
    public void EverySettingFieldIsExplainedInPrompt(Type type, string[] excluded)
    {
        var text = Prompt();
        var entries = ExternalToolHelp.ToolFields.Concat(ExternalToolHelp.PromptFields)
            .Select(e => (e.Key, e.Description))
            .Concat(ExternalToolHelp.Options.Select(o => (o.Key, o.On)))
            .ToDictionary(e => e.Key, e => e.Item2);
        var fields = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is not null && !excluded.Contains(p.Name));
        foreach (var field in fields)
        {
            var key = $"{type.Name}.{field.Name}";
            Assert.True(entries.TryGetValue(key, out var description), $"説明が無い欄: {key}");
            Assert.Contains(AsCell(description), text);
        }
    }

    [Theory]
    [InlineData(typeof(PromptItemKind))]
    [InlineData(typeof(PromptArgumentKind))]
    [InlineData(typeof(PromptFolderEmptyBehavior))]
    public void EveryEnumValueIsExplainedInPrompt(Type type)
    {
        var text = Prompt();
        foreach (var name in Enum.GetNames(type))
        {
            var key = $"{type.Name}.{name}";
            var entry = ExternalToolHelp.PromptValues.SingleOrDefault(v => v.Key == key);
            Assert.True(entry is not null, $"説明が無い値: {key}");
            Assert.Contains(AsCell(entry!.Description), text);
        }
    }

    [Fact]
    public void HelpKeysAreUnique()
    {
        var keys = ExternalToolHelp.ToolFields.Concat(ExternalToolHelp.PromptFields).Concat(ExternalToolHelp.PromptValues)
            .Concat(ExternalToolHelp.Terms).Select(e => e.Key).Concat(ExternalToolHelp.Options.Select(o => o.Key)).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    // R-145-2: 例は解析できるだけでなく、入力ダイアログの検査を通り、例の入力から期待どおりの引数になる
    public static TheoryData<int> ExampleIndexes => new(Enumerable.Range(0, ExternalToolExamples.All.Count));

    [Theory]
    [MemberData(nameof(ExampleIndexes))]
    public void ExampleIsValidAndExpandsAsDocumented(int index)
    {
        var example = ExternalToolExamples.All[index];
        var template = ArgumentTemplate.Parse(example.Tool.Arguments);
        Assert.True(template.IsValid, example.Title);
        Assert.Equal(example.Tool.Prompt is not null, template.HasPrompt);

        PromptInput? input = null;
        if (example.Tool.Prompt is { } prompt)
        {
            Assert.Empty(PromptDefinitionRules.Validate(prompt));
            input = new PromptInput(prompt, example.Input);
        }

        var request = Assert.Single(LaunchPlanner.Plan(example.Tool, template, example.Targets, example.Cursor,
            ExternalToolExamples.CurrentFolder, input));
        Assert.Equal(example.Expected, request.Arguments);
        Assert.Equal(ExternalToolExamples.CurrentFolder, request.WorkingDirectory);
    }

    [Fact]
    public void ExamplesAppearInAnswerShape()
    {
        var text = Prompt();
        foreach (var example in ExternalToolExamples.All)
        {
            Assert.Contains($"### {example.Title}\n", text);
            Assert.Contains($"```\n{example.Tool.Arguments}\n```", text);
        }
    }

    /// <summary>
    /// レビュー用に、実際のプロンプトをファイルへ書き出す。RETAC_PROMPT_EXPORT にパスを入れたときだけ書く
    /// （manifest.json の RETAC_SCHEMA_UPDATE と同じ扱い。ふだんのテストではファイルを作らない）。
    /// </summary>
    [Fact]
    public void ExportForReview()
    {
        var path = Environment.GetEnvironmentVariable("RETAC_PROMPT_EXPORT");
        if (string.IsNullOrEmpty(path)) return;
        File.WriteAllText(path, Prompt(), new UTF8Encoding(false));
    }
}
