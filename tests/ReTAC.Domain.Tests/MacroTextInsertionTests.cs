using ReTAC.App;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

public class MacroTextInsertionTests
{
    [Fact]
    public void SelectedRangeAndCaretOnly()
    {
        var entry = MacroCatalog.Entries.Single(e => e.Insert == "${cwd}");
        Assert.Equal(("pre ${cwd} tail", 10), MacroTextInsertion.Apply("pre OLD tail", 4, 3, entry));
        Assert.Equal(("pre${cwd} tail", 9), MacroTextInsertion.Apply("pre tail", 3, 0, entry));
        Assert.Equal(("x${cwd}", 7), MacroTextInsertion.Apply("x", 1, 0, entry));
        var caret = entry with { CaretOffset = 2 };
        Assert.Equal(("${cwd}", 2), MacroTextInsertion.Apply("old", 0, 3, caret));
        Assert.Equal(("old", 1), MacroTextInsertion.Apply("old", 1, 1, entry with { Insert = null }));
    }
}
