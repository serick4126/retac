using System.Drawing;
using System.Windows.Forms;
using ReTAC.Domain.Navigation;

namespace ReTAC.App;

/// <summary>
/// 名前を 1 つ入力させるダイアログ。名前の変更（`N`）・フォルダ作成（`K`）・
/// リネームコピー（R-63）で使う。
/// R-46 / R-46-2: プリセットは<b>拡張子を含めた名前全体</b>を全選択で渡す。
/// R-48: 誤りは操作を中止せず入力欄に戻す（差し戻しは <c>validate</c> を渡して行う）。
/// </summary>
public sealed class TextInputDialog : Form
{
    private readonly TextBox _input = new();
    /// <summary>
    /// OK で閉じると決まった時点の値。<see cref="OwnerModal"/> のモードレス表示では、
    /// Close() の直後（FormClosed）でフォームが破棄され、破棄中の WM_DESTROY は TextBox の
    /// テキストを保持しない。呼び出し側は await の後で Value を読むので、それより前に確保しておく
    /// （ShowDialog の呼び出し元は今までどおり最新の入力を読めるよう、未確保なら実値を返す）。
    /// </summary>
    private string? _capturedValue;

    /// <param name="validate">確定時の検査。エラー文言を返すと入力欄に戻す。OK なら null</param>
    public TextInputDialog(string caption, string message, string preset,
                           Func<string, string?>? validate = null)
    {
        Text = caption;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;   // ダイアログはタスクバーに出さない（既定は true）
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(460, 150);

        Controls.Add(new Label { Text = message, AutoSize = true, Location = new Point(14, 14), MaximumSize = new Size(430, 0) });
        _input.SetBounds(14, 54, 430, 23);
        _input.Text = preset;
        Controls.Add(_input);

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(150, 100, 90, 28) };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(354, 100, 90, 28) };
        Controls.AddRange([ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;

        // 「以降全て」は置いていない。R-50 が要るのは属性変更（AttributeDialog）と
        // 同名衝突（ConflictDialog）で、そちらは各自が持っている。
        // ここにも枠だけ用意していたが、true で渡す呼び出し元が 1 つも無く
        // 一度も動いていなかったので外した（V-10）

        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK) return;
            if (validate?.Invoke(Value) is { } error)
            {
                // R-48: 中止せず、入力しなおせるように戻す
                MessageBox.Show(this, error, "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
                _input.Focus();
                _input.SelectAll();
                return;
            }

            // 検査を通った（または検査なし）ので閉じてよい。破棄で消える前に確保する
            _capturedValue = Value;
        };

        Shown += (_, _) => { _input.Focus(); _input.SelectAll(); };   // R-46

        // C-1: AutoScaleMode の代入はその場で PerformAutoScale を走らせる。ClientSize と
        // Controls が揃ってからでないと、まだ 96 DPI のレイアウトを拡大できない
        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16: 座標と大きさは 96 DPI（100%）で書いてある
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    public string Value => _capturedValue ?? InputText.TrimEdge(_input.Text);
}
