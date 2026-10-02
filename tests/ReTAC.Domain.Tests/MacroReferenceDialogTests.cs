using System.Drawing;
using System.Windows.Forms;
using ReTAC.App;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

public class MacroReferenceDialogTests
{
    [Fact]
    public void MacroList_AllEntrancesShareColumns()
    {
        foreach (var usage in Enum.GetValues<MacroUsage>())
        {
            using var dialog = new MacroReferenceDialog(usage);
            Assert.Equal(new[] { "説明", "項目", "挿入する文字" }, dialog.ColumnTitles);
            Assert.All(dialog.Rows, r => Assert.False(string.IsNullOrWhiteSpace(r.Summary)));
            Assert.Contains(dialog.Rows, r => r.Insert == "${cwdBasename}" && r.Summary == "実行時のフォルダ名");
            Assert.Equal(MacroCatalog.For(usage).Select(e => (e.Summary, e.Label, e.Insert, e.Group)), dialog.Rows);
        }
    }

    [Fact]
    public void MacroList_InitialHelpUsesScalarRules()
    {
        using var dialog = new MacroReferenceDialog(MacroUsage.InitialValue);
        Assert.Contains("先頭の 1 件", dialog.Controls.OfType<Label>().Single().Text);
        var quotes = MacroCatalog.For(MacroUsage.InitialValue).Single(e => e.Label.Contains("引用符"));
        Assert.Contains("文字", quotes.Description);
        Assert.DoesNotContain("取り除く", quotes.Description);
        Assert.DoesNotContain("空の値", quotes.Description);
    }

    [Fact]
    public void MacroList_RowSelectionAndInsertRules()
    {
        using var dialog = new MacroReferenceDialog();
        Assert.True(dialog.Controls.OfType<ListView>().Single().FullRowSelect);
        Assert.NotNull(dialog.AcceptButton);
        Assert.NotNull(dialog.CancelButton);
        Assert.False(dialog.CanInsert(null));
        foreach (var entry in MacroCatalog.Entries) Assert.Equal(entry.Insert is not null, dialog.CanInsert(entry));
        Assert.Equal(FormBorderStyle.Sizable, dialog.FormBorderStyle);
        Assert.Contains(dialog.Controls.Cast<Control>(), c => c.Anchor.HasFlag(AnchorStyles.Right) && c.Anchor.HasFlag(AnchorStyles.Bottom));
    }

    [Theory]
    [InlineData(956, 599, 1920, 1040)]
    [InlineData(956, 599, 600, 400)]
    [InlineData(1434, 899, 1366, 728)]
    [InlineData(716, 499, 600, 400)]
    public void MacroList_FitsSmallWorkArea(int width, int height, int areaWidth, int areaHeight)
    {
        var size = MacroReferenceDialog.ClampSize(new(width, height), new(areaWidth, areaHeight));
        Assert.Equal(new Size(Math.Min(width, areaWidth), Math.Min(height, areaHeight)), size);
    }
}
