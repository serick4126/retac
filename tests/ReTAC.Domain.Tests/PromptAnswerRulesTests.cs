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
        var values = PromptAnswerRules.Defaults(All());
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
        var initial = PromptAnswerRules.Initial(remembered);
        Assert.Equal(new PromptValue("前回"), initial[1]);
        Assert.Equal(new PromptValue("out"), initial[2]);
        Assert.Equal(new PromptValue(Checked: false), initial[4]);
        Assert.Equal(new PromptValue(ChoiceId: 1), initial[5]);

        Assert.Equal(PromptAnswerRules.Defaults(remembered), PromptAnswerRules.Initial(remembered with { RememberLast = false }));
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
        Assert.Equal(2, PromptAnswerRules.Initial(definition)[5].ChoiceId);
    }

    [Fact]
    public void 空欄だったことも前回の値として残す()
    {
        var remembered = PromptAnswerRules.Remember(All(remember: true), Input(text: ""));
        Assert.Equal("", PromptAnswerRules.Initial(remembered)[1].Text);   // 初期値「既定」には戻さない
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
        var check = PromptAnswerRules.Check(All(), input, Cwd);
        Assert.Null(check.Error);
        return check.Values!;
    }

    [Theory]
    [InlineData("", "メッセージを入力してください。")]
    [InlineData(" \t", "メッセージを入力してください。")]
    public void 必須のテキストは空白だけでも空(string text, string message)
    {
        var definition = All() with { Items = [.. All().Items.Select(i => i.Id == 1 ? i with { Required = true } : i)] };
        var check = PromptAnswerRules.Check(definition, Input(text: text), Cwd);
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
        var check = PromptAnswerRules.Check(definition, Input(file: file), Cwd);
        Assert.Equal("比較先を入力してください。", check.Error);
    }

    [Theory]
    [InlineData("bad|name")]
    [InlineData(@"..\..")]
    public void 解決できないパスは誤り(string folder)
    {
        var check = PromptAnswerRules.Check(All(), Input(folder: folder), Cwd);
        Assert.Equal((2, "出力先のパスが正しくありません。"), (check.ItemId!.Value, check.Error));
    }

    [Fact]
    public void 存在しないパスは誤りにしない()
    {
        Assert.Equal(@"Z:\無い\フォルダ", Values(Input(folder: @"Z:\無い\フォルダ"))[2].Text);
    }

    [Fact]
    public void 誤りが複数あれば表示順で最初の一つ()
    {
        var check = PromptAnswerRules.Check(All(), Input(folder: "bad|name", file: "bad|name"), Cwd);
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
        var check = PromptAnswerRules.Check(All(), Input(folder: "\"\"C:\\work\\x\"\""), Cwd);
        Assert.Equal("出力先のパスが正しくありません。", check.Error);
    }

    [Fact]
    public void 項目の番号が重なっていても初期値は例外にしない()
    {
        // 手で直した設定ファイル。ヘルパーを開いて直せるように、ここでは止めない（重なりは定義の検査が知らせる）
        var broken = new PromptDefinition { Items = [TextItem, TextItem with { Initial = "後" }] };
        Assert.Equal("後", PromptAnswerRules.Defaults(broken)[1].Text);
        Assert.Equal("後", PromptAnswerRules.Initial(broken with { RememberLast = true })[1].Text);
    }
}
