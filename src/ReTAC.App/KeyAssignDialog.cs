using System.Drawing;
using System.Windows.Forms;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Keys;
using ReTAC.Domain.Tools;

namespace ReTAC.App;

/// <summary>
/// キー割り当ての設定（0x8155・5-2 節）。ReTAC のキーの枠のそれぞれに、組み込みのコマンドか外部ツールを割り当てる。
/// R-12: 実行時のキー解決はここで作った <see cref="KeyMap"/> の 1 経路だけを通る。
///
/// コマンドは分類ごとにグループ化して並べ、右端に「今どのキーに付いているか」を出す。
/// 50 個超を 1 本のコンボから探すのは現実的でないため（実機指摘）。
/// 同じコマンドを複数のキーに割り当てられるので、キーは列挙して出す。
/// 名前の一部で絞り込める。割り当ては<b>ダブルクリック</b>で確定する。
/// 選ぶだけで割り当たると、誤クリックで設定が変わってしまうため（実機指摘）。
/// F-06: 登録した外部ツールは「登録した外部ツール」の分類に並ぶ。
/// </summary>
public sealed class KeyAssignDialog : Form
{
    private const string ToolCategory = "登録した外部ツール";

    private readonly ListView _slots = new()
    {
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
        Bounds = new Rectangle(14, 40, 300, 380),
    };
    private readonly ListView _commands = new()
    {
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
        ShowGroups = true,
        Bounds = new Rectangle(330, 66, 410, 354),
    };
    private readonly TextBox _filter = new()
    {
        Bounds = new Rectangle(330, 36, 410, 23),
        PlaceholderText = "絞り込み（名前の一部を入力。「コピー」「外部ツール」など）",
    };
    private readonly Dictionary<KeyBinding, CommandTarget?> _assignments = [];
    private readonly IReadOnlyList<ExternalTool> _tools;

    public KeyAssignDialog(KeyMap current, IReadOnlyList<ExternalTool> tools)
    {
        _tools = tools;

        Text = "キー割り当ての設定";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;   // ダイアログはタスクバーに出さない（既定は true）
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(754, 512);

        foreach (var slot in KeySlots.All) _assignments[slot] = current.Resolve(slot);

        Controls.Add(new Label { Text = "キー／マウスボタンを選んで、右のコマンドを割り当てます。", AutoSize = true, Location = new Point(14, 14) });
        Controls.Add(new Label { Text = "割り当てるコマンド(&C):", AutoSize = true, Location = new Point(330, 14) });

        // 列幅は AutoScaleMode.Dpi の対象外なので自分で追従させる（R-66。150% で文字が切れる）
        _slots.Columns.Add("キー／マウスボタン", Scaled(110));
        _slots.Columns.Add("コマンド", Scaled(166));   // 横スクロールバーが出ない幅に収める

        _commands.Columns.Add("コマンド", Scaled(250));
        _commands.Columns.Add("割り当て済み", Scaled(130));
        BuildCommandList();

        ReloadSlots(0);
        _slots.SelectedIndexChanged += (_, _) => SyncSelection();

        // 選ぶだけでは割り当てない。誤クリックで設定が変わらないようにする。
        // Enter は OK（このダイアログを閉じる）に譲る
        _commands.DoubleClick += (_, _) => Assign();
        _filter.TextChanged += (_, _) => BuildCommandList();

        Controls.Add(new Label
        {
            Text = "コマンドをダブルクリックすると、左で選んでいる枠に割り当てます。",
            AutoSize = true,
            Location = new Point(14, 428),
        });

        var reset = new Button { Text = "既定に戻す(&D)", Bounds = new Rectangle(14, 468, 130, 28) };
        reset.Click += (_, _) =>
        {
            var defaults = DefaultKeyMap.Create();
            foreach (var slot in KeySlots.All)
            {
                var target = defaults.Resolve(slot);
                // 初期登録のツールを削除していたら、そのキーは空のままにする（F-01）
                _assignments[slot] = target is ToolTarget tool && _tools.All(t => t.Id != tool.ToolId) ? null : target;
            }
            ReloadSlots(_slots.SelectedIndices.Count > 0 ? _slots.SelectedIndices[0] : 0);
        };

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(544, 468, 90, 28) };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(644, 468, 90, 28) };
        Controls.AddRange([_slots, _commands, _filter, reset, ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;

        // C-1: AutoScaleMode の代入はその場で PerformAutoScale を走らせる。ClientSize と
        // Controls が揃ってからでないと、まだ 96 DPI のレイアウトを拡大できない
        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16: 座標と大きさは 96 DPI（100%）で書いてある
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    private int Scaled(int logical) => logical * DeviceDpi / 96;

    /// <summary>OK で確定した割り当て。</summary>
    public KeyMap Result =>
        new(_assignments.Where(a => a.Value is not null)
            .Select(a => new KeyValuePair<KeyBinding, CommandTarget>(a.Key, a.Value!)));

    /// <summary>絞り込みの語で一覧を作り直す。分類名でも名前でも当たる。</summary>
    private void BuildCommandList()
    {
        var filter = _filter.Text.Trim();
        bool Matches(string category, string label) =>
            filter.Length == 0
            || label.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || category.Contains(filter, StringComparison.OrdinalIgnoreCase);

        _commands.BeginUpdate();
        _commands.Items.Clear();
        _commands.Groups.Clear();

        // 先頭に解除用の 1 行。グループ名を付けないと Windows が「既定」の見出しでまとめてしまう
        var none = new ListViewGroup("解除");
        _commands.Groups.Add(none);
        _commands.Items.Add(new ListViewItem([CommandLabels.Unassigned, ""]) { Group = none, Tag = null });

        ListViewGroup? group = null;
        foreach (var (category, command, label) in CommandLabels.Grouped)
        {
            if (!Matches(category, label)) continue;
            if (group?.Header != category)
            {
                group = new ListViewGroup(category);
                _commands.Groups.Add(group);
            }
            _commands.Items.Add(new ListViewItem([label, ""]) { Group = group, Tag = new BuiltinTarget(command) });
        }

        ListViewGroup? tools = null;
        foreach (var tool in _tools)
        {
            if (!Matches(ToolCategory, tool.Name)) continue;
            if (tools is null)
            {
                tools = new ListViewGroup(ToolCategory);
                _commands.Groups.Add(tools);
            }
            _commands.Items.Add(new ListViewItem([tool.Name, ""]) { Group = tools, Tag = new ToolTarget(tool.Id) });
        }

        _commands.EndUpdate();
        RefreshAssignedKeys();
        SyncSelection();
    }

    /// <summary>右端の「割り当て済みのキー」を今の割り当てから作り直す。</summary>
    private void RefreshAssignedKeys()
    {
        foreach (ListViewItem item in _commands.Items)
        {
            // 同じコマンドを複数のキーに割り当てられる。キーの並びは枠の順
            var keys = item.Tag is CommandTarget target
                ? string.Join(" ", KeySlots.All.Where(slot => Equals(_assignments[slot], target)).Select(KeySlots.Display))
                : "";
            item.SubItems[1].Text = keys;
        }
    }

    private void ReloadSlots(int select)
    {
        _slots.BeginUpdate();
        _slots.Items.Clear();
        foreach (var slot in KeySlots.All)
        {
            var item = new ListViewItem([KeySlots.Display(slot), CommandLabels.Of(_assignments[slot], _tools)]);
            // 未割り当ての行は薄くする。「(割り当てなし)」がコマンド名と同じ濃さで並ぶと、
            // どのキーが空いているか目で拾えない
            if (_assignments[slot] is null) item.ForeColor = SystemColors.GrayText;
            _slots.Items.Add(item);
        }
        _slots.EndUpdate();
        RefreshAssignedKeys();

        if (_slots.Items.Count == 0) return;
        var index = Math.Clamp(select, 0, _slots.Items.Count - 1);
        _slots.Items[index].Selected = true;
        _slots.Items[index].EnsureVisible();
        SyncSelection();
    }

    /// <summary>選んだキーに付いているコマンドを右の一覧で選び直す。</summary>
    private void SyncSelection()
    {
        if (_slots.SelectedIndices.Count == 0) return;
        var target = _assignments[KeySlots.All[_slots.SelectedIndices[0]]];

        foreach (ListViewItem item in _commands.Items)
        {
            var match = Equals(item.Tag, target);
            item.Selected = match;
            if (match) item.EnsureVisible();
        }
    }

    private void Assign()
    {
        // 分類の見出しをクリックしただけのときは項目が選ばれていない。何もしない
        if (_slots.SelectedIndices.Count == 0 || _commands.SelectedItems.Count == 0) return;

        var index = _slots.SelectedIndices[0];
        var slot = KeySlots.All[index];
        _assignments[slot] = (CommandTarget?)_commands.SelectedItems[0].Tag;

        _slots.Items[index].SubItems[1].Text = CommandLabels.Of(_assignments[slot], _tools);
        _slots.Items[index].ForeColor = _assignments[slot] is null ? SystemColors.GrayText : _slots.ForeColor;
        RefreshAssignedKeys();
    }
}
