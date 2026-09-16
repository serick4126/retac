using System.Drawing;
using System.Windows.Forms;
using ReTAC.Domain.Listing;

namespace ReTAC.App;

/// <summary>表示するファイルタイプの設定（0x82FF）。16.4 節の 7 項目。現行はすべて ON。</summary>
public sealed class FileTypeDialog : Form
{
    private readonly (CheckBox Box, Action<bool> Set)[] _items;

    public FileTypeDialog(FileTypeFilter filter)
    {
        Text = "表示するファイルタイプの設定";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;   // ダイアログはタスクバーに出さない（既定は true）
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(340, 280);

        Controls.Add(new Label { Text = "表示するファイルの種類を選んでください。", AutoSize = true, Location = new Point(16, 14) });

        var y = 44;
        (CheckBox Box, Action<bool> Set) Item(string text, bool value, Action<bool> set)
        {
            var box = new CheckBox { Text = text, AutoSize = true, Checked = value, Location = new Point(20, y) };
            Controls.Add(box);
            y += 26;
            return (box, set);
        }

        _items =
        [
            Item("フォルダ(&D)", filter.Folders, v => filter.Folders = v),
            Item("プログラム(&P)", filter.Programs, v => filter.Programs = v),
            Item("関連付けファイル(&A)", filter.Associated, v => filter.Associated = v),
            Item("書庫ファイル(&R)", filter.Archives, v => filter.Archives = v),
            Item("その他のファイル(&O)", filter.Others, v => filter.Others = v),
            Item("システムファイル(&S)", filter.SystemFiles, v => filter.SystemFiles = v),
            Item("隠しファイル(&H)", filter.HiddenFiles, v => filter.HiddenFiles = v),
        ];

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(120, 236, 90, 28) };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(220, 236, 90, 28) };
        Controls.AddRange([ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;

        FormClosing += (_, _) =>
        {
            if (DialogResult != DialogResult.OK) return;
            foreach (var (box, set) in _items) set(box.Checked);
        };

        // C-1: AutoScaleMode の代入はその場で PerformAutoScale を走らせる。ClientSize と
        // Controls が揃ってからでないと、まだ 96 DPI のレイアウトを拡大できない
        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16: 座標と大きさは 96 DPI（100%）で書いてある
        AutoScaleMode = AutoScaleMode.Dpi;
    }
}
