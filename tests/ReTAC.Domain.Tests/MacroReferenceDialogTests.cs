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
            Assert.Equal(new[] { "説明", "挿入する文字" }, dialog.ColumnTitles);
            Assert.All(dialog.Rows, r => Assert.False(string.IsNullOrWhiteSpace(r.Summary)));
            Assert.Contains(dialog.Rows, r => r.Insert == "${cwdBasename}" && r.Summary == "実行時のフォルダ名");
            Assert.Equal(MacroCatalog.For(usage).Select(e => (e.Summary, e.Label, e.Insert, e.Group)), dialog.Rows);
        }
    }

    [Fact]
    public void MacroList_ShowsInsertionTextOrHelpMarkerWithoutDuplicateColumn()
    {
        foreach (var usage in Enum.GetValues<MacroUsage>())
        {
            using var dialog = new MacroReferenceDialog(usage);
            var rows = dialog.Controls.OfType<ListView>().Single().Items.Cast<ListViewItem>().ToList();
            Assert.All(rows, row => Assert.Equal(2, row.SubItems.Count));
            var file = rows.Single(row => ((MacroCatalogEntry)row.Tag!).Insert == "${file}");
            Assert.Equal("対象ファイルのフルパス（フォルダ除外）", file.SubItems[0].Text);
            Assert.Equal("${file}", file.SubItems[1].Text);
            var help = rows.Where(row => ((MacroCatalogEntry)row.Tag!).Insert is null).ToList();
            Assert.NotEmpty(help);
            Assert.All(help, row =>
            {
                Assert.Equal("—", row.SubItems[1].Text);
                Assert.False(dialog.CanInsert((MacroCatalogEntry)row.Tag!));
            });
        }
    }

    [Fact]
    public void MacroList_InitialHelpUsesScalarRules()
    {
        using var dialog = new MacroReferenceDialog(MacroUsage.InitialValue);
        Assert.Contains("先頭の 1 件", dialog.Controls.OfType<Label>().Single(l => l.Text != MacroBuildPromptActions.Guide).Text);
        var quotes = MacroCatalog.For(MacroUsage.InitialValue).Single(e => e.Label.Contains("引用符"));
        Assert.Contains("文字", quotes.Description);
        Assert.DoesNotContain("取り除く", quotes.Description);
        Assert.DoesNotContain("空の値", quotes.Description);
    }

    // R-145-1: 最小の幅・縮めたとき・DPI を変えたときに、ボタンと案内の行がほかの子に重ならず、はみ出さない
    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.5f)]
    [InlineData(2.0f)]
    public void PromptButton_FitsWithoutOverlap(float scale)
    {
        using var dialog = new MacroReferenceDialog();
        if (scale != 1.0f) dialog.Scale(new SizeF(scale, scale));
        foreach (var width in new[] { 700, 820, 940 })
        {
            dialog.ClientSize = new Size((int)(width * scale), (int)(560 * scale));
            dialog.PerformLayout();
            var client = new Rectangle(Point.Empty, dialog.ClientSize);
            var button = dialog.Controls.OfType<Button>().Single(b => b.Text.StartsWith("マクロ構築プロンプト", StringComparison.Ordinal));
            var guide = dialog.Controls.OfType<Label>().Single(l => l.Text == MacroBuildPromptActions.Guide);
            var others = dialog.Controls.Cast<Control>().Where(c => c != button && c != guide && c.Visible).ToList();

            Assert.True(client.Contains(button.Bounds), $"{scale}/{width}: ボタンがはみ出す {button.Bounds}");
            Assert.True(client.Contains(guide.Bounds), $"{scale}/{width}: 案内がはみ出す {guide.Bounds}");
            Assert.True(guide.Width > 0, $"{scale}/{width}: 案内の幅が無い");
            Assert.False(button.Bounds.IntersectsWith(guide.Bounds));
            foreach (var other in others)
            {
                Assert.False(button.Bounds.IntersectsWith(other.Bounds), $"{scale}/{width}: ボタンが {other.Text} {other.Bounds} に重なる");
                Assert.False(guide.Bounds.IntersectsWith(other.Bounds), $"{scale}/{width}: 案内 {guide.Bounds} が {other.Text} {other.Bounds} に重なる");
            }
        }
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
