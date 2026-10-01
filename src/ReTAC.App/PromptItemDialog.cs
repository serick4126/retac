using System.Drawing;
using System.Windows.Forms;
using ReTAC.Domain.Navigation;
using ReTAC.Domain.Tools;

namespace ReTAC.App;

/// <summary>
/// 入力ダイアログの 1 項目の編集（R-133）。種類は表示するだけで変えない（前回の値の意味が変わるため）。
/// 欄は種類ごとに出し分ける。OK のときに定義と同じ検査をかける。
/// </summary>
public sealed class PromptItemDialog : Form
{
    private readonly PromptItem _source;
    private readonly TextBox _label = new() { Width = 300 };
    private readonly TextBox _initial = new() { Width = 300 };
    private readonly TextBox _prefix = new() { Width = 300 };
    private readonly TextBox _suffix = new() { Width = 300 };
    private readonly TextBox _value = new() { Width = 300 };
    private readonly CheckBox _required = new() { Text = "入力を必須にする(&Q)", AutoSize = true };
    private readonly CheckBox _initialChecked = new() { Text = "初期値をオンにする(&O)", AutoSize = true };
    private readonly DataGridView _choices = new()
    {
        Size = new Size(420, 160),
        AllowUserToAddRows = true,
        AllowUserToResizeRows = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
    };
    private readonly Label _sample = new() { AutoSize = true, UseMnemonic = false };

    public PromptItemDialog(PromptItem item)
    {
        _source = item;
        Result = item;
        Text = "項目の編集";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;

        var y = 14;
        void Row(string caption, Control control)
        {
            Controls.Add(new Label { Text = caption, AutoSize = true, Location = new Point(14, y + 3) });
            control.Location = new Point(140, y);
            Controls.Add(control);
            y += 32;
        }

        Controls.Add(new Label { Text = $"種類: {PromptSettingsDialog.KindName(item.Kind)}", AutoSize = true, Location = new Point(14, y) });
        y += 28;
        _label.Text = item.Label;
        Row("ラベル(&L):", _label);

        switch (item.Kind)
        {
            case PromptItemKind.Text or PromptItemKind.Folder or PromptItemKind.File:
                _initial.Text = item.Initial;
                _prefix.Text = item.Prefix;
                _suffix.Text = item.Suffix;
                Row("初期値(&I):", _initial);
                Row("前に付ける(&B):", _prefix);
                Row("後ろに付ける(&F):", _suffix);
                if (item.Kind != PromptItemKind.Folder)
                {
                    _required.Checked = item.Required;
                    _required.Location = new Point(140, y);
                    Controls.Add(_required);
                    y += 28;
                }
                else
                {
                    Controls.Add(new Label { Text = "空欄で OK するとカレントフォルダを送ります。", AutoSize = true, Location = new Point(140, y) });
                    y += 24;
                }
                _sample.Location = new Point(140, y);
                Controls.Add(_sample);
                y += 28;
                foreach (var box in new[] { _initial, _prefix, _suffix }) box.TextChanged += (_, _) => RenderSample();
                RenderSample();
                break;

            case PromptItemKind.CheckBox:
                _value.Text = item.Value;
                Row("送る値(&V):", _value);
                _initialChecked.Checked = item.InitialChecked;
                _initialChecked.Location = new Point(140, y);
                Controls.Add(_initialChecked);
                y += 32;
                break;

            default:
                _choices.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "初期", Width = 48 });
                _choices.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "表示名", Width = 170 });
                _choices.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "送る値", Width = 180 });
                foreach (var choice in item.Choices)
                {
                    var index = _choices.Rows.Add(choice.Id == item.InitialChoiceId, choice.Label, choice.Value);
                    _choices.Rows[index].Tag = choice.Id;
                }
                // 初期の選択は 1 つだけ
                _choices.CellContentClick += (_, e) =>
                {
                    if (e.ColumnIndex != 0 || e.RowIndex < 0) return;
                    _choices.CommitEdit(DataGridViewDataErrorContexts.Commit);
                    foreach (DataGridViewRow row in _choices.Rows)
                        if (row.Index != e.RowIndex && !row.IsNewRow) row.Cells[0].Value = false;
                };
                Controls.Add(new Label { Text = "選択肢(&C):", AutoSize = true, Location = new Point(14, y) });
                _choices.Location = new Point(14, y + 20);
                Controls.Add(_choices);
                var delete = new Button { Text = "削除(&D)", Bounds = new Rectangle(442, y + 20, 80, 26) };
                var up = new Button { Text = "上へ(&U)", Bounds = new Rectangle(442, y + 52, 80, 26) };
                var down = new Button { Text = "下へ(&N)", Bounds = new Rectangle(442, y + 84, 80, 26) };
                delete.Click += (_, _) => { if (_choices.CurrentRow is { IsNewRow: false } row) _choices.Rows.Remove(row); };
                up.Click += (_, _) => MoveChoice(-1);
                down.Click += (_, _) => MoveChoice(1);
                Controls.AddRange([delete, up, down]);
                y += 190;
                break;
        }

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(336, y + 8, 90, 28) };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(432, y + 8, 90, 28) };
        Controls.AddRange([ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;
        ClientSize = new Size(536, y + 48);

        FormClosing += OnClosing;
        Shown += (_, _) => { _label.Focus(); _label.SelectAll(); };

        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    /// <summary>OK で閉じたときの項目。前回の値（Last…）と番号は元の項目のまま。</summary>
    public PromptItem Result { get; private set; }

    private PromptItem Build() => _source.Kind switch
    {
        PromptItemKind.Text or PromptItemKind.Folder or PromptItemKind.File => _source with
        {
            Label = _label.Text, Initial = _initial.Text, Prefix = _prefix.Text, Suffix = _suffix.Text,
            Required = _source.Kind != PromptItemKind.Folder && _required.Checked,
        },
        PromptItemKind.CheckBox => _source with { Label = _label.Text, Value = _value.Text, InitialChecked = _initialChecked.Checked },
        _ => WithChoices(_source with { Label = _label.Text }),
    };

    /// <summary>番号の付け方と前回の選択の扱いは Domain（PromptChoiceRows.Apply）に任せる。消した選択肢の番号を使い回さないため。</summary>
    private PromptItem WithChoices(PromptItem item)
    {
        _choices.EndEdit();
        var rows = _choices.Rows.Cast<DataGridViewRow>()
            .Where(r => !r.IsNewRow)
            .Select(r => new PromptChoiceRow(r.Tag as int?, r.Cells[1].Value as string ?? "", r.Cells[2].Value as string ?? "", r.Cells[0].Value is true))
            .ToList();
        return PromptChoiceRows.Apply(item, rows);
    }

    private void MoveChoice(int delta)
    {
        if (_choices.CurrentRow is not { IsNewRow: false } row) return;
        var to = row.Index + delta;
        if (to < 0 || to >= _choices.Rows.Count - 1) return;   // 末尾は新しい行の入力欄
        _choices.Rows.Remove(row);
        _choices.Rows.Insert(to, row);
        _choices.CurrentCell = row.Cells[1];
    }

    private void RenderSample()
    {
        var path = _source.Kind is PromptItemKind.Folder or PromptItemKind.File;
        var value = _initial.Text.Length > 0 ? _initial.Text : "値";
        _sample.Text = ArgumentSplitter.Join(_prefix.Text, value, _suffix.Text, path) is { } parts
            ? "送るもの: " + string.Join(" ", parts.Select(p => p.Length == 0 || p.Any(ArgumentSplitter.IsSeparator) ? $"\"{p}\"" : p))
            : "送るもの: （引用符が閉じていません）";
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK) return;
        var item = Build();
        // 定義と同じ検査。この項目だけを入れた定義で調べる（「引数に入っていない」は出ない）
        var problems = PromptDefinitionRules.Validate(new PromptDefinition { Items = [item], Arguments = [PromptArgument.Item(item.Id)] });
        if (problems.Count > 0)
        {
            MessageBox.Show(this, problems[0].Message, "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);   // R-48
            e.Cancel = true;
            return;
        }
        Result = item;
    }
}
