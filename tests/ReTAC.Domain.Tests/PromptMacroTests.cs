using System.Text.Json;
using System.Windows.Forms;
using ReTAC.App;
using ReTAC.Domain.Navigation;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

public class PromptMacroTests
{
    private static MacroContext Context => ScalarTemplateTests.Context();
    private static PromptDefinition Def(params PromptItem[] items) => new()
    { Items = [.. items], Arguments = [.. items.Select(i => PromptArgument.Item(i.Id))] };
    private static PromptItem Text(string initial, int id = 1) => new() { Id = id, Kind = PromptItemKind.Text, Label = "名前", Initial = initial };
    private static IEnumerable<Control> Controls(Control root) => root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Controls(c)));

    [Fact]
    public void Initial_AllMacrosAcrossTextKinds()
    {
        foreach (var kind in new[] { PromptItemKind.Text, PromptItemKind.Folder, PromptItemKind.File })
        foreach (var name in ArgumentTemplate.Names.Keys.Where(n => n != "prompt"))
        {
            var source = " \t\"${" + name + "}\"　$$$${prompt}";
            var definition = Def(Text(source) with { Kind = kind });
            var expected = ScalarTemplate.Parse(source).Expand(Context, out _);
            var result = PromptAnswerRules.Initial(definition, Context);
            Assert.Null(result.Error);
            Assert.Equal(expected, result.Values![1].Text);
            Assert.Equal(source, definition.Items[0].Initial);
        }
        var mixed = PromptAnswerRules.Defaults(Def(Text("${pathBase}|${fileBasenameNoExtension}")), Context);
        Assert.Equal(@"C:\work\v1.2|a", mixed.Values![1].Text);
        Assert.Equal("", PromptAnswerRules.Initial(Def(Text("${file}")), new([], null, @"C:\work")).Values![1].Text);
    }

    [Fact]
    public void Initial_RememberedEmptySkipsExpansion()
    {
        var definition = Def(Text("${file}!") with { LastText = "" }) with { RememberLast = true };
        var context = new MacroContext([], null, @"C:\work");
        Assert.Equal("", PromptAnswerRules.Initial(definition, context).Values![1].Text);
        var failure = PromptAnswerRules.Defaults(definition, context);
        Assert.Null(failure.Values);
        Assert.Equal(1, failure.ItemId);
        Assert.Contains("初期値", failure.Error!);
        Assert.Contains("file", failure.Error!);
        Assert.Equal("${file}!", definition.Items[0].Initial);
    }

    [Fact]
    public void Initial_RememberedTextIsLiteral()
    {
        var definition = Def(Text("${cwd}") with { LastText = "${unknown} $$" }, Text("${cwdBasename}", 2)) with { RememberLast = true };
        var result = PromptAnswerRules.Initial(definition, Context);
        Assert.Equal("${unknown} $$", result.Values![1].Text);
        Assert.Equal("work", result.Values[2].Text);
    }

    [Fact]
    public void Reset_FailureKeepsAllInputs()
    {
        var definition = Def(Text("${cwd}"), Text("${file}!", 2),
            new() { Id = 3, Kind = PromptItemKind.CheckBox, Label = "確認", Value = "x", InitialChecked = true },
            new() { Id = 4, Kind = PromptItemKind.DropDown, Label = "選択", InitialChoiceId = 1,
                Choices = [new() { Id = 1, Label = "初期" }, new() { Id = 2, Label = "入力" }] });
        var initial = new Dictionary<int, PromptValue> { [1] = new("入力1"), [2] = new("入力2"), [3] = new(Checked: false), [4] = new(ChoiceId: 2) };
        using var dialog = new PromptDialog(definition, "ツール", new FolderHistory(), null, new([], null, @"C:\work"), initial);
        var before = JsonSerializer.Serialize(dialog.ReadValues());
        Assert.NotNull(dialog.TryResetValues().Error);
        Assert.Equal(before, JsonSerializer.Serialize(dialog.ReadValues()));
        using var success = new PromptDialog(definition, "ツール", new FolderHistory(), null, Context, initial);
        Assert.Null(success.TryResetValues().Error);
        Assert.Equal(@"C:\work", success.ReadValues()[1].Text);
        Assert.Equal(@"C:\work\a.txt", success.ReadValues()[2].Text);
        Assert.True(success.ReadValues()[3].Checked);
        Assert.Equal(1, success.ReadValues()[4].ChoiceId);
    }

    [Fact]
    public void Values_AllChoicesValidatedButOnlySelectedExpanded()
    {
        var check = new PromptItem { Id = 1, Kind = PromptItemKind.CheckBox, Label = "確認", Value = "${file}!" };
        var drop = new PromptItem { Id = 2, Kind = PromptItemKind.DropDown, Label = "出力", InitialChoiceId = 1,
            Choices = [new() { Id = 1, Label = "なし", Value = "" }, new() { Id = 2, Label = "ファイル", Value = "${file}!" }] };
        Assert.Empty(PromptDefinitionRules.Validate(Def(check, drop)));
        var empty = new MacroContext([], null, @"C:\work");
        Assert.Empty(PromptExpander.ItemArguments(check, new(Checked: false), empty)!);
        Assert.Null(PromptExpander.ItemArguments(check, new(Checked: true), empty));
        Assert.Empty(PromptExpander.ItemArguments(drop, new(ChoiceId: 1), empty)!);
        Assert.Null(PromptExpander.ItemArguments(drop, new(ChoiceId: 2), empty));
        Assert.Equal(new[] { @"C:\work\a.txt", @"C:\work\b.txt" }, PromptExpander.ItemArguments(check, new(Checked: true), Context));
        var spaced = ReTAC.Domain.Entries.Entry.ForFile(@"C:\work\a b.txt", "a b.txt", 0, 0, default);
        Assert.Equal(new[] { @"C:\work\a b.txt" }, PromptExpander.ItemArguments(check, new(Checked: true), new([spaced], spaced, @"C:\work")));
    }

    [Fact]
    public void PerItem_ValuesUseEachTarget()
    {
        var definition = Def(Text("${fileBasenameNoExtension}"), new() { Id = 2, Kind = PromptItemKind.CheckBox, Label = "出力", Value = "${pathBase} ${cursorFileBasenameNoExtension} ${cwdBasename}", InitialChecked = true });
        var input = new PromptInput(definition, PromptAnswerRules.Initial(definition, Context).Values!);
        var tool = new ExternalTool { Path = "tool.exe", LaunchPerItem = true };
        var requests = LaunchPlanner.Plan(tool, ArgumentTemplate.Parse("${prompt}"), Context.Targets, Context.Cursor, Context.CurrentFolder, input);
        Assert.Equal(3, requests.Count);
        Assert.Equal(new[] { @"C:\work\v1.2", @"C:\work\a", @"C:\work\b" }, requests.Select(r => r.Arguments[1]));
        Assert.All(requests, r => { Assert.Equal("a", r.Arguments[0]); Assert.Equal("b", r.Arguments[2]); Assert.Equal("work", r.Arguments[3]); });
        var required = definition with { Items = [definition.Items[0], definition.Items[1] with { Value = "${file}!" }] };
        Assert.Equal(2, LaunchPlanner.Plan(tool, ArgumentTemplate.Parse("${prompt}"), Context.Targets, Context.Cursor, Context.CurrentFolder, input with { Definition = required }).Count);
    }

    [Fact]
    public void Definition_AllMacroFieldsUseSameRules()
    {
        foreach (var source in new[] { "${unknown}", "${file", "${prompt}" })
        {
            foreach (var kind in new[] { PromptItemKind.Text, PromptItemKind.Folder, PromptItemKind.File })
            {
                var problem = Assert.Single(PromptDefinitionRules.Validate(Def(Text(source) with { Kind = kind })));
                Assert.Equal(PromptField.Initial, problem.Field);
                Assert.Equal(1, problem.ItemId);
                Assert.Contains("名前", problem.Message);
                Assert.Contains("初期値", problem.Message);
            }
            var check = new PromptItem { Id = 1, Kind = PromptItemKind.CheckBox, Label = "確認", Value = source };
            Assert.Contains(PromptDefinitionRules.Validate(Def(check)), p => p.Field == PromptField.Value);
            var drop = new PromptItem { Id = 1, Kind = PromptItemKind.DropDown, Label = "出力", InitialChoiceId = 1,
                Choices = [new() { Id = 1, Label = "初期", Value = "" }, new() { Id = 9, Label = "未選択", Value = source }] };
            Assert.Contains(PromptDefinitionRules.Validate(Def(drop)), p => p.Field == PromptField.Value && p.ChoiceId == 9 && p.Message.Contains("未選択"));
        }
        foreach (var source in new[] { "\"", "${pathBase}:${file}" })
        {
            Assert.Empty(PromptDefinitionRules.Validate(Def(Text(source))));
            Assert.NotEmpty(PromptDefinitionRules.Validate(Def(new PromptItem { Id = 1, Kind = PromptItemKind.CheckBox, Label = "確認", Value = source })));
        }
        Assert.Empty(PromptDefinitionRules.Validate(Def(Text("$${prompt}"))));
    }

    [Fact]
    public void NonMacroFields_StayLiteral()
    {
        var definition = Def(Text("x") with { Label = "${unknown}", Prefix = "${unknown}", Suffix = "$$" }) with { Title = "${unknown}" };
        Assert.Empty(PromptDefinitionRules.Validate(definition));
        var raw = new Dictionary<int, PromptValue> { [1] = new("${unknown}") };
        Assert.Null(PromptAnswerRules.Check(definition, raw, Context.CurrentFolder).Error);
        Assert.Equal(new[] { "${unknown}${unknown}$$" }, PromptExpander.Expand(new(definition, raw), Context));
    }

    [Fact]
    public void Preview_UsesOnlyCapturedCwd()
    {
        var definition = Def(Text("${path}|${cursorPath}|${cwd}"));
        using var dialog = new PromptSettingsDialog(definition, "ツール", new FolderHistory(), null, @"E:\captured");
        Assert.Equal(@"||E:\captured", dialog.PreviewInitial().Values![1].Text);
        using var failing = new PromptSettingsDialog(Def(Text("${file}!")), "ツール", new FolderHistory(), null, @"E:\captured");
        var before = JsonSerializer.Serialize(failing.Current());
        Assert.NotNull(failing.PreviewInitial().Error);
        Assert.Equal(before, JsonSerializer.Serialize(failing.Current()));
        Assert.False(failing.CanUndo);
    }

    [Fact]
    public void Example_PreservesTemplatesAndEscapedLiterals()
    {
        var definition = Def(Text("${file}"), Text("$${file}", 2), new() { Id = 3, Kind = PromptItemKind.CheckBox, Label = "出力", Value = "${file} $${file}", InitialChecked = true });
        Assert.Equal("${file} $${file} ${file} $${file}", PromptExample.Render(definition));
        var preview = new Dictionary<int, PromptValue> { [1] = new("${unknown} $$"), [2] = new("x"), [3] = new(Checked: true) };
        Assert.Equal("\"${unknown} $$\" x ${file} $${file}", PromptExample.Render(definition, preview));
        var folder = new PromptItem { Id = 1, Kind = PromptItemKind.Folder, Label = "出力", Prefix = "-o " };
        Assert.Equal("-o ${cwd}", PromptExample.Render(Def(folder)));
        Assert.Equal("-o \"\"", PromptExample.Render(Def(folder with { FolderEmptyBehavior = PromptFolderEmptyBehavior.SendEmpty })));
    }
}
