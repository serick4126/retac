using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>
/// 表示するドライブの設定（0x814C）。統合設定画面（R-102）のページ。16.7 節のドライブごとのチェックリスト。
/// 現行はすべて表示（ローカル・ネットワーク・BD-ROM・Google Drive）。チェックの変更はその場で下書きへ反映する。
/// </summary>
public sealed class DriveVisibilityPage : UserControl
{
    private readonly SettingsDraft _draft;
    private readonly List<(CheckBox Box, char Letter)> _drives = [];
    private readonly CheckBox _desktop = new() { Text = "デスクトップのアイコンを表示する(&D)", AutoSize = true };

    public DriveVisibilityPage(SettingsDraft draft)
    {
        _draft = draft;

        AutoScaleMode = AutoScaleMode.Inherit;   // R-102-3: 拡大は SettingsDialog だけが行う
        Size = new Size(360, 342);   // 旧ダイアログのクライアント領域から OK/キャンセルの行を除いた大きさ

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
                Checked = !draft.HiddenDrives.Contains(char.ToUpperInvariant(letter)),
                Location = new Point(x, y),
            };
            box.CheckedChanged += (_, _) => _draft.HiddenDrives = [.. _drives.Where(d => !d.Box.Checked).Select(d => char.ToUpperInvariant(d.Letter))];
            Controls.Add(box);
            _drives.Add((box, letter));

            y += 26;
            if (y <= 274) continue;   // デスクトップの行に重ならないところで折り返す
            y = 40;
            x += 100;
        }

        _desktop.Checked = draft.ShowDesktopButton;
        _desktop.Location = new Point(16, 312);
        _desktop.CheckedChanged += (_, _) => _draft.ShowDesktopButton = _desktop.Checked;
        Controls.Add(_desktop);
    }
}
