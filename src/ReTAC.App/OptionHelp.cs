using System.Drawing;
using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>
/// 設定の選択肢の右に「?」を置き、ON と OFF で何が起きるかをツールチップで出す（F-04）。
/// 名前だけでは動きが読めない項目にだけ付ける。
/// </summary>
public static class OptionHelp
{
    /// <summary>通りすがりで出さない程度に待つ。長い文でも読み切れる間は出したままにする</summary>
    private const int Delay = 400;
    private const int Show = 30000;

    public static void Attach(Form form, ToolTip tips, params (Control Target, string Text)[] items)
    {
        tips.InitialDelay = Delay;
        tips.ReshowDelay = Delay / 4;
        tips.AutoPopDelay = Show;

        foreach (var (target, text) in items)
        {
            tips.SetToolTip(target, text);

            // AutoSize のチェックボックスは Controls に入った時点で幅が決まる。その右に並べる。
            // 「?」の 1 文字は本文に埋もれるので、丸を塗って白抜きにする（Windows の説明ボタンに寄せる）
            var mark = new Label { Size = new Size(16, 16), Location = new Point(target.Right + 6, target.Top + 1) };
            mark.Paint += (sender, e) =>
            {
                var label = (Label)sender!;
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var circle = new Rectangle(0, 0, label.Width - 1, label.Height - 1);
                using var fill = new SolidBrush(SystemColors.HotTrack);
                e.Graphics.FillEllipse(fill, circle);
                using var font = new Font(label.Font.FontFamily, label.Height * 0.62F, FontStyle.Bold, GraphicsUnit.Pixel);
                using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                e.Graphics.DrawString("?", font, Brushes.White, circle, format);
            };
            tips.SetToolTip(mark, text);
            form.Controls.Add(mark);
        }
    }
}
