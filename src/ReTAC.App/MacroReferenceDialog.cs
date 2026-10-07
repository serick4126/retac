using System.Drawing;
using System.Windows.Forms;
using ReTAC.Domain.Tools;

namespace ReTAC.App;

/// <summary>
/// マクロの一覧（F-04）。外部ツールの設定から開き、選んだマクロを引数欄に挿入する。
/// 書き方の説明とスクリプトの例は挿入しない（見るだけ）。
/// A-02: ↑↓ で選び、Enter で挿入、Esc で閉じる。
/// </summary>
public sealed class MacroReferenceDialog : Form
{
    private readonly GroupedListView _entries = new()
    {
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
        ShowGroups = true,
        Bounds = new Rectangle(14, 60, 912, 296),
        Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
    };
    private readonly TextBox _description = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Bounds = new Rectangle(14, 368, 912, 134),
        Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
    };
    private readonly Button _insert = new() { Text = "挿入(&I)", DialogResult = DialogResult.OK, Bounds = new Rectangle(736, 518, 90, 28), Anchor = AnchorStyles.Bottom | AnchorStyles.Right, Enabled = false };

    /// <param name="showScripts">「実行できる書き方を見る」から開いたとき、スクリプトの例を選んだ状態で開く</param>
    public MacroReferenceDialog(MacroUsage usage = MacroUsage.Arguments, bool showScripts = false)
    {
        Text = "マクロの一覧";
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(940, 560);

        _entries.Columns.Add("説明", Scaled(580));
        _entries.Columns.Add("挿入する文字", Scaled(310));

        var groups = new Dictionary<string, ListViewGroup>();
        foreach (var entry in MacroCatalog.For(usage))
        {
            if (!groups.TryGetValue(entry.Group, out var group))
            {
                group = new ListViewGroup(entry.Group);
                groups[entry.Group] = group;
                _entries.Groups.Add(group);
            }
            _entries.Items.Add(new ListViewItem([entry.Summary, entry.Insert ?? "—"]) { Group = group, Tag = entry });
        }

        var close = new Button { Text = "閉じる", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(836, 518, 90, 28), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        var help = new Label { Text = MacroCatalog.ContextHelp(usage), Bounds = new Rectangle(14, 14, 912, 38),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        Controls.AddRange([help, _entries, _description, _insert, close]);
        // R-145-1: 下端の左。案内の行の右端は「挿入」の手前に合わせ、大きさを変えても重ならないようにする
        MacroBuildPromptButton.Attach(this, new Point(14, 518), _insert.Left - 10, AnchorStyles.Bottom | AnchorStyles.Left);
        AcceptButton = _insert;
        CancelButton = close;

        _entries.SelectedIndexChanged += (_, _) => Sync();
        _entries.DoubleClick += (_, _) =>
        {
            if (!CanInsert(Selected)) return;
            DialogResult = DialogResult.OK;
            Close();
        };

        Shown += (_, _) =>
        {
            var first = _entries.Items.Cast<ListViewItem>().FirstOrDefault(i => (MacroCatalogEntry)i.Tag! is var e
                && (showScripts ? e.Group == MacroCatalog.ScriptGroup : MacroCatalog.IsMacro(e)));
            if (first is not null)
            {
                first.Selected = true;
                first.Focused = true;
                first.EnsureVisible();
            }
            _entries.Focus();
            Sync();
        };

        // C-1: ClientSize と Controls が揃ってから
        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var area = Screen.FromControl(Owner ?? (Control)this).WorkingArea;
        MinimumSize = ClampSize(SizeFromClientSize(new Size(Scaled(700), Scaled(460))), area.Size);
        Size = ClampSize(Size, area.Size);
        StartPosition = FormStartPosition.Manual;
        Location = DialogPlacement.Place(Owner?.Bounds ?? Bounds, Size, area);
    }

    internal static Size ClampSize(Size desired, Size workArea) =>
        new(Math.Min(desired.Width, workArea.Width), Math.Min(desired.Height, workArea.Height));

    internal bool CanInsert(MacroCatalogEntry? entry) => entry?.Insert is not null;

    internal IReadOnlyList<string> ColumnTitles => _entries.Columns.Cast<ColumnHeader>().Select(c => c.Text).ToList();

    internal IReadOnlyList<(string Summary, string Label, string? Insert, string Group)> Rows =>
        _entries.Items.Cast<ListViewItem>().Select(i => (i.SubItems[0].Text, ((MacroCatalogEntry)i.Tag!).Label,
            ((MacroCatalogEntry)i.Tag!).Insert is null ? null : i.SubItems[1].Text, i.Group!.Header)).ToList();

    private int Scaled(int logical) => logical * DeviceDpi / 96;

    private MacroCatalogEntry? Selected =>
        _entries.SelectedItems.Count == 0 ? null : (MacroCatalogEntry)_entries.SelectedItems[0].Tag!;

    /// <summary>挿入する項目。挿入しない項目を選んで閉じたときは null。</summary>
    public MacroCatalogEntry? Result => DialogResult == DialogResult.OK && CanInsert(Selected) ? Selected : null;

    private void Sync()
    {
        _description.Text = Selected?.Description ?? "";
        // 挿入しない項目では押せない。Enter（AcceptButton）も効かない
        _insert.Enabled = CanInsert(Selected);
    }

    /// <summary>一覧に出している挿入する文字。</summary>
    internal IEnumerable<string> Inserts => _entries.Items.Cast<ListViewItem>().Select(i => ((MacroCatalogEntry)i.Tag!).Insert).OfType<string>();
}
