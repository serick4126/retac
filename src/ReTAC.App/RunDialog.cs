using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>
/// 名前を指定し実行（`X` / 0x82E9）。16.10 節。
/// R-46-4: カーソル位置のエントリ名をプリセットして全選択する。
/// R-54 / R-54-2: ウィンドウ状態 3 択。作業ディレクトリはカレントフォルダ。
/// </summary>
public sealed class RunDialog : Form
{
    private readonly ComboBox _command = new() { DropDownStyle = ComboBoxStyle.DropDown };
    private readonly (RadioButton Button, ProcessWindowStyle Style)[] _styles;

    public RunDialog(string preset, IReadOnlyList<string> history)
    {
        Text = "名前を指定して実行";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;   // ダイアログはタスクバーに出さない（既定は true）
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(430, 150);

        Controls.Add(new Label { Text = "コマンドライン(&C):", AutoSize = true, Location = new Point(14, 14) });
        _command.SetBounds(14, 34, 400, 23);
        _command.Items.AddRange([.. history]);
        _command.Text = preset;
        Controls.Add(_command);

        _styles =
        [
            (Radio("通常表示(&N)", 60, 68), ProcessWindowStyle.Normal),
            (Radio("最小表示(&I)", 180, 68), ProcessWindowStyle.Minimized),
            (Radio("最大表示(&M)", 300, 68), ProcessWindowStyle.Maximized),
        ];
        _styles[0].Button.Checked = true;

        var run = new Button { Text = "実行(&R)", DialogResult = DialogResult.OK, Bounds = new Rectangle(120, 106, 90, 28) };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(216, 106, 90, 28) };
        var browse = new Button { Text = "参照(&B)...", Bounds = new Rectangle(312, 106, 90, 28) };
        browse.Click += (_, _) => Browse();
        Controls.AddRange([run, cancel, browse]);
        AcceptButton = run;
        CancelButton = cancel;

        Shown += (_, _) => { _command.Focus(); _command.SelectAll(); };   // R-46

        // C-1: AutoScaleMode の代入はその場で PerformAutoScale を走らせる。ClientSize と
        // Controls が揃ってからでないと、まだ 96 DPI のレイアウトを拡大できない
        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16: 座標と大きさは 96 DPI（100%）で書いてある
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    public string CommandLine => _command.Text.Trim();

    public ProcessWindowStyle WindowStyle => _styles.First(s => s.Button.Checked).Style;

    private RadioButton Radio(string text, int x, int y)
    {
        var button = new RadioButton { Text = text, AutoSize = true, Location = new Point(x, y) };
        Controls.Add(button);
        return button;
    }

    private void Browse()
    {
        using var dialog = new OpenFileDialog { Filter = "すべてのファイル (*.*)|*.*", FileName = CommandLine };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _command.Text = dialog.FileName;
        _command.SelectAll();
    }
}
