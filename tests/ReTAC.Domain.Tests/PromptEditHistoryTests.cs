using ReTAC.App;
using ReTAC.Domain.Tools;
using Xunit;

namespace ReTAC.Domain.Tests;

/// <summary>ヘルパーの「元に戻す」の積み置き場（R-133）。</summary>
public class PromptEditHistoryTests
{
    private static void Push(PromptEditHistory history, int id, string? path = null) =>
        history.Push([new PromptItem { Id = id }], [PromptArgument.Item(id)], path);

    private static PromptItem Drop(string label = "a") => new()
    {
        Id = 1, Kind = PromptItemKind.DropDown, InitialChoiceId = 1,
        Choices = [new PromptChoice { Id = 1, Label = label, Value = "x" }],
    };

    [Fact]
    public void SameItem_compares_choices_by_value()
    {
        Assert.True(PromptEditHistory.SameItem(Drop(), Drop()));
        Assert.False(PromptEditHistory.SameItem(Drop("a"), Drop("b")));
        Assert.False(PromptEditHistory.SameItem(Drop(), Drop() with { Label = "L" }));
        var two = Drop() with { Choices = [.. Drop().Choices, new PromptChoice { Id = 2, Label = "c" }] };
        Assert.False(PromptEditHistory.SameItem(Drop(), two));
    }

    [Fact]
    public void Empty_cannot_undo()
    {
        var history = new PromptEditHistory();
        Assert.False(history.CanUndo);
        Assert.False(history.TryUndo(out _));
    }

    [Fact]
    public void Undo_returns_pushed_states_newest_first()
    {
        var history = new PromptEditHistory();
        Push(history, 1);
        Push(history, 2, "a.exe");
        Assert.True(history.TryUndo(out var second));
        Assert.Equal(2, second.Items[0].Id);
        Assert.Equal("a.exe", second.ImportedPath);
        Assert.True(history.TryUndo(out var first));
        Assert.Equal(1, first.Items[0].Id);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Pushed_state_is_not_changed_by_later_edits_of_the_source_lists()
    {
        var items = new List<PromptItem> { new() { Id = 1 } };
        var arguments = new List<PromptArgument> { PromptArgument.Item(1) };
        var history = new PromptEditHistory();
        history.Push(items, arguments, null);
        items.Clear();
        arguments.Add(PromptArgument.Fixed("x"));
        Assert.True(history.TryUndo(out var state));
        Assert.Single(state.Items);
        Assert.Single(state.Arguments);
    }

    [Fact]
    public void Oldest_states_are_dropped_over_the_limit()
    {
        var history = new PromptEditHistory();
        for (var i = 0; i < PromptEditHistory.Limit + 5; i++) Push(history, i);
        var count = 0;
        var last = -1;
        while (history.TryUndo(out var s)) { count++; last = s.Items[0].Id; }
        Assert.Equal(PromptEditHistory.Limit, count);
        Assert.Equal(5, last);
    }

    [Fact]
    public void New_dialog_cannot_undo()
    {
        using var dialog = new PromptSettingsDialog(null, "t", new ReTAC.Domain.Navigation.FolderHistory(), null, "C:\\");
        Assert.False(dialog.CanUndo);
    }
}
