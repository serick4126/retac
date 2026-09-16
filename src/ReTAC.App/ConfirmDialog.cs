using System.Drawing;
using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>外部ツールの実行前の確認（F-03）。<see cref="OwnerModal"/> で出すための、OK / キャンセルだけのダイアログ。</summary>
public sealed class ConfirmDialog : Form
{
    public ConfirmDialog(string message)
    {
        Text = "ReTAC";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(520, 200);

        var text = new TextBox
        {
            Text = message,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Bounds = new Rectangle(14, 14, 492, 132),
        };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(316, 158, 90, 28) };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(416, 158, 90, 28) };
        Controls.AddRange([text, ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;
        Shown += (_, _) => ok.Focus();

        // C-1: ClientSize と Controls が揃ってから
        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16
        AutoScaleMode = AutoScaleMode.Dpi;
    }
}
