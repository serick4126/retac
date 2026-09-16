using System.Drawing;
using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>
/// ショートカットファイルの作成（`O` / 0x82F1）。
/// R-58: 3 つのオプションをすべて実装する。いずれも都度切り替えて使用されている。
/// </summary>
public sealed class ShortcutDialog : Form
{
    private readonly CheckBox _onDesktop = new() { Text = "デスクトップに作成する(&D)", AutoSize = true, Checked = true };
    private readonly CheckBox _withSuffix = new() { Text = "ショートカット名に「へのショートカット」を付ける(&S)", AutoSize = true };
    private readonly CheckBox _withExtension = new() { Text = "ショートカット名にリンク元の拡張子を付ける(&E)", AutoSize = true };

    public ShortcutDialog()
    {
        Text = "ショートカットファイルの作成";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;   // ダイアログはタスクバーに出さない（既定は true）
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(400, 200);

        Controls.Add(new Label
        {
            Text = "ファイルまたはフォルダのショートカットを作成します。" + Environment.NewLine
                 + "複数選択している場合は連続して作成します。",
            AutoSize = true,
            Location = new Point(16, 14),
        });

        _onDesktop.Location = new Point(24, 64);
        _withSuffix.Location = new Point(24, 90);
        _withExtension.Location = new Point(24, 116);
        Controls.AddRange([_onDesktop, _withSuffix, _withExtension]);

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(190, 156, 90, 28) };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(290, 156, 90, 28) };
        Controls.AddRange([ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;

        // C-1: AutoScaleMode の代入はその場で PerformAutoScale を走らせる。ClientSize と
        // Controls が揃ってからでないと、まだ 96 DPI のレイアウトを拡大できない
        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16: 座標と大きさは 96 DPI（100%）で書いてある
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    public bool OnDesktop => _onDesktop.Checked;
    public bool WithSuffix => _withSuffix.Checked;
    public bool WithExtension => _withExtension.Checked;

    /// <summary>R-58 の 3 オプションから、作るショートカットの名前を決める。</summary>
    public string NameFor(string sourceName)
    {
        var baseName = WithExtension ? sourceName : System.IO.Path.GetFileNameWithoutExtension(sourceName);
        if (baseName.Length == 0) baseName = sourceName;
        return (WithSuffix ? baseName + " へのショートカット" : baseName) + ".lnk";
    }
}
