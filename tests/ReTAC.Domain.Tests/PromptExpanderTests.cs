using ReTAC.Domain.Entries;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

/// <summary>R-130: ${prompt} の位置に、定義の引数の並びを送る順に展開する</summary>
public class PromptExpanderTests
{
    private const string Cwd = @"C:\work";
    private static readonly Entry A = TestEntries.File("a.txt");
    private static readonly Entry B = TestEntries.File("b.txt");

    private static string[] Args(string template, PromptDefinition definition, Dictionary<int, PromptValue> values, Entry[]? targets = null,
                                 Func<string, string>? form = null)
    {
        targets ??= [A];
        var result = ArgumentExpander.Expand(ArgumentTemplate.Parse(template),
            new MacroContext(targets, targets.FirstOrDefault(), Cwd, new PromptInput(definition, values), form));
        Assert.NotNull(result);
        return [.. result];
    }

    // WinRAR の展開: x -o+ -ibck ${file} 展開先\（項目の間に ${file} を挟む形）
    private static readonly PromptDefinition WinRar = new()
    {
        Items =
        [
            new PromptItem { Id = 1, Kind = PromptItemKind.Folder, Label = "展開先", Suffix = @"\" },
            new PromptItem { Id = 2, Kind = PromptItemKind.CheckBox, Label = "上書きする", Value = "-o+" },
            new PromptItem { Id = 3, Kind = PromptItemKind.CheckBox, Label = "バックグラウンドで実行", Value = "-ibck", InitialChecked = true },
        ],
        Arguments = [PromptArgument.Fixed("x"), PromptArgument.Item(2), PromptArgument.Item(3), PromptArgument.Template("${file}"), PromptArgument.Item(1)],
    };

    [Fact]
    public void 項目の間にマクロを挟めて表示順と送る順は別()
    {
        var values = new Dictionary<int, PromptValue> { [1] = new(@"E:\展開先"), [2] = new(Checked: true), [3] = new(Checked: true) };
        Assert.Equal(["x", "-o+", "-ibck", @"C:\work\a.txt", @"C:\work\b.txt", @"E:\展開先\"],
            Args("${prompt}", WinRar, values, [A, B]));
    }

    [Fact]
    public void オフのチェックボックスは何も送らない()
    {
        var values = new Dictionary<int, PromptValue> { [1] = new(@"E:\展開先"), [2] = new(Checked: false), [3] = new(Checked: false) };
        Assert.Equal(["x", @"C:\work\a.txt", @"E:\展開先\"], Args("${prompt}", WinRar, values));
    }

    [Fact]
    public void プロンプトの外の引数はそのまま残る()
    {
        var values = new Dictionary<int, PromptValue> { [1] = new(@"E:\a"), [2] = new(Checked: false), [3] = new(Checked: false) };
        Assert.Equal(["-before", "x", @"C:\work\a.txt", @"E:\a\", "-after"], Args("-before ${prompt} -after", WinRar, values));
    }

    [Fact]
    public void 固定の引数は書いたとおり送る()
    {
        var definition = new PromptDefinition
        {
            Arguments = [.. new[] { "a b", "$$", "${file}", "$${file}", "\"q\"", "" }.Select(PromptArgument.Fixed)],
        };
        Assert.Equal(["a b", "$$", "${file}", "$${file}", "\"q\"", ""], Args("${prompt}", definition, []));
    }

    [Fact]
    public void マクロを含む引数の行は引数欄と同じ意味()
    {
        var definition = new PromptDefinition { Arguments = [PromptArgument.Template("--input=${file} $$")] };
        Assert.Equal([@"--input=C:\work\a.txt", "$"], Args("${prompt}", definition, []));
    }

    [Fact]
    public void マクロを含む引数の行のびっくりで空なら起動しない()
    {
        var definition = new PromptDefinition { Arguments = [PromptArgument.Template("${cursorFile}!")] };
        var folder = TestEntries.Folder("sub");
        var result = ArgumentExpander.Expand(ArgumentTemplate.Parse("${prompt}"),
            new MacroContext([folder], folder, Cwd, new PromptInput(definition, new Dictionary<int, PromptValue>())));
        Assert.Null(result);
    }

    private static PromptDefinition One(PromptItem item) => new() { Items = [item with { Id = 1 }], Arguments = [PromptArgument.Item(1)] };

    [Theory]
    [InlineData("-ss ", "00:01:00", "", new[] { "-ss", "00:01:00" })]
    [InlineData("-m ", "直した 箇所", "", new[] { "-m", "直した 箇所" })]
    [InlineData("", "  前後の空白  ", "", new[] { "  前後の空白  " })]
    public void テキストは付ける文字とつなぎ値は区切らない(string prefix, string text, string suffix, string[] expected)
    {
        var item = new PromptItem { Kind = PromptItemKind.Text, Label = "t", Prefix = prefix, Suffix = suffix };
        Assert.Equal(expected, Args("${prompt}", One(item), new() { [1] = new(text) }));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t")]
    public void 必須でないテキストの空欄は付ける文字ごと送らない(string text)
    {
        var item = new PromptItem { Kind = PromptItemKind.Text, Label = "t", Prefix = "-ss " };
        Assert.Equal(["before"], Args("before ${prompt}", One(item), new() { [1] = new(text) }));
    }

    [Theory]
    [InlineData(@"C:\", @"\", @"C:\")]
    [InlineData(@"C:\", @"\out.mp4", @"C:\out.mp4")]
    [InlineData(@"E:\展開先\", @"\", @"E:\展開先\")]
    public void フォルダの値と後ろに付ける円記号は重ねない(string folder, string suffix, string expected)
    {
        var item = new PromptItem { Kind = PromptItemKind.Folder, Label = "f", Suffix = suffix };
        Assert.Equal([expected], Args("${prompt}", One(item), new() { [1] = new(folder) }));
    }

    [Fact]
    public void フォルダとファイルの値には短いパスの変換を掛ける()
    {
        var item = new PromptItem { Kind = PromptItemKind.Folder, Label = "f", Prefix = "-o", Suffix = @"\" };
        Assert.Equal([@"-oSHORT\"], Args("${prompt}", One(item), new() { [1] = new(@"E:\長い") }, form: _ => "SHORT"));
        var text = new PromptItem { Kind = PromptItemKind.Text, Label = "t" };
        Assert.Equal([@"E:\長い"], Args("${prompt}", One(text), new() { [1] = new(@"E:\長い") }, form: _ => "SHORT"));
    }

    [Fact]
    public void チェックボックスと選択肢の送る値は区切りの規則で分ける()
    {
        var check = new PromptItem { Kind = PromptItemKind.CheckBox, Label = "c", Value = "-c:v copy \"-vf scale=1280:-1\" $${file}" };
        Assert.Equal(["-c:v", "copy", "-vf scale=1280:-1", "$${file}"], Args("${prompt}", One(check), new() { [1] = new(Checked: true) }));

        var drop = new PromptItem
        {
            Kind = PromptItemKind.DropDown, Label = "d",
            Choices = [new PromptChoice { Id = 1, Label = "指定しない" }, new PromptChoice { Id = 2, Label = "高速", Value = "-preset fast" }],
        };
        Assert.Equal(["-preset", "fast"], Args("${prompt}", One(drop), new() { [1] = new(ChoiceId: 2) }));
        Assert.Equal(["x"], Args("x ${prompt}", One(drop), new() { [1] = new(ChoiceId: 1) }));
        // 存在しない選択肢の番号は何も送らず、例外にもしない
        Assert.Equal(["x"], Args("x ${prompt}", One(drop), new() { [1] = new(ChoiceId: 99) }));
    }

    [Fact]
    public void 同じ項目を二つの行に入れたら両方に送る()
    {
        var definition = new PromptDefinition
        {
            Items = [new PromptItem { Id = 1, Kind = PromptItemKind.Folder, Label = "f" }],
            Arguments = [PromptArgument.Item(1), PromptArgument.Fixed("-and"), PromptArgument.Item(1)],
        };
        Assert.Equal([@"E:\a", "-and", @"E:\a"], Args("${prompt}", definition, new() { [1] = new(@"E:\a") }));
    }

    [Fact]
    public void 定義の無いプロンプトは入力を一つの引数で送り空なら送らない()
    {
        var simple = PromptDefinition.Simple("検索");
        Assert.Equal(["-search", "a b"], Args("-search ${prompt}", simple, new() { [1] = new("a b") }));
        Assert.Equal(["-search"], Args("-search ${prompt}", simple, new() { [1] = new("") }));
    }

    [Fact]
    public void 結果の例はマクロをそのまま出しフォルダの空欄はcwd()
    {
        var values = new Dictionary<int, PromptValue> { [1] = new(""), [2] = new(Checked: true), [3] = new(Checked: false) };
        Assert.Equal(@"x -o+ ${file} ${cwd}\", PromptExample.Render(WinRar, values));

        var spaced = new PromptDefinition { Arguments = [PromptArgument.Fixed("a b"), PromptArgument.Fixed("")] };
        Assert.Equal("\"a b\" \"\"", PromptExample.Render(spaced, new Dictionary<int, PromptValue>()));
    }
}
