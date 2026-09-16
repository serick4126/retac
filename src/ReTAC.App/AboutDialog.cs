using System.Drawing;
using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>
/// 「バージョン情報」（0xE140）。アイコンと版だけを出す。
/// <c>Application.ProductVersion</c> にはコミットハッシュが付くが利用者には読めないので外す
/// （必要なときは exe のプロパティに残っている）。
/// </summary>
public sealed class AboutDialog : Form
{
    public AboutDialog()
    {
        Text = "バージョン情報";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;   // ダイアログはタスクバーに出さない（既定は true）
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(320, 140);

        // H-14 と同じ埋め込みリソース。.ico は複数の大きさを持つので、
        // ここでは大きい方を取り出す（Icon(stream, size) が最も近いものを選ぶ）
        // B-16: 置き場所の大きさは論理値で書き、WinForms に拡大させる。
        // 取り出すビットマップだけは実ピクセルが要るので DPI を掛ける
        const int side = 64;
        var pixels = side * DeviceDpi / 96;
        var picture = new PictureBox { Bounds = new Rectangle(20, 20, side, side), SizeMode = PictureBoxSizeMode.Zoom };
        using (var s = typeof(AboutDialog).Assembly.GetManifestResourceStream("ReTAC.App.retac.ico"))
        {
            if (s is not null)
            {
                using var icon = new Icon(s, new Size(pixels, pixels));
                picture.Image = icon.ToBitmap();
            }
        }

        var left = 20 + side + 20;
        var name = new Label
        {
            Text = "ReTAC",
            AutoSize = true,
            Location = new Point(left, 24),
            Font = new Font(Font.FontFamily, Font.Size * 1.6f, FontStyle.Bold),
        };
        // I-1: PreferredHeight は現在の DPI（実ピクセル）で測る。後で自動拡大がもう一度掛かるので、
        // ここに積む座標は論理値（96 DPI）に戻しておく
        var version = new Label
        {
            // `1.1.0+<ハッシュ>` の右側は出さない
            Text = "Ver " + Application.ProductVersion.Split('+')[0],
            AutoSize = true,
            Location = new Point(left, 24 + name.PreferredHeight * 96 / DeviceDpi + 8),
        };

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(215, 100, 90, 28) };
        Controls.AddRange([picture, name, version, ok]);
        AcceptButton = ok;
        CancelButton = ok;

        FormClosed += (_, _) => picture.Image?.Dispose();

        // C-1: AutoScaleMode の代入はその場で PerformAutoScale を走らせる。ClientSize と
        // Controls が揃ってからでないと、まだ 96 DPI のレイアウトを拡大できない
        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16: 座標と大きさは 96 DPI（100%）で書いてある
        AutoScaleMode = AutoScaleMode.Dpi;   // R-66: DPI に追従させる
    }
}
