using ReTAC.Domain.Navigation;
using ReTAC.Domain.Entries;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

/// <summary>R-131 / R-132: 入力の検査・初期値・前回の値</summary>
public class PromptAnswerRulesTests
{
    private const string Cwd = @"C:\work";

    private static readonly PromptItem TextItem = new() { Id = 1, Kind = PromptItemKind.Text, Label = "メッセージ", Initial = "既定" };
    private static readonly PromptItem FolderItem = new() { Id = 2, Kind = PromptItemKind.Folder, Label = "出力先" };
    private static readonly PromptItem FileItem = new() { Id = 3, Kind = PromptItemKind.File, Label = "比較先", Initial = "b.txt" };
    private static readonly PromptItem CheckItem = new() { Id = 4, Kind = PromptItemKind.CheckBox, Label = "上書き", Value = "-y", InitialChecked = true };
    private static readonly PromptItem DropItem = new()
    {
        Id = 5, Kind = PromptItemKind.DropDown, Label = "画質", InitialChoiceId = 2,
        Choices = [new PromptChoice { Id = 1, Label = "高速", Value = "fast" }, new PromptChoice { Id = 2, Label = "標準", Value = "medium" }],
    };

    private static PromptDefinition All(bool remember = false) => new()
    {
        RememberLast = remember,
        Items = [TextItem, FolderItem, FileItem, CheckItem, DropItem],
        Arguments = [.. new[] { 1, 2, 3, 4, 5 }.Select(PromptArgument.Item)],
    };

    private static Dictionary<int, PromptValue> Input(string text = "x", string folder = "", string file = "f.txt", bool check = false, int choice = 1) => new()
    {
        [1] = new PromptValue(text),
        [2] = new PromptValue(folder),
        [3] = new PromptValue(file),
        [4] = new PromptValue(Checked: check),
        [5] = new PromptValue(ChoiceId: choice),
    };

    [Fact]
    public void 初期値は定義の初期値()
    {
        var values = PromptAnswerRules.Defaults(All(), new MacroContext([], null, @"C:\work")).Values!;
        Assert.Equal(new PromptValue("既定"), values[1]);
        Assert.Equal(new PromptValue(""), values[2]);
        Assert.Equal(new PromptValue("b.txt"), values[3]);
        Assert.Equal(new PromptValue(Checked: true), values[4]);
        Assert.Equal(new PromptValue(ChoiceId: 2), values[5]);
    }

    [Fact]
    public void 前回の値を覚えるなら前回の値で開き覚えないなら初期値()
    {
        var remembered = PromptAnswerRules.Remember(All(remember: true), Input(text: "前回", folder: "out", check: false, choice: 1));
        var initial = PromptAnswerRules.Initial(remembered, new MacroContext([], null, @"C:\work")).Values!;
        Assert.Equal(new PromptValue("前回"), initial[1]);
        Assert.Equal(new PromptValue("out"), initial[2]);
        Assert.Equal(new PromptValue(Checked: false), initial[4]);
        Assert.Equal(new PromptValue(ChoiceId: 1), initial[5]);

        Assert.Equal(PromptAnswerRules.Defaults(remembered, new MacroContext([], null, @"C:\work")).Values!, PromptAnswerRules.Initial(remembered with { RememberLast = false }, new MacroContext([], null, @"C:\work")).Values!);
    }

    [Fact]
    public void 覚えない定義には前回の値を書かない()
    {
        var definition = All();
        Assert.Same(definition, PromptAnswerRules.Remember(definition, Input()));
    }

    [Fact]
    public void 前回の選択肢が無ければ初期の選択()
    {
        var definition = All(remember: true) with { Items = [.. All().Items.Select(i => i.Id == 5 ? i with { LastChoiceId = 99 } : i)] };
        Assert.Equal(2, PromptAnswerRules.Initial(definition, new MacroContext([], null, @"C:\work")).Values![5].ChoiceId);
    }

    [Fact]
    public void 空欄だったことも前回の値として残す()
    {
        var remembered = PromptAnswerRules.Remember(All(remember: true), Input(text: ""));
        Assert.Equal("", PromptAnswerRules.Initial(remembered, new MacroContext([], null, @"C:\work")).Values![1].Text);   // 初期値「既定」には戻さない
    }

    [Fact]
    public void 前回の値を消す()
    {
        var remembered = PromptAnswerRules.Remember(All(remember: true), Input());
        var forgotten = PromptAnswerRules.ForgetLast(remembered);
        Assert.All(forgotten.Items, i =>
        {
            Assert.Null(i.LastText);
            Assert.Null(i.LastChecked);
            Assert.Null(i.LastChoiceId);
        });
    }

    [Fact]
    public void フォルダの空欄はカレントフォルダで相対パスは絶対パスになり引用符は外れる()
    {
        Assert.Equal(Cwd, Values(Input(folder: ""))[2].Text);
        Assert.Equal(Cwd, Values(Input(folder: " \t"))[2].Text);
        Assert.Equal(@"C:\work\out", Values(Input(folder: "out"))[2].Text);
        Assert.Equal(@"D:\a b", Values(Input(folder: "\"D:\\a b\""))[2].Text);
        Assert.Equal(@"C:\work\f.txt", Values(Input())[3].Text);
    }

    [Fact]
    public void 必須でないファイルの空欄は空()
    {
        Assert.Equal("", Values(Input(file: "\"\""))[3].Text);
    }

    private static IReadOnlyDictionary<int, PromptValue> Values(Dictionary<int, PromptValue> input)
    {
        var check = PromptAnswerRules.Check(All(), input, Cwd, _ => null);
        Assert.Null(check.Error);
        return check.Values!;
    }

    [Theory]
    [InlineData("", "メッセージを入力してください。")]
    [InlineData(" \t", "メッセージを入力してください。")]
    public void 必須のテキストは空白だけでも空(string text, string message)
    {
        var definition = All() with { Items = [.. All().Items.Select(i => i.Id == 1 ? i with { Required = true } : i)] };
        var check = PromptAnswerRules.Check(definition, Input(text: text), Cwd, _ => null);
        Assert.Null(check.Values);
        Assert.Equal((1, message), (check.ItemId!.Value, check.Error));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"\"")]
    [InlineData("  \"  \"  ")]
    public void 必須のファイルは空白と引用符を外してから判断する(string file)
    {
        var definition = All() with { Items = [.. All().Items.Select(i => i.Id == 3 ? i with { Required = true } : i)] };
        var check = PromptAnswerRules.Check(definition, Input(file: file), Cwd, _ => null);
        Assert.Equal("比較先を入力してください。", check.Error);
    }

    [Theory]
    [InlineData("bad|name")]
    [InlineData(@"..\..")]
    public void 解決できないパスは誤り(string folder)
    {
        var check = PromptAnswerRules.Check(All(), Input(folder: folder), Cwd, _ => null);
        Assert.Equal((2, "出力先のパスが正しくありません。"), (check.ItemId!.Value, check.Error));
    }

    // R-147: Check が置き換えた実パスは PromptExpander がそのまま使う（2 回置き換えない）
    private static readonly Func<string, string?> P32Env = name => name switch
    {
        "P32_OUTER" => @"C:\x\%P32_INNER%", "P32_INNER" => "inner", "P32_SUB" => "sub", "P32_UP" => @"..\sub", _ => null,
    };

    private static string? FinalArgument(PromptItemKind kind, string text, string prefix = "", string suffix = "")
    {
        var item = new PromptItem { Id = 1, Kind = kind, Label = "x", Prefix = prefix, Suffix = suffix, Required = true };
        var definition = new PromptDefinition { Items = [item], Arguments = [PromptArgument.Item(1)] };
        var check = PromptAnswerRules.Check(definition, new Dictionary<int, PromptValue> { [1] = new(text) }, @"C:\work\cur", P32Env);
        Assert.Null(check.Error);
        return Assert.Single(PromptExpander.Expand(new(definition, check.Values!), new([], null, @"C:\work\cur"))!);
    }

    [Theory]
    [InlineData(PromptItemKind.Folder)]
    [InlineData(PromptItemKind.File)]
    public void 入力の変数は一度だけ置き換わり_値の中の変数は残る(PromptItemKind kind) =>
        Assert.Equal(@"C:\x\%P32_INNER%", FinalArgument(kind, "%P32_OUTER%"));

    [Theory]
    [InlineData(PromptItemKind.Folder, "sub", @"C:\work\cur\sub")]
    [InlineData(PromptItemKind.Folder, "%P32_SUB%", @"C:\work\cur\sub")]
    [InlineData(PromptItemKind.Folder, @"%P32_UP%", @"C:\work\sub")]
    [InlineData(PromptItemKind.File, "sub", @"C:\work\cur\sub")]
    [InlineData(PromptItemKind.File, "%P32_SUB%", @"C:\work\cur\sub")]
    [InlineData(PromptItemKind.File, @"%P32_UP%", @"C:\work\sub")]
    [InlineData(PromptItemKind.File, "a b %P32_SUB%", @"C:\work\cur\a b sub")]   // 空白を含む
    public void 相対や相対に置き換わる入力はカレントフォルダ基準の絶対パス(PromptItemKind kind, string text, string expected) =>
        Assert.Equal(expected, FinalArgument(kind, text));

    [Fact]
    public void 前後に付ける文字があっても置き換えは一度だけ()
    {
        Assert.Equal(@"-o=C:\x\%P32_INNER%;", FinalArgument(PromptItemKind.Folder, "%P32_OUTER%", "-o=", ";"));
        Assert.Equal(@"/f:C:\work\cur\sub", FinalArgument(PromptItemKind.File, "%P32_SUB%", "/f:"));
    }

    [Fact]
    public void 存在しないパスは誤りにしない()
    {
        Assert.Equal(@"Z:\無い\フォルダ", Values(Input(folder: @"Z:\無い\フォルダ"))[2].Text);
    }

    [Fact]
    public void 誤りが複数あれば表示順で最初の一つ()
    {
        var check = PromptAnswerRules.Check(All(), Input(folder: "bad|name", file: "bad|name"), Cwd, _ => null);
        Assert.Equal(2, check.ItemId);
    }

    [Fact]
    public void 履歴に入れるのは空欄でないフォルダの絶対パスだけ()
    {
        var input = Input(folder: "out");
        Assert.Equal([@"C:\work\out"], PromptAnswerRules.HistoryFolders(All(), input, Values(input)));
        var blank = Input(folder: "");
        Assert.Empty(PromptAnswerRules.HistoryFolders(All(), blank, Values(blank)));
    }

    [Fact]
    public void 開いた定義のままなら前回の値を書き込んだ一覧を返す()
    {
        var shown = All(remember: true);
        var tools = new List<ExternalTool> { new() { Id = 1 }, new() { Id = 7, Prompt = shown } };
        var updated = PromptAnswerRules.RememberIn(tools, 7, shown, Input(text: "新しい"))!;
        Assert.Equal("新しい", updated[1].Prompt!.Items[0].LastText);
        Assert.Same(tools[0], updated[0]);
    }

    [Fact]
    public void 同じ定義で開いた二つを順にOKしたら後の値が残る()
    {
        // 2 つのウィンドウから同じツールを起動した。最初の OK で定義のインスタンスは新しくなるが、中身（前回の値を除く）は同じ
        var shown = All(remember: true);
        var tools = new List<ExternalTool> { new() { Id = 7, Prompt = shown } };
        var first = PromptAnswerRules.RememberIn(tools, 7, shown, Input(text: "先"))!;
        var second = PromptAnswerRules.RememberIn(first, 7, shown, Input(text: "後"))!;
        Assert.Equal("後", second[0].Prompt!.Items[0].LastText);
    }

    [Fact]
    public void 開いた後に定義が変わったら残さない()
    {
        var shown = All(remember: true);
        var edited = new List<ExternalTool> { new() { Id = 7, Prompt = shown with { Title = "直した" } } };
        Assert.Null(PromptAnswerRules.RememberIn(edited, 7, shown, Input()));
        var itemEdited = new List<ExternalTool> { new() { Id = 7, Prompt = shown with { Items = [.. shown.Items.Select(i => i with { Label = i.Label + "!" })] } } };
        Assert.Null(PromptAnswerRules.RememberIn(itemEdited, 7, shown, Input()));
        Assert.Null(PromptAnswerRules.RememberIn([], 7, shown, Input()));                                       // ツールが消えた
        Assert.Null(PromptAnswerRules.RememberIn([new ExternalTool { Id = 7 }], 7, shown, Input()));             // 定義が消えた
        Assert.Null(PromptAnswerRules.RememberIn([new ExternalTool { Id = 7, Prompt = All() }], 7, All(), Input()));   // 覚えない定義
    }

    [Fact]
    public void 前回の値だけが違う定義は同じ中身()
    {
        var shown = All(remember: true);
        Assert.True(PromptAnswerRules.SameShape(shown, PromptAnswerRules.Remember(shown, Input(text: "別"))));
        Assert.False(PromptAnswerRules.SameShape(shown, shown with { RememberLast = false }));
    }

    [Fact]
    public void 囲みの引用符は一つずつしか外さない()
    {
        // R-127: 先頭と末尾の " をそれぞれ 1 つだけ外す。""C:\work\x"" は名前に " を含むパスとして誤り
        Assert.Equal(@"C:\work\x", Values(Input(folder: "\"C:\\work\\x\""))[2].Text);
        var check = PromptAnswerRules.Check(All(), Input(folder: "\"\"C:\\work\\x\"\""), Cwd, _ => null);
        Assert.Equal("出力先のパスが正しくありません。", check.Error);
    }

    [Fact]
    public void 項目の番号が重なっていても初期値は例外にしない()
    {
        // 手で直した設定ファイル。ヘルパーを開いて直せるように、ここでは止めない（重なりは定義の検査が知らせる）
        var broken = new PromptDefinition { Items = [TextItem, TextItem with { Initial = "後" }] };
        Assert.Equal("後", PromptAnswerRules.Defaults(broken, new MacroContext([], null, @"C:\work")).Values![1].Text);
        Assert.Equal("後", PromptAnswerRules.Initial(broken with { RememberLast = true }, new MacroContext([], null, @"C:\work")).Values![1].Text);
    }

    [Fact]
    public void Folder_EmptyMatrix()
    {
        foreach (var required in new[] { false, true })
        foreach (var behavior in Enum.GetValues<PromptFolderEmptyBehavior>())
        foreach (var text in new[] { "", " ", "\t", "\"\"" })
        {
            var item = FolderItem with { Required = required, FolderEmptyBehavior = behavior };
            var definition = new PromptDefinition { Items = [item], Arguments = [PromptArgument.Item(item.Id)] };
            var input = new Dictionary<int, PromptValue> { [item.Id] = new(text) };
            var check = PromptAnswerRules.Check(definition, input, Cwd, _ => null);
            if (required)
            {
                Assert.Null(check.Values);
                Assert.Equal(item.Id, check.ItemId);
                Assert.Equal("出力先を入力してください。", check.Error);
            }
            else
            {
                Assert.Null(check.Error);
                Assert.Equal(behavior == PromptFolderEmptyBehavior.CurrentFolder ? Cwd : "", check.Values![item.Id].Text);
                Assert.Empty(PromptAnswerRules.HistoryFolders(definition, input, check.Values));
            }
        }
        var nonEmpty = PromptAnswerRules.Check(new() { Items = [FolderItem] }, new Dictionary<int, PromptValue> { [2] = new("　") }, Cwd, _ => null);
        Assert.Equal(@"C:\work\　", nonEmpty.Values![2].Text);
    }

    [Fact]
    public void Folder_RememberRawEmpty()
    {
        foreach (var behavior in Enum.GetValues<PromptFolderEmptyBehavior>())
        {
            var definition = new PromptDefinition { RememberLast = true, Items = [FolderItem with { FolderEmptyBehavior = behavior }] };
            var remembered = PromptAnswerRules.Remember(definition, new Dictionary<int, PromptValue> { [2] = new("") });
            Assert.Equal("", remembered.Items[0].LastText);
        }
    }

    // ---- R-147 / R-136 / R-132: フォルダ・ファイルの初期値と前回の入力（式と設定ファイルの形） ----

    private static readonly Func<string, string?> InitEnv = name => name switch
    {
        "P32.a" => "other", "P32_DIR" => @"C:\base", "TEMP" => @"C:\bogus", "P32_DEST" => "other", _ => null,
    };

    private static MacroContext FileContext(string fileName)
    {
        var file = Entry.ForFile(@"C:\work\" + fileName, fileName, 0, 0, default);
        return new([file], file, @"C:\work");
    }

    private static PromptDefinition PathDefinition(PromptItemKind kind, string initial, string? last = null, bool remember = false) => new()
    {
        RememberLast = remember,
        Items = [new PromptItem { Id = 1, Kind = kind, Label = "場所", Initial = initial, LastText = last }],
        Arguments = [PromptArgument.Item(1)],
    };

    private static string? Sent(PromptDefinition definition, string cwd, MacroContext context)
    {
        var initial = PromptAnswerRules.Initial(definition, context, InitEnv);
        Assert.Null(initial.Error);
        var check = PromptAnswerRules.Check(definition, initial.Values!, cwd, InitEnv);
        Assert.Null(check.Error);
        return Assert.Single(PromptExpander.Expand(new(definition, check.Values!), context)!);
    }

    [Theory]
    [InlineData(PromptItemKind.Folder)]
    [InlineData(PromptItemKind.File)]
    public void 初期値のマクロの値の変数の形は_連結しても変数にならない(PromptItemKind kind)
    {
        // ファイル名 %P32.a% は ${fileBasenameNoExtension} = %P32 と ${fileExtname} = .a% に分かれる。部品ごとに書いて連結すると %P32.a% が変数になる
        var definition = PathDefinition(kind, @"${cwd}\${fileBasenameNoExtension}${fileExtname}");
        var context = FileContext("%P32.a%");
        var initial = PromptAnswerRules.Initial(definition, context, InitEnv).Values![1].Text;
        Assert.Equal(@"C:\work\%%P32.a%%", initial);
        Assert.Equal(@"C:\work\%P32.a%", Sent(definition, @"C:\work", context));
    }

    [Fact]
    public void 初期値のテンプレートの地の文字の変数は働く()
    {
        var definition = PathDefinition(PromptItemKind.Folder, PathExpression.Store(@"%P32_DIR%\${fileBasenameNoExtension}"));
        var context = FileContext("a.txt");
        Assert.Equal(@"%P32_DIR%\a", PromptAnswerRules.Initial(definition, context, InitEnv).Values![1].Text);
        Assert.Equal(@"C:\base\a", Sent(definition, @"C:\work", context));
    }

    [Fact]
    public void 前回の入力は参照で選んだ文字のまま往復する()
    {
        var definition = PathDefinition(PromptItemKind.Folder, "", remember: true);
        var picked = PathExpression.Literal(@"D:\%TEMP%");   // 参照の結果は式に書いて欄に入る
        var remembered = PromptAnswerRules.Remember(definition, new Dictionary<int, PromptValue> { [1] = new(picked) }, InitEnv);
        Assert.Equal(@"|D:\%%TEMP%%", remembered.Items[0].LastText);
        Assert.Equal(@"D:\%TEMP%", Sent(remembered, @"C:\work", FileContext("a.txt")));
    }

    [Fact]
    public void 前回の入力は正規の形で残り_空欄は空欄のまま()
    {
        var definition = PathDefinition(PromptItemKind.File, "", remember: true);
        string? Stored(string text, Func<string, string?> env) =>
            PromptAnswerRules.Remember(definition, new Dictionary<int, PromptValue> { [1] = new(text) }, env).Items[0].LastText;
        Assert.Equal(@"|%P32_DIR%\x", Stored(@"%P32_DIR%\x", InitEnv));
        Assert.Equal(@"|%%P32_DIR%%\x", Stored(@"%P32_DIR%\x", _ => null));   // 未定義は文字。後で定義されても意味が変わらない
        Assert.Equal("", Stored("", InitEnv));
        // テキストの項目は今までどおり欄の文字のまま
        var text = PromptAnswerRules.Remember(PathDefinition(PromptItemKind.Text, "", remember: true), new Dictionary<int, PromptValue> { [1] = new("%A%B%") });
        Assert.Equal("%A%B%", text.Items[0].LastText);
    }

    [Fact]
    public void 旧設定の初期値は使わず空欄から始まる()
    {
        var context = FileContext("a.txt");
        foreach (var kind in new[] { PromptItemKind.Folder, PromptItemKind.File })
        {
            var legacy = PathDefinition(kind, @"C:\work\%P32_DEST%");
            Assert.Equal("", PromptAnswerRules.Initial(legacy, context, InitEnv).Values![1].Text);
            // 印の付いた同じ文字は式として働く（陽性対照）
            var marked = PathDefinition(kind, @"|C:\work\%P32_DEST%");
            Assert.Equal(@"C:\work\%P32_DEST%", PromptAnswerRules.Initial(marked, context, InitEnv).Values![1].Text);
            Assert.Equal(@"C:\work\other", Sent(marked, @"C:\work", context));
            // % が 1 つ以下の旧設定の値は今までどおり使える
            Assert.Equal(@"C:\work\100%", PromptAnswerRules.Initial(PathDefinition(kind, @"C:\work\100%"), context, InitEnv).Values![1].Text);
        }
        // テキストの項目は印を読まない
        Assert.Equal(@"C:\work\%P32_DEST%", PromptAnswerRules.Initial(PathDefinition(PromptItemKind.Text, @"C:\work\%P32_DEST%"), context, InitEnv).Values![1].Text);
    }

    [Fact]
    public void 旧設定の前回の入力は使わず定義の初期値へ進む()
    {
        var context = FileContext("a.txt");
        var legacy = PathDefinition(PromptItemKind.Folder, @"${cwd}\新", last: @"C:\work\%P32_DEST%", remember: true);
        Assert.Equal(@"C:\work\新", PromptAnswerRules.Initial(legacy, context, InitEnv).Values![1].Text);
        var marked = PathDefinition(PromptItemKind.Folder, @"${cwd}\新", last: @"|C:\work\%P32_DEST%", remember: true);
        Assert.Equal(@"C:\work\%P32_DEST%", PromptAnswerRules.Initial(marked, context, InitEnv).Values![1].Text);   // 前回の入力は展開しない（R-132）
        Assert.Equal(@"C:\work\other", Sent(marked, @"C:\work", context));
    }

    [Fact]
    public void 項目の編集の初期値は地の文字の断片ごとに正規の形にして保存の形で残す()
    {
        var item = new PromptItem { Id = 1, Kind = PromptItemKind.Folder, Label = "x" };
        // マクロ（${…}・$$・!）は壊さない。地の文字の未定義の %名前% は文字に直す
        Assert.Equal(@"|a$$b${file}!%%X%%", PromptAnswerRules.InitialToStore(item, @"a$$b${file}!%X%", _ => null));
        Assert.Equal(@"|%P32_DIR%\${cwd}", PromptAnswerRules.InitialToStore(item, @"%P32_DIR%\${cwd}", InitEnv));
        Assert.Equal(@"${cwd}\新", PromptAnswerRules.InitialToStore(item, @"${cwd}\新", InitEnv));
        // テキストの項目は何もしない
        Assert.Equal("%A%B%", PromptAnswerRules.InitialToStore(item with { Kind = PromptItemKind.Text }, "%A%B%", InitEnv));
    }

    [Fact]
    public void 項目の編集で書き換えていない旧設定の初期値はそのまま残る()
    {
        var legacy = new PromptItem { Id = 1, Kind = PromptItemKind.File, Label = "x", Initial = @"C:\work\%P32_DEST%" };
        Assert.Equal(@"C:\work\%P32_DEST%", PromptAnswerRules.InitialForDisplay(legacy));
        Assert.Equal(legacy.Initial, PromptAnswerRules.InitialToStore(legacy, PromptAnswerRules.InitialForDisplay(legacy), InitEnv));
        // 書き換えたら新しい式として残る
        Assert.Equal(@"|C:\work\%P32_DEST%x", PromptAnswerRules.InitialToStore(legacy, @"C:\work\%P32_DEST%x", InitEnv));
        // 印付きの値は印を外して見せ、書き換えなければそのまま
        var marked = legacy with { Initial = @"|C:\work\%%P32_DEST%%" };
        Assert.Equal(@"C:\work\%%P32_DEST%%", PromptAnswerRules.InitialForDisplay(marked));
        Assert.Equal(marked.Initial, PromptAnswerRules.InitialToStore(marked, PromptAnswerRules.InitialForDisplay(marked), InitEnv));
        // 例の表示（決まった値の一覧）にも印を出さない
        var example = PromptExample.Render(new PromptDefinition { Items = [marked], Arguments = [PromptArgument.Item(1)] });
        Assert.DoesNotContain("|", example);
    }
}
