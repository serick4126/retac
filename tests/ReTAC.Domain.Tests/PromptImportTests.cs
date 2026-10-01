using ReTAC.Domain.Entries;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

/// <summary>R-134: インポート・印・変換・まとめる</summary>
public class PromptImportTests
{
    private static readonly Entry A = TestEntries.File("a.txt");

    /// <summary>定義を展開した引数（インポートした直後・変換の前後で、送るものが変わらないことを確かめる）</summary>
    private static string[] Sent(PromptDefinition definition, Dictionary<int, PromptValue>? values = null)
    {
        values ??= new Dictionary<int, PromptValue>(PromptAnswerRules.Defaults(definition));
        var check = PromptAnswerRules.Check(definition, values, @"C:\work");
        Assert.Null(check.Error);
        var result = ArgumentExpander.Expand(ArgumentTemplate.Parse("${prompt}"),
            new MacroContext([A], A, @"C:\work", new PromptInput(definition, check.Values!)));
        return [.. result!];
    }

    private static PromptDefinition Imported(string commandLine) =>
        new() { Arguments = [.. PromptImport.Parse(commandLine)!.Arguments] };

    [Fact]
    public void 先頭がパスで残りは固定の引数()
    {
        var command = PromptImport.Parse("\"C:\\Program Files\\WinRAR\\WinRAR.exe\" x -o+ -ibck D:\\書庫.rar \"E:\\展開先\\\"")!;
        Assert.Equal(@"C:\Program Files\WinRAR\WinRAR.exe", command.Path);
        Assert.All(command.Arguments, a => Assert.Equal(PromptArgumentKind.Fixed, a.Kind));
        Assert.Equal(["x", "-o+", "-ibck", @"D:\書庫.rar", @"E:\展開先\"], command.Arguments.Select(a => a.Text));
    }

    [Theory]
    [InlineData("tool.exe a　b \"c d\" \"\"", new[] { "a　b", "c d", "" })]
    [InlineData("tool.exe $$ ${file} $${file} ${nope}", new[] { "$$", "${file}", "$${file}", "${nope}" })]
    public void 読み込んだ直後は貼り付けたものと同じ引数の並びが送られる(string commandLine, string[] expected)
    {
        Assert.Equal(expected, Sent(Imported(commandLine)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \t ")]
    [InlineData("\"tool.exe a")]
    [InlineData("\"\" a")]
    public void 読み取れないコマンドラインはnull(string commandLine)
    {
        Assert.Null(PromptImport.Parse(commandLine));
    }

    [Theory]
    [InlineData("-o+", ArgumentMark.Switch)]
    [InlineData("/ibck", ArgumentMark.Switch)]
    [InlineData("-", ArgumentMark.None)]
    [InlineData("x", ArgumentMark.None)]
    [InlineData(@"D:\書庫.rar", ArgumentMark.Path)]
    [InlineData(@"\\server\share", ArgumentMark.Path)]
    [InlineData(@"sub\file.txt", ArgumentMark.Path)]
    [InlineData("C:", ArgumentMark.Path)]
    [InlineData(@"/x\y", ArgumentMark.Path)]                // / で始まっても \ を含めばパス
    [InlineData(@"-oE:\展開先", ArgumentMark.Path)]
    public void 印の判定(string value, ArgumentMark expected)
    {
        Assert.Equal(expected, PromptImport.Mark(value));
    }

    private static ConversionTarget[] Targets(string value, PathKind? kind, int itemCount = 0) =>
        [.. PromptImport.Candidates(value, kind, itemCount).Select(c => c.Target)];

    [Fact]
    public void 候補の並び()
    {
        Assert.Equal([ConversionTarget.CheckBox, ConversionTarget.Text], Targets("-o+", null));
        Assert.Equal([ConversionTarget.MacroFile, ConversionTarget.MacroCursorFile, ConversionTarget.MacroPath, ConversionTarget.FileItem, ConversionTarget.Text],
            Targets(@"D:\書庫.rar", PathKind.File));
        Assert.Equal([ConversionTarget.FolderItem, ConversionTarget.MacroPath, ConversionTarget.MacroCwd, ConversionTarget.Text],
            Targets(@"E:\展開先", PathKind.Folder));
        Assert.Equal([ConversionTarget.FolderItem, ConversionTarget.MacroPath, ConversionTarget.MacroCwd, ConversionTarget.Text],
            Targets(@"E:\無い\", PathKind.Missing));               // \ で終わればフォルダ
        Assert.Equal([ConversionTarget.FileItem, ConversionTarget.FolderItem, ConversionTarget.MacroFile, ConversionTarget.Text],
            Targets(@"E:\無い.txt", PathKind.Missing));
        Assert.Equal([ConversionTarget.Text, ConversionTarget.CheckBox], Targets("x", null));
    }

    [Fact]
    public void 変換できない値と項目が十個のとき()
    {
        Assert.Empty(Targets("", null));                                               // 空の引数は項目にすると消える
        Assert.Equal([ConversionTarget.Text], Targets("a\"b", null));                  // " はチェックボックスで表せない
        Assert.Equal([ConversionTarget.CheckBox], Targets("  ", null));                // 空白だけはテキストにすると空とみなされる
        Assert.Equal([ConversionTarget.MacroFile, ConversionTarget.MacroCursorFile, ConversionTarget.MacroPath],
            Targets(@"D:\書庫.rar", PathKind.File, itemCount: 10));
    }

    [Fact]
    public void 項目に変えても送るものは変わらない()
    {
        foreach (var (value, target) in new[]
        {
            ("-o+", ConversionTarget.CheckBox),
            ("a b", ConversionTarget.CheckBox),
            ("$${file}", ConversionTarget.Text),
            ("  前後  ", ConversionTarget.Text),
            (@"E:\展開先\", ConversionTarget.FolderItem),
            (@"C:\", ConversionTarget.FolderItem),
            (@"D:\書庫.rar", ConversionTarget.FileItem),
        })
        {
            var before = new PromptDefinition { Arguments = [PromptArgument.Fixed("x"), PromptArgument.Fixed(value)] };
            var after = PromptImport.Convert(before, 1, target);
            Assert.Equal(Sent(before), Sent(after));
            Assert.Equal(PromptArgumentKind.Item, after.Arguments[1].Kind);
            Assert.Equal(value, Assert.Single(after.Items).Label);
        }
    }

    [Fact]
    public void 円記号で終わるフォルダは初期値をそのままにして後ろに付けるを円記号にする()
    {
        var after = PromptImport.Convert(new PromptDefinition { Arguments = [PromptArgument.Fixed(@"E:\展開先\")] }, 0, ConversionTarget.FolderItem);
        var item = Assert.Single(after.Items);
        Assert.Equal((@"E:\展開先\", @"\"), (item.Initial, item.Suffix));
        // 利用者が \ を付けずに別のフォルダを入れても \ で終わる
        Assert.Equal([@"F:\別\"], Sent(after, new() { [item.Id] = new(@"F:\別") }));
    }

    [Fact]
    public void マクロに変えると送るものは変わる()
    {
        var after = PromptImport.Convert(new PromptDefinition { Arguments = [PromptArgument.Fixed(@"D:\書庫.rar")] }, 0, ConversionTarget.MacroFile);
        Assert.Equal(PromptArgument.Template("${file}"), Assert.Single(after.Arguments));
        Assert.Empty(after.Items);
        Assert.Equal([@"C:\work\a.txt"], Sent(after));
    }

    private static PromptDefinition Rows(params string[] values) => new() { Arguments = [.. values.Select(PromptArgument.Fixed)] };

    [Fact]
    public void まとめる前とまとめたチェックボックスがオンのときで送るものが同じ()
    {
        var before = Rows("x", "-c:v", "copy", "a b", "$${file}", "y");
        int[] picked = [1, 2, 3, 4];
        Assert.True(PromptImport.CanMerge(before, picked));
        var draft = PromptImport.MergeDraft(before, picked);
        Assert.Equal((PromptItemKind.CheckBox, "-c:v", true), (draft.Kind, draft.Label, draft.InitialChecked));
        Assert.Equal("-c:v copy \"a b\" $${file}", draft.Value);

        var after = PromptImport.ApplyMerge(before, picked, draft);
        Assert.Equal(Sent(before), Sent(after));
        Assert.Equal(["x", "y"], Sent(after, new() { [draft.Id] = new(Checked: false) }));
        Assert.Equal([PromptArgumentKind.Fixed, PromptArgumentKind.Item, PromptArgumentKind.Fixed], after.Arguments.Select(a => a.Kind));
    }

    [Fact]
    public void 選んだ順に関係なく送る順でまとめる()
    {
        var before = Rows("x", "-c:v", "copy");
        var draft = PromptImport.MergeDraft(before, [2, 1]);
        Assert.Equal("-c:v copy", draft.Value);
        Assert.Equal(Sent(before), Sent(PromptImport.ApplyMerge(before, [2, 1], draft)));
    }

    [Fact]
    public void まとめられない選び方()
    {
        Assert.False(PromptImport.CanMerge(Rows("a", "b", "c"), [0, 2]));          // 連続していない
        Assert.False(PromptImport.CanMerge(Rows("a"), [0]));                       // 1 行だけ
        Assert.False(PromptImport.CanMerge(Rows("a", ""), [0, 1]));                // 空の値
        Assert.False(PromptImport.CanMerge(Rows("a", "b\"c"), [0, 1]));            // " を含む
        var mixed = new PromptDefinition { Arguments = [PromptArgument.Fixed("a"), PromptArgument.Template("${file}")] };
        Assert.False(PromptImport.CanMerge(mixed, [0, 1]));                        // マクロを含む引数の行
        var full = Rows("a", "b") with
        {
            Items = [.. Enumerable.Range(1, 10).Select(i => new PromptItem { Id = i, Kind = PromptItemKind.Text, Label = $"t{i}" })],
        };
        Assert.False(PromptImport.CanMerge(full, [0, 1]));                         // 項目が 10 個
    }
}
