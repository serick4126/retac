using ReTAC.App;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Keys;
using System.Windows.Forms;

namespace ReTAC.Domain.Tests;

public class LeftPanelTests
{
    private static readonly LeftPanelViewKind[] Kinds =
        [LeftPanelViewKind.DriveTree, LeftPanelViewKind.DesktopTree, LeftPanelViewKind.Bookmarks, LeftPanelViewKind.Preview];

    [Fact]
    public void 上端の選択欄は4ビューだけを仕様順に持ち閉じる操作を持たない()
    {
        using var panel = new LeftPanel();
        var selector = panel.Controls.OfType<Button>().Single();
        var items = selector.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().ToList();

        Assert.False(selector.TabStop);
        Assert.Equal(["ドライブツリー", "デスクトップツリー", "ブックマーク", "プレビュー"], items.Select(i => i.Text));
        Assert.DoesNotContain(items, i => i.Text?.Contains("非表示") == true || i.Text?.Contains('×') == true);
    }

    [Fact]
    public void 内容ホストはビューを切り替えても常に1個だけを持つ()
    {
        using var panel = new LeftPanel();
        var first = new Panel();
        var second = new Panel();

        panel.ShowView(LeftPanelViewKind.DesktopTree, first);
        panel.ShowView(LeftPanelViewKind.Bookmarks, second);

        Assert.Equal(LeftPanelViewKind.Bookmarks, panel.ViewKind);
        Assert.Same(second, panel.CurrentView);
        Assert.Single(second.Parent!.Controls);
    }

    [Fact]
    public void 選択欄の高さはフォントに合わせて文字が切れない高さになる()
    {
        using var panel = new LeftPanel();
        var selector = panel.Controls.OfType<Button>().Single();
        using var font = new System.Drawing.Font("Meiryo UI", 24f);
        panel.Font = font;

        Assert.True(selector.Height > TextRenderer.MeasureText(selector.Text, font).Height);
        Assert.False(selector.CanSelect);
    }

    [Fact]
    public void 選択欄は要求だけを通知して自分ではビューを変えない()

    {
        using var panel = new LeftPanel();
        LeftPanelViewKind? requested = null;
        panel.ViewRequested += (_, kind) => requested = kind;

        var selector = panel.Controls.OfType<Button>().Single();
        selector.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().ElementAt(3).PerformClick();

        Assert.Equal(LeftPanelViewKind.Preview, requested);
        Assert.Equal(LeftPanelViewKind.DriveTree, panel.ViewKind);
    }

    [Fact]
    public void 上端一覧と表示メニューは割り当てキーを表示して同じコマンドを通知する()
    {
        var map = new KeyMap([]);
        map.Assign(new KeyBinding(Vk.Letter('P')), new BuiltinTarget(CommandId.ShowPreview));
        using var panel = new LeftPanel();
        panel.SetKeyMap(map);
        var preview = panel.Controls.OfType<Button>().Single().ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().ElementAt(3);
        Assert.Equal("P", preview.ShortcutKeyDisplayString);
        panel.SetKeyMap(new KeyMap([]));
        Assert.Equal("", preview.ShortcutKeyDisplayString);

        CommandTarget? dispatched = null;
        using var menu = MenuBar.Create(target => dispatched = target, map, [], NoDynamicContent(), out _, out _, out _, out var leftPanel, () => null);
        Assert.Equal("P", leftPanel.Views[LeftPanelViewKind.Preview].ShortcutKeyDisplayString);
        leftPanel.Views[LeftPanelViewKind.Preview].PerformClick();
        Assert.Equal(new BuiltinTarget(CommandId.ShowPreview), dispatched);
    }

    [Fact]
    public void 表示メニューの左パネルはチェック項目と4個のラジオ項目を持つ()
    {
        using var menu = MenuBar.Create(_ => { }, new KeyMap([]), [], NoDynamicContent(), out _, out _, out _, out var leftPanel, () => null);

        Assert.Equal(CommandId.ToggleLeftPanel, leftPanel.Root.DropDownItems[0].Tag);
        Assert.Equal(6, leftPanel.Root.DropDownItems.Count);
        Assert.IsType<ToolStripSeparator>(leftPanel.Root.DropDownItems[1]);
        Assert.Equal(Kinds, leftPanel.Views.Keys);
        Assert.All(leftPanel.Views.Values, item => Assert.IsType<RadioToolStripMenuItem>(item));
        leftPanel.Views[LeftPanelViewKind.DriveTree].Checked = true;
        leftPanel.Views[LeftPanelViewKind.Preview].Checked = true;
        Assert.False(leftPanel.Views[LeftPanelViewKind.DriveTree].Checked);
        Assert.True(leftPanel.Views[LeftPanelViewKind.Preview].Checked);
    }

    /// <summary>状態で中身が変わるサブメニュー（R-104-1）はこのテストの対象外なので、中身を持たない関数の束を渡す。</summary>
    private static MenuDynamicContent NoDynamicContent() =>
        new(() => [], () => [], () => [], () => [], () => { }, () => []);
}
