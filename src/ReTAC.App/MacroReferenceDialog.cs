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
    private readonly ListView _entries = new()
    {
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
        ShowGroups = true,
        Bounds = new Rectangle(14, 14, 560, 300),
    };
    private readonly TextBox _description = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Bounds = new Rectangle(14, 322, 560, 96),
    };
    private readonly Button _insert = new() { Text = "挿入(&I)", DialogResult = DialogResult.OK, Bounds = new Rectangle(384, 430, 90, 28) };

    /// <param name="showScripts">「実行できる書き方を見る」から開いたとき、スクリプトの例を選んだ状態で開く</param>
    public MacroReferenceDialog(bool showScripts = false)
    {
        Text = "マクロの一覧";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(588, 472);

        _entries.Columns.Add("項目", Scaled(220));
        _entries.Columns.Add("挿入する文字", Scaled(310));

        var groups = new Dictionary<string, ListViewGroup>();
        foreach (var entry in MacroCatalog.Entries)
        {
            if (!groups.TryGetValue(entry.Group, out var group))
            {
                group = new ListViewGroup(entry.Group);
                groups[entry.Group] = group;
                _entries.Groups.Add(group);
            }
            _entries.Items.Add(new ListViewItem([entry.Label, entry.Insert ?? ""]) { Group = group, Tag = entry });
        }

        var close = new Button { Text = "閉じる", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(484, 430, 90, 28) };
        Controls.AddRange([_entries, _description, _insert, close]);
        AcceptButton = _insert;
        CancelButton = close;

        _entries.SelectedIndexChanged += (_, _) => Sync();
        _entries.DoubleClick += (_, _) =>
        {
            if (Selected?.Insert is null) return;
            DialogResult = DialogResult.OK;
            Close();
        };

        Shown += (_, _) =>
        {
            var start = showScripts ? MacroCatalog.ScriptGroup : MacroCatalog.MacroGroup;
            var first = _entries.Items.Cast<ListViewItem>().FirstOrDefault(i => ((MacroCatalogEntry)i.Tag!).Group == start);
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

    private int Scaled(int logical) => logical * DeviceDpi / 96;

    private MacroCatalogEntry? Selected =>
        _entries.SelectedItems.Count == 0 ? null : (MacroCatalogEntry)_entries.SelectedItems[0].Tag!;

    /// <summary>挿入する項目。挿入しない項目を選んで閉じたときは null。</summary>
    public MacroCatalogEntry? Result => DialogResult == DialogResult.OK && Selected?.Insert is not null ? Selected : null;

    private void Sync()
    {
        _description.Text = Selected?.Description ?? "";
        // 挿入しない項目では押せない。Enter（AcceptButton）も効かない
        _insert.Enabled = Selected?.Insert is not null;
    }
}
