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
    private readonly GroupBox _folderEmpty = new() { Text = "空欄のとき", Size = new Size(382, 82) };
    private readonly RadioButton _emptyCurrent = new() { Text = "現在のフォルダパスを送信(&K)", AutoSize = true, Location = new Point(12, 20) };
    private readonly RadioButton _emptyLiteral = new() { Text = "そのまま送信(&S)", AutoSize = true, Location = new Point(12, 48) };
    private readonly Button _choiceMacro = new() { Text = "マクロ(&M)...", Tag = MacroUsage.ItemValue, Enabled = false };
    private (DataGridViewRow Row, string Text, int Start, int Length)? _choiceSelection;

    private readonly Label _sample = new() { AutoSize = true, UseMnemonic = false };

    /// <summary>
    /// R-133: DataGridView は Application.SetColorMode のダーク対応に乗らず、見出しが白地に黒文字・選んだ行が青地に黒文字になる。
    /// ほかの画面と同じく起動時の OS の色（<see cref="Program.StartupOs"/>）を当てる。ライトは今までの見た目のまま。
    /// ハイコントラストは OS の組に従い、ダークの選択行は文字を白にする（OS の HighlightText は黒で青地では読みにくい。Theme.Resolve と同じ）
    /// </summary>
    private static void ApplyOsColors(DataGridView grid)
    {
        var os = Program.StartupOs;
        if (!os.Dark && !os.HighContrast) return;
        var selectionText = os.HighContrast ? os.HighlightText : Color.White;
        // 見出しは本文の地から少し離す（ハイコントラストは OS の色だけを使い、混ぜない）
        var header = os.HighContrast ? os.Window : Blend(os.Window, os.WindowText, 0.15);
        var line = os.HighContrast ? os.WindowText : Blend(os.Window, os.WindowText, 0.35);
        grid.EnableHeadersVisualStyles = false;
        grid.BackgroundColor = os.Window;
        grid.GridColor = line;
        grid.ColumnHeadersDefaultCellStyle.BackColor = header;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = os.WindowText;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = header;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = os.WindowText;
        grid.DefaultCellStyle.BackColor = os.Window;
        grid.DefaultCellStyle.ForeColor = os.WindowText;
        grid.DefaultCellStyle.SelectionBackColor = os.Highlight;
        grid.DefaultCellStyle.SelectionForeColor = selectionText;
    }

    private static Color Blend(Color from, Color to, double amount) => Color.FromArgb(
        (int)(from.R + (to.R - from.R) * amount), (int)(from.G + (to.G - from.G) * amount), (int)(from.B + (to.B - from.B) * amount));

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
                AddMacroButton(_initial, MacroUsage.InitialValue);
                Row("前に付ける(&B):", _prefix);
                Row("後ろに付ける(&F):", _suffix);
                _required.Checked = item.Required;
                _required.Location = new Point(140, y);
                Controls.Add(_required);
                y += 28;
                if (item.Kind == PromptItemKind.Folder)
                {
                    _emptyCurrent.Checked = item.FolderEmptyBehavior == PromptFolderEmptyBehavior.CurrentFolder;
                    _emptyLiteral.Checked = item.FolderEmptyBehavior == PromptFolderEmptyBehavior.SendEmpty;
                    _folderEmpty.Controls.AddRange([_emptyCurrent, _emptyLiteral]);
                    _folderEmpty.Location = new Point(140, y);
                    _folderEmpty.Enabled = !item.Required;
                    _required.CheckedChanged += (_, _) => _folderEmpty.Enabled = !_required.Checked;
                    _emptyCurrent.CheckedChanged += (_, _) => RenderSample();
                    _emptyLiteral.CheckedChanged += (_, _) => RenderSample();
                    Controls.Add(_folderEmpty);
                    y += 90;
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
                AddMacroButton(_value, MacroUsage.ItemValue);
                _initialChecked.Checked = item.InitialChecked;
                _initialChecked.Location = new Point(140, y);
                Controls.Add(_initialChecked);
                y += 32;
                break;

            default:
                ApplyOsColors(_choices);
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
                var add = new Button { Text = "追加(&A)", Bounds = new Rectangle(442, y + 20, 80, 26) };
                var delete = new Button { Text = "削除(&D)", Bounds = new Rectangle(442, y + 52, 80, 26) };
                var up = new Button { Text = "上へ(&U)", Bounds = new Rectangle(442, y + 84, 80, 26) };
                var down = new Button { Text = "下へ(&N)", Bounds = new Rectangle(442, y + 116, 80, 26) };
                add.Click += (_, _) =>
                {
                    _choices.Focus();
                    _choices.CurrentCell = _choices.Rows[_choices.Rows.Count - 1].Cells[1];   // 新しい行の表示名
                    _choices.BeginEdit(true);
                };
                delete.Click += (_, _) => { if (_choices.CurrentRow is { IsNewRow: false } row) _choices.Rows.Remove(row); };
                up.Click += (_, _) => MoveChoice(-1);
                down.Click += (_, _) => MoveChoice(1);
                Controls.AddRange([add, delete, up, down]);
                _choiceMacro.Bounds = new Rectangle(14, y + 186, 120, 28);
                _choiceMacro.MouseDown += (_, _) => CaptureChoice();
                _choiceMacro.Click += (_, _) => InsertSelectedChoiceMacro();
                _choices.CurrentCellChanged += (_, _) => { _choiceSelection = null; SyncChoiceMacro(); };
                _choices.RowsAdded += (_, _) => SyncChoiceMacro();
                _choices.RowsRemoved += (_, _) => SyncChoiceMacro();
                _choices.EditingControlShowing += (_, e) =>
                {
                    if (e.Control is not TextBox box) return;
                    box.Leave -= CaptureChoiceEvent;
                    box.Leave += CaptureChoiceEvent;
                    box.KeyUp -= CaptureChoiceKey;
                    box.KeyUp += CaptureChoiceKey;
                    box.MouseUp -= CaptureChoiceMouse;
                    box.MouseUp += CaptureChoiceMouse;
                };
                Controls.Add(_choiceMacro);
                SyncChoiceMacro();
                y += 224;
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

    internal PromptItem Build() => _source.Kind switch
    {
        PromptItemKind.Text or PromptItemKind.Folder or PromptItemKind.File => _source with
        {
            Label = _label.Text, Initial = _initial.Text, Prefix = _prefix.Text, Suffix = _suffix.Text,
            Required = _required.Checked,
            FolderEmptyBehavior = _source.Kind == PromptItemKind.Folder && _emptyLiteral.Checked
                ? PromptFolderEmptyBehavior.SendEmpty : PromptFolderEmptyBehavior.CurrentFolder,
        },
        PromptItemKind.CheckBox => _source with { Label = _label.Text, Value = _value.Text, InitialChecked = _initialChecked.Checked, FolderEmptyBehavior = PromptFolderEmptyBehavior.CurrentFolder },
        _ => WithChoices(_source with { Label = _label.Text, FolderEmptyBehavior = PromptFolderEmptyBehavior.CurrentFolder }),
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
        var item = Build();
        _sample.Text = "送るもの: " + PromptExample.Render(new PromptDefinition { Items = [item], Arguments = [PromptArgument.Item(item.Id)] });
    }

    private void AddMacroButton(TextBox box, MacroUsage usage)
    {
        var button = new Button { Text = "マクロ(&M)...", Tag = usage, Bounds = new Rectangle(442, box.Top, 80, 26) };
        button.Click += (_, _) =>
        {
            var start = box.SelectionStart;
            var length = box.SelectionLength;
            using var dialog = new MacroReferenceDialog(usage);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is not { } entry) return;
            var insertion = MacroTextInsertion.Apply(box.Text, start, length, entry);
            box.Text = insertion.Text;
            box.Focus();
            box.Select(insertion.Caret, 0);
        };
        Controls.Add(button);
    }

    private void SyncChoiceMacro() => _choiceMacro.Enabled = _choices.CurrentRow is { IsNewRow: false };
    private void CaptureChoiceEvent(object? sender, EventArgs e) => CaptureChoice();
    private void CaptureChoiceKey(object? sender, KeyEventArgs e) => CaptureChoice();
    private void CaptureChoiceMouse(object? sender, MouseEventArgs e) => CaptureChoice();

    private void CaptureChoice()
    {
        if (_choices.CurrentRow is not { IsNewRow: false } row) { _choiceSelection = null; return; }
        if (_choices.CurrentCell?.ColumnIndex == 2 && _choices.IsCurrentCellInEditMode && _choices.EditingControl is TextBox box)
            _choiceSelection = (row, box.Text, box.SelectionStart, box.SelectionLength);
        else if (_choiceSelection is not { } saved || !ReferenceEquals(saved.Row, row))
        {
            var text = row.Cells[2].Value as string ?? "";
            _choiceSelection = (row, text, text.Length, 0);
        }
    }

    private void InsertSelectedChoiceMacro()
    {
        CaptureChoice();
        if (_choiceSelection is not { } selection) return;
        // R-136: ボタンへ移った後でも編集中の文字と範囲を保持する。行は参照で照合する。
        _choices.EndEdit();
        using var dialog = new MacroReferenceDialog(MacroUsage.ItemValue);
        try
        {
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is not { } entry) return;
            var index = _choices.Rows.IndexOf(selection.Row);
            if (index < 0 || selection.Row.IsNewRow) return;
            var insertion = MacroTextInsertion.Apply(selection.Text, selection.Start, selection.Length, entry);
            if (!InsertChoiceMacro(index, selection.Start, selection.Length, entry)) return;
            _choices.CurrentCell = selection.Row.Cells[2];
            _choices.BeginEdit(false);
            if (_choices.EditingControl is TextBox box)
            {
                box.Focus();
                box.Select(insertion.Caret, 0);
            }
        }
        finally { _choiceSelection = null; }
    }

    internal bool InsertChoiceMacro(int rowIndex, int start, int length, MacroCatalogEntry entry)
    {
        if (rowIndex < 0 || rowIndex >= _choices.Rows.Count || _choices.Rows[rowIndex].IsNewRow || entry.Insert is null) return false;
        var cell = _choices.Rows[rowIndex].Cells[2];
        cell.Value = MacroTextInsertion.Apply(cell.Value as string ?? "", start, length, entry).Text;
        return true;
    }

    internal (Control Control, int? Row, int? Column) ProblemTarget(PromptProblem problem)
    {
        if (_source.Kind == PromptItemKind.DropDown && problem.Field is PromptField.Value or PromptField.Choices or PromptField.InitialChoice)
        {
            var item = Build();
            var row = problem.ChoiceId is { } id ? item.Choices.FindIndex(c => c.Id == id) : 0;
            return (_choices, row >= 0 ? row : 0, problem.Field == PromptField.Value ? 2 : problem.Field == PromptField.InitialChoice ? 0 : 1);
        }
        return (problem.Field switch
        {
            PromptField.Initial => _initial,
            PromptField.Prefix => _prefix,
            PromptField.Suffix => _suffix,
            PromptField.Value => _value,
            PromptField.FolderEmptyBehavior => _emptyCurrent,
            _ => _label,
        }, null, null);
    }

    private void FocusProblem(PromptProblem problem)
    {
        var target = ProblemTarget(problem);
        if (target.Control == _choices && target.Row is { } row && row < _choices.Rows.Count && target.Column is { } column)
            _choices.CurrentCell = _choices.Rows[row].Cells[column];
        target.Control.Focus();
        if (target.Control is TextBox box) box.SelectAll();
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
            FocusProblem(problems[0]);
            return;
        }
        Result = item;
    }
}
