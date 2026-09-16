using System.Drawing;
using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>
/// 択一の問い合わせ。R-62 の「フォルダを新しく作成する／別の名前に変えて複写する」のように、
/// エラーで止めずに対処を選ばせる場面で使う。先頭の選択肢が既定。
/// </summary>
public sealed class ChoiceDialog : Form
{
    private readonly RadioButton[] _choices;

    public ChoiceDialog(string caption, string message, IReadOnlyList<string> choices)
    {
        Text = caption;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;   // ダイアログはタスクバーに出さない（既定は true）
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;

        Controls.Add(new Label
        {
            Text = message,
            AutoSize = true,
            Location = new Point(16, 14),
            MaximumSize = new Size(450, 0),
        });

        var y = 60;
        _choices = [.. choices.Select(text =>
        {
            var button = new RadioButton { Text = text, AutoSize = true, Location = new Point(24, y) };
            Controls.Add(button);
            y += 28;
            return button;
        })];
        _choices[0].Checked = true;

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(180, y + 12, 90, 28) };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(280, y + 12, 90, 28) };
        Controls.AddRange([ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;
        ClientSize = new Size(482, y + 52);

        // C-1: AutoScaleMode の代入はその場で PerformAutoScale を走らせる。ClientSize と
        // Controls が揃ってからでないと、まだ 96 DPI のレイアウトを拡大できない
        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16: 座標と大きさは 96 DPI（100%）で書いてある
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    public int SelectedIndex => Array.FindIndex(_choices, c => c.Checked);
}
