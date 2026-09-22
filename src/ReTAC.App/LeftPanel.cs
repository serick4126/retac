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

    private readonly Button _selector = new SelectorButton { Dock = DockStyle.Top, TabStop = false, TextAlign = ContentAlignment.MiddleLeft };
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
        _selector.FontChanged += (_, _) => UpdateSelectorHeight();

        var empty = new Panel { Dock = DockStyle.Fill };
        _content.Controls.Add(empty);
        Controls.Add(_content);
        Controls.Add(_selector);
        UpdateSelector();
        UpdateSelectorHeight();
    }

    public static CommandId CommandOf(LeftPanelViewKind kind) => Views.Single(view => view.Kind == kind).Command;

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        UpdateSelectorHeight();
    }

    /// <summary>R-96 / R-66: 既定の 23px 固定では Meiryo 等の行の高いフォントや高 DPI で文字の上下が切れる。
    /// 高さはその時点のフォントの実測から決める。</summary>
    private void UpdateSelectorHeight() =>
        _selector.Height = TextRenderer.MeasureText("ドライブツリー ▼", _selector.Font).Height + _selector.LogicalToDeviceUnits(8);
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

    /// <summary>クリックでフォーカスを持つと、ツリーへ戻らないまま Enter で一覧が開き直す。
    /// 選択後のフォーカスはビュー側へ置くので、欄自体はフォーカスを受けない。</summary>
    private sealed class SelectorButton : Button
    {
        public SelectorButton() => SetStyle(ControlStyles.Selectable, false);
    }
}
