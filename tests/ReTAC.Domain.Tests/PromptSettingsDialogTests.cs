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
}
