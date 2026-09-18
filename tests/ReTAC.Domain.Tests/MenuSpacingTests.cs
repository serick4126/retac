using System.Windows.Forms;
using ReTAC.App;

namespace ReTAC.Domain.Tests;

/// <summary>R-88: ReTAC が描くメニューの項目の上下に余白を足す</summary>
public class MenuSpacingTests
{
    [Fact]
    public void 項目には上下の余白を足し区切り線には足さない()
    {
        var child = new ToolStripMenuItem("子");
        var parent = new ToolStripMenuItem("親");
        parent.DropDownItems.Add(child);
        var separator = new ToolStripSeparator();
        var items = new ToolStrip().Items;
        items.AddRange(new ToolStripItem[] { parent, separator });

        MenuSpacing.Apply(items, dpi: 144);

        Assert.Equal((6, 6), (parent.Padding.Top, parent.Padding.Bottom));   // 4px × 1.5
        Assert.Equal((6, 6), (child.Padding.Top, child.Padding.Bottom));     // 入れ子にも掛かる
        Assert.Equal(new ToolStripSeparator().Padding, separator.Padding);
    }
}
