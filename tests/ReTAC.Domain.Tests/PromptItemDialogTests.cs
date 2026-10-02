using System.Windows.Forms;
using ReTAC.App;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

public class PromptItemDialogTests
{
    private static IEnumerable<Control> All(Control root) => root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(All(c)));
    private static PromptItem Item(PromptItemKind kind) => new() { Id = 1, Kind = kind, Label = "名前", Initial = "${cwd}", Value = "${file}", InitialChoiceId = 7,
        Choices = [new() { Id = 7, Label = "A", Value = "pre OLD tail" }, new() { Id = 9, Label = "B", Value = "other" }] };

    [Fact]
    public void FolderEditor_RequiredPreservesEmptySelection()
    {
        using var dialog = new PromptItemDialog(Item(PromptItemKind.Folder) with { Required = true, FolderEmptyBehavior = PromptFolderEmptyBehavior.SendEmpty });
        var required = All(dialog).OfType<CheckBox>().Single();
        var group = All(dialog).OfType<GroupBox>().Single();
        var send = All(group).OfType<RadioButton>().Single(r => r.Text.Contains("そのまま"));
        Assert.True(required.Checked);
        Assert.False(group.Enabled);
        Assert.True(send.Checked);
        required.Checked = false;
        Assert.True(group.Enabled);
        Assert.True(send.Checked);
        required.Checked = true;
        Assert.False(group.Enabled);
        Assert.Equal(PromptFolderEmptyBehavior.SendEmpty, dialog.Build().FolderEmptyBehavior);
        required.Checked = false;
        Assert.False(dialog.Build().Required);
        Assert.Equal(PromptFolderEmptyBehavior.SendEmpty, dialog.Build().FolderEmptyBehavior);
    }

    [Fact]
    public void ItemEditor_MacroTargets()
    {
        foreach (var kind in Enum.GetValues<PromptItemKind>())
        {
            using var dialog = new PromptItemDialog(Item(kind));
            var button = Assert.Single(All(dialog).OfType<Button>(), b => b.Tag is MacroUsage);
            Assert.Equal(kind is PromptItemKind.Text or PromptItemKind.Folder or PromptItemKind.File ? MacroUsage.InitialValue : MacroUsage.ItemValue, button.Tag);
            if (kind != PromptItemKind.Folder)
            {
                Assert.Empty(All(dialog).OfType<RadioButton>());
                Assert.Equal(PromptFolderEmptyBehavior.CurrentFolder, dialog.Build().FolderEmptyBehavior);
            }
            var entries = MacroCatalog.For((MacroUsage)button.Tag!);
            Assert.DoesNotContain(entries, e => e.Insert == "${prompt}" || e.Group == MacroCatalog.ScriptGroup);
        }
    }

    [Fact]
    public void ChoiceMacro_ReplacesSelectionOnly()
    {
        using var dialog = new PromptItemDialog(Item(PromptItemKind.DropDown));
        var entry = MacroCatalog.Entries.Single(e => e.Insert == "${cwd}");
        var before = dialog.Build();
        Assert.True(dialog.InsertChoiceMacro(0, 4, 3, entry));
        var item = dialog.Build();
        Assert.Equal("pre ${cwd} tail", item.Choices[0].Value);
        Assert.Equal(before.Choices[0].Label, item.Choices[0].Label);
        Assert.Equal(before.Choices[1], item.Choices[1]);
        Assert.Equal(before.InitialChoiceId, item.InitialChoiceId);
        Assert.Equal(before.Choices.Select(c => c.Id), item.Choices.Select(c => c.Id));
        Assert.True(dialog.InsertChoiceMacro(1, 2, 0, entry));
        Assert.Equal("ot${cwd}her", dialog.Build().Choices[1].Value);
        Assert.True(dialog.InsertChoiceMacro(1, dialog.Build().Choices[1].Value.Length, 0, entry));
        Assert.Equal("ot${cwd}her${cwd}", dialog.Build().Choices[1].Value);
        Assert.False(dialog.InsertChoiceMacro(-1, 0, 0, entry));
        Assert.False(dialog.InsertChoiceMacro(2, 0, 0, entry)); // New row.
        var unchanged = dialog.Build().Choices[0].Value;
        Assert.False(dialog.InsertChoiceMacro(0, 0, 0, entry with { Insert = null }));
        Assert.Equal(unchanged, dialog.Build().Choices[0].Value);
    }

    [Fact]
    public void ItemEditor_InvalidDraftIsEditable()
    {
        foreach (var kind in Enum.GetValues<PromptItemKind>())
        {
            var source = Item(kind) with { Label = "", Initial = "bad|path", Value = "\"" };
            using var dialog = new PromptItemDialog(source);
            var built = dialog.Build();
            Assert.Equal("", built.Label);
            if (kind is PromptItemKind.Text or PromptItemKind.File or PromptItemKind.Folder) Assert.Equal("bad|path", built.Initial);
            if (kind == PromptItemKind.CheckBox) Assert.Equal("\"", built.Value);
            Assert.NotEmpty(PromptDefinitionRules.Validate(new() { Items = [built], Arguments = [PromptArgument.Item(1)] }));
        }
    }

    [Fact]
    public void ItemEditor_DefinitionTextIsNotExpanded()
    {
        using var dialog = new PromptItemDialog(Item(PromptItemKind.Folder));
        Assert.Contains(All(dialog).OfType<TextBox>(), b => b.Text == "${cwd}");
        Assert.Equal("${cwd}", dialog.Build().Initial);
    }

    [Fact]
    public void ItemEditor_ProblemTargetUsesFieldAndChoiceId()
    {
        using var text = new PromptItemDialog(Item(PromptItemKind.Text));
        Assert.Equal("${cwd}", text.ProblemTarget(new("problem", 1, Field: PromptField.Initial)).Control.Text);
        using var drop = new PromptItemDialog(Item(PromptItemKind.DropDown));
        var target = drop.ProblemTarget(new("problem", 1, Field: PromptField.Value, ChoiceId: 9));
        Assert.IsType<DataGridView>(target.Control);
        Assert.Equal(1, target.Row);
        Assert.Equal(2, target.Column);
    }
}
