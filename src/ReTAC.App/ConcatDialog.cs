using System.Drawing;
using System.Windows.Forms;
using ReTAC.Domain.Entries;
using ReTAC.Domain.Navigation;

namespace ReTAC.App;

/// <summary>
/// ファイルの連結（0x82E4）。
/// R-35: <b>連結順を並べ替えられること</b>。順序の指定ができなければ機能として成立しない。
/// R-35-2: 「EOF をカットする」（既定オン）と「末尾に改行コードをつける」の 2 つ。
/// </summary>
public sealed class ConcatDialog : Form
{
    private readonly List<Entry> _sources;
    private readonly ListBox _order = new() { Bounds = new Rectangle(14, 34, 340, 160) };
    private readonly ComboBox _destination = new() { Bounds = new Rectangle(14, 232, 340, 23) };
    private readonly CheckBox _cutEof = new()
    {
        Text = "EOF（ファイルの末尾記号）をカットする(&E)",
        AutoSize = true,
        Checked = true,                      // R-35-2: 既定オン
        Location = new Point(14, 268),
    };
    private readonly CheckBox _appendNewLine = new()
    {
        Text = "ファイルの末尾に改行コードをつける(&N)",
        AutoSize = true,
        Location = new Point(14, 294),
    };

    public ConcatDialog(IReadOnlyList<Entry> sources, FolderHistory history, string currentFolder)
    {
        _sources = [.. sources];

        Text = "ファイルの連結";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;   // ダイアログはタスクバーに出さない（既定は true）
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(470, 380);

        Controls.Add(new Label { Text = "連結する順（↑↓で並べ替え）:", AutoSize = true, Location = new Point(14, 14) });
        Controls.Add(new Label { Text = "連結先のファイル(&D):", AutoSize = true, Location = new Point(14, 212) });

        var up = new Button { Text = "↑(&U)", Bounds = new Rectangle(364, 34, 90, 28) };
        var down = new Button { Text = "↓(&W)", Bounds = new Rectangle(364, 68, 90, 28) };
        up.Click += (_, _) => MoveSelected(-1);
        down.Click += (_, _) => MoveSelected(1);

        _destination.Items.AddRange([.. history.Recent]);   // R-35: 入力欄は履歴を持つ
        _destination.Text = System.IO.Path.Combine(currentFolder, "concat.txt");

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(264, 332, 90, 28) };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(364, 332, 90, 28) };
        Controls.AddRange([_order, up, down, _destination, _cutEof, _appendNewLine, ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;

        Reload(0);

        // C-1: AutoScaleMode の代入はその場で PerformAutoScale を走らせる。ClientSize と
        // Controls が揃ってからでないと、まだ 96 DPI のレイアウトを拡大できない
        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16: 座標と大きさは 96 DPI（100%）で書いてある
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    /// <summary>並べ替えた結果の順序。</summary>
    public IReadOnlyList<Entry> Sources => _sources;

    public string Destination => InputText.TrimEdge(_destination.Text);
    public bool CutEof => _cutEof.Checked;
    public bool AppendNewLine => _appendNewLine.Checked;

    private void MoveSelected(int delta)
    {
        var index = _order.SelectedIndex;
        var to = index + delta;
        if (index < 0 || to < 0 || to >= _sources.Count) return;

        (_sources[index], _sources[to]) = (_sources[to], _sources[index]);
        Reload(to);
    }

    private void Reload(int select)
    {
        _order.BeginUpdate();
        _order.Items.Clear();
        foreach (var entry in _sources) _order.Items.Add(entry.Name);
        _order.EndUpdate();
        if (_order.Items.Count > 0) _order.SelectedIndex = Math.Clamp(select, 0, _order.Items.Count - 1);
    }
}
