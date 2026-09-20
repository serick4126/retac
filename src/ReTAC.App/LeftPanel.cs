using System.Windows.Forms;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Keys;

namespace ReTAC.App;

/// <summary>R-96: 4 種類のうち常に 1 つだけを表示する左パネル。</summary>
public sealed class LeftPanel : UserControl
{
    private static readonly (LeftPanelViewKind Kind, string Label, CommandId Command)[] Views =
    [
        (LeftPanelViewKind.DriveTree, "ドライブツリー", CommandId.ShowDriveTree),
        (LeftPanelViewKind.DesktopTree, "デスクトップツリー", CommandId.ShowDesktopTree),
        (LeftPanelViewKind.Bookmarks, "ブックマーク", CommandId.ShowBookmarksView),
        (LeftPanelViewKind.Preview, "プレビュー", CommandId.ShowPreview),
    ];

    private readonly Button _selector = new() { Dock = DockStyle.Top, TabStop = false, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Panel _content = new() { Dock = DockStyle.Fill };
    private readonly Dictionary<LeftPanelViewKind, ToolStripMenuItem> _items = [];

    public LeftPanelViewKind ViewKind { get; private set; } = LeftPanelViewKind.DriveTree;
    public Control CurrentView => _content.Controls[0];
    public event EventHandler<LeftPanelViewKind>? ViewRequested;

    public LeftPanel()
    {
        Dock = DockStyle.Fill;
        var menu = new ContextMenuStrip();
        foreach (var (kind, label, _) in Views)
        {
            var item = new ToolStripMenuItem(label) { Tag = kind };
            item.Click += (_, _) => ViewRequested?.Invoke(this, kind);
            menu.Items.Add(item);
            _items[kind] = item;
        }
        _selector.ContextMenuStrip = menu;
        _selector.Click += (_, _) => menu.Show(_selector, new Point(0, _selector.Height));

        var empty = new Panel { Dock = DockStyle.Fill };
        _content.Controls.Add(empty);
        Controls.Add(_content);
        Controls.Add(_selector);
        UpdateSelector();
    }

    public void ShowView(LeftPanelViewKind kind, Control view)
    {
        // ビューの寿命は MainForm が所有し、切り替え後に再利用する。ここでは外すだけで破棄しない。
        _content.Controls.Clear();
        view.Dock = DockStyle.Fill;
        _content.Controls.Add(view);
        ViewKind = kind;
        UpdateSelector();
    }

    public void SetKeyMap(KeyMap keyMap)
    {
        var labels = MenuBar.KeyLabels(keyMap);
        foreach (var (kind, _, command) in Views)
            _items[kind].ShortcutKeyDisplayString = labels.GetValueOrDefault(new BuiltinTarget(command), "");
    }

    private void UpdateSelector() =>
        _selector.Text = $"{Views.Single(view => view.Kind == ViewKind).Label}  ▼";
}
