using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>
/// 表示するドライブの設定（0x814C）。16.7 節のドライブごとのチェックリスト。
/// 現行はすべて表示（ローカル・ネットワーク・BD-ROM・Google Drive）。
/// </summary>
public sealed class DriveVisibilityDialog : Form
{
    private readonly List<(CheckBox Box, char Letter)> _drives = [];
    private readonly CheckBox _desktop = new() { Text = "デスクトップのアイコンを表示する(&D)", AutoSize = true };

    public DriveVisibilityDialog(IReadOnlySet<char> hidden, bool showDesktop)
    {
        Text = "表示するドライブの設定";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;   // ダイアログはタスクバーに出さない（既定は true）
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(360, 380);

        Controls.Add(new Label { Text = "ドライブバーに表示するドライブ:", AutoSize = true, Location = new Point(16, 12) });

        var y = 40;
        var x = 24;
        // N-05: IsReady や容量には触れない。応答しないドライブで固まらせないため
        foreach (var drive in DriveInfo.GetDrives())
        {
            var letter = drive.Name[0];
            var box = new CheckBox
            {
                Text = $"{letter}:",
                AutoSize = true,
                Checked = !hidden.Contains(char.ToUpperInvariant(letter)),
                Location = new Point(x, y),
            };
            Controls.Add(box);
            _drives.Add((box, letter));

            y += 26;
            if (y <= 274) continue;   // デスクトップの行に重ならないところで折り返す
            y = 40;
            x += 100;
        }

        _desktop.Checked = showDesktop;
        _desktop.Location = new Point(16, 312);
        Controls.Add(_desktop);

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(160, 342, 90, 28) };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(260, 342, 90, 28) };
        Controls.AddRange([ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;

        // C-1: AutoScaleMode の代入はその場で PerformAutoScale を走らせる。ClientSize と
        // Controls が揃ってからでないと、まだ 96 DPI のレイアウトを拡大できない
        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16: 座標と大きさは 96 DPI（100%）で書いてある
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    /// <summary>チェックを外されたドライブ（＝表示しない）。</summary>
    public HashSet<char> Hidden =>
        [.. _drives.Where(d => !d.Box.Checked).Select(d => char.ToUpperInvariant(d.Letter))];

    public bool ShowDesktop => _desktop.Checked;
}
