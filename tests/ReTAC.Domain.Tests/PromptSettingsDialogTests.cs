using System.Text.Json;
using ReTAC.App;
using ReTAC.Domain.Navigation;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

public class PromptSettingsDialogTests
{
    private static readonly PromptDefinition Source = new()
    {
        Title = "元の定義", Arguments = [PromptArgument.Fixed("original")],
    };

    [Theory]
    [InlineData("")]
    [InlineData("\"\" x")]
    [InlineData("\"bad.exe")]
    [InlineData("\"tool.exe\"x")]
    [InlineData("tool.exe \"x")]
    [InlineData("tool.exe\nx")]
    public void Import_InvalidDoesNotChangeDraft(string command)
    {
        using var dialog = new PromptSettingsDialog(Source, "ツール", new FolderHistory(), null, @"C:\work");
        var before = JsonSerializer.Serialize(dialog.Current());
        Assert.NotNull(dialog.TryImport(command));
        Assert.Equal(before, JsonSerializer.Serialize(dialog.Current()));
        Assert.Null(dialog.ImportedPath);
        Assert.False(dialog.CanUndo);
    }

    [Fact]
    public void Import_SuccessAndFailureKeepUndoBoundary()
    {
        using var dialog = new PromptSettingsDialog(Source, "ツール", new FolderHistory(), null, @"C:\work");
        Assert.Null(dialog.TryImport(@"C:\Program Files\tool.exe x"));
        Assert.Null(dialog.TryImport("other.exe y"));
        Assert.NotNull(dialog.TryImport("\"bad.exe"));
        dialog.Undo();
        Assert.Equal(@"C:\Program Files\tool.exe", dialog.ImportedPath);
        Assert.Equal("x", Assert.Single(dialog.Current().Arguments).Text);
        dialog.Undo();
        Assert.Null(dialog.ImportedPath);
        Assert.Equal("original", Assert.Single(dialog.Current().Arguments).Text);
        Assert.False(dialog.CanUndo);
    }

    [Fact]
    public void Convert_UndoRestoresPositionAndValue()
    {
        var source = Source with { Arguments = [PromptArgument.Fixed("before"), PromptArgument.Fixed("${file}"), PromptArgument.Fixed("after")] };
        using var dialog = new PromptSettingsDialog(source, "ツール", new FolderHistory(), null, @"C:\work");
        Assert.Null(dialog.ApplyConversion(1, ConversionTarget.DropDown));
        Assert.Equal(PromptItemKind.DropDown, Assert.Single(dialog.Current().Items).Kind);
        Assert.True(dialog.CanUndo);
        dialog.Undo();
        Assert.Equal(JsonSerializer.Serialize(source), JsonSerializer.Serialize(dialog.Current()));
        Assert.False(dialog.CanUndo);
        Assert.Null(dialog.ApplyConversion(0, ConversionTarget.Text));
        Assert.Null(dialog.ApplyConversion(0, ConversionTarget.Text)); // Item row: no further history.
        dialog.Undo();
        Assert.False(dialog.CanUndo);
    }

    [Fact]
    public void Convert_SelectsNewItemAndCancelDiscards()
    {
        var source = Source with { Arguments = [PromptArgument.Fixed("a\"b")] };
        using var dialog = new PromptSettingsDialog(source, "ツール", new FolderHistory(), null, @"C:\work");
        Assert.NotNull(dialog.ApplyConversion(0, ConversionTarget.CheckBox));
        static IEnumerable<System.Windows.Forms.Control> All(System.Windows.Forms.Control root) =>
            root.Controls.Cast<System.Windows.Forms.Control>().SelectMany(c => new[] { c }.Concat(All(c)));
        var list = All(dialog).OfType<System.Windows.Forms.ListView>().OrderBy(l => l.Top).First();
        Assert.True(Assert.Single(list.Items.Cast<System.Windows.Forms.ListViewItem>()).Selected);
        Assert.NotEmpty(PromptDefinitionRules.Validate(dialog.Current()));
        Assert.Single(dialog.Current().Items); // Invalid draft survives save validation.
        Assert.Empty(source.Items);
        Assert.Same(source, dialog.Definition); // Until OK, caller has original definition.
        Assert.Null(dialog.ImportedPath);
    }
}
