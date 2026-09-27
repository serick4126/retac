using System.Reflection;
using ReTAC.App;
using ReTAC.Domain.Keys;
using System.Windows.Forms;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-104-1 / P11-2: 開くたびに中身を作るサブメニュー。WinForms は子の無いメニューをキーボードで開かないので、
/// 一度もマウスで開いていなくても子があることを確かめる。
/// </summary>
public class DynamicSubmenuTests
{
    private static ToolStripMenuItem DriveSelectMenu(Func<IReadOnlyList<ToolStripItem>> drives)
    {
        var menu = MenuBar.Create(_ => { }, new KeyMap([]), [],
            new MenuDynamicContent(() => [], () => [], () => [], () => [], () => { }, drives),
            out _, out _, out _, out _, () => null);
        return Find(menu.Items, "ドライブの選択(&V)")!;
    }

    private static ToolStripMenuItem? Find(ToolStripItemCollection items, string text)
    {
        foreach (ToolStripItem item in items)
        {
            if (item is not ToolStripMenuItem menuItem) continue;
            if (menuItem.Text == text) return menuItem;
            if (Find(menuItem.DropDownItems, text) is { } found) return found;
        }
        return null;
    }

    /// <summary>DropDownOpening を起こす（ShowDropDown は画面に出してしまう）。</summary>
    private static void Open(ToolStripMenuItem item) =>
        typeof(ToolStripDropDownItem).GetMethod("OnDropDownShow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(item, [EventArgs.Empty]);

    [Fact]
    public void 作った直後でも子があるのでキーボードで開ける()
    {
        Assert.True(DriveSelectMenu(() => [new ToolStripMenuItem("C:")]).HasDropDownItems);
    }

    [Fact]
    public void 開くと仮項目が実際の一覧に替わる()
    {
        var menu = DriveSelectMenu(() => [new ToolStripMenuItem("C:"), new ToolStripMenuItem("D:")]);
        Open(menu);
        Assert.Equal(["C:", "D:"], menu.DropDownItems.Cast<ToolStripItem>().Select(i => i.Text));
    }

    [Fact]
    public void 一覧が空でも子が残るので次もキーボードで開ける()
    {
        var menu = DriveSelectMenu(() => []);
        Open(menu);
        Assert.True(menu.HasDropDownItems);
        Assert.All(menu.DropDownItems.Cast<ToolStripItem>(), i => Assert.False(i.Enabled));
    }
}
