using System.Drawing;
using System.Windows.Forms;
using ReTAC.Domain.Listing;
using SortOrder = ReTAC.Domain.Listing.SortOrder;

namespace ReTAC.App;

/// <summary>
/// ソートの設定（`S` / 0x8300）。16.5 節のダイアログに合わせる。
/// 並べ方は R-05-2-2 の 4 択（昇順 / 降順 / 自然な昇順 / 自然な降順）。卓駆の 2 択から拡張した点。
/// </summary>
public sealed class SortDialog : Form
{
    private readonly (RadioButton Button, SortKey Key)[] _keys;
    private readonly (RadioButton Button, SortDirection Direction, ComparisonMode Mode)[] _methods;

    public SortDialog(SortOrder current)
    {
        Text = "ソートの設定";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;   // ダイアログはタスクバーに出さない（既定は true）
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(420, 264);

        Controls.Add(new Label
        {
            Text = "ファイルリストのソート方法を選択してください。",
            AutoSize = true,
            Location = new Point(16, 14),
        });

        // 項目と並べ方は別々のグループにする（同じ親に置くと排他が混ざる）
        var keyPanel = new Panel { Bounds = new Rectangle(0, 40, ClientSize.Width, 84) };
        var methodPanel = new Panel { Bounds = new Rectangle(0, 132, ClientSize.Width, 62) };
        Controls.AddRange([keyPanel, methodPanel]);

        _keys =
        [
            (Radio(keyPanel, "名前でソート(&N)", 16, 4), SortKey.Name),
            (Radio(keyPanel, "拡張子でソート(&X)", 216, 4), SortKey.Extension),
            (Radio(keyPanel, "時刻でソート(&T)", 16, 30), SortKey.Date),
            (Radio(keyPanel, "ファイルサイズでソート(&S)", 216, 30), SortKey.Size),
            (Radio(keyPanel, "ソートはしない(&U)", 16, 56), SortKey.None),
        ];

        _methods =
        [
            (Radio(methodPanel, "昇順で並べる(&P)", 16, 4), SortDirection.Ascending, ComparisonMode.Strict),
            (Radio(methodPanel, "降順で並べる(&D)", 216, 4), SortDirection.Descending, ComparisonMode.Strict),
            (Radio(methodPanel, "自然な昇順で並べる(&A)", 16, 30), SortDirection.Ascending, ComparisonMode.Natural),
            (Radio(methodPanel, "自然な降順で並べる(&E)", 216, 30), SortDirection.Descending, ComparisonMode.Natural),
        ];

        foreach (var (button, key) in _keys) button.Checked = key == current.Key;
        foreach (var (button, direction, mode) in _methods)
            button.Checked = direction == current.Direction && mode == current.Mode;

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(120, 216, 90, 28) };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(220, 216, 90, 28) };
        Controls.AddRange([ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;

        // C-1: AutoScaleMode の代入はその場で PerformAutoScale を走らせる。ClientSize と
        // Controls が揃ってからでないと、まだ 96 DPI のレイアウトを拡大できない
        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16: 座標と大きさは 96 DPI（100%）で書いてある
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    public SortOrder Result
    {
        get
        {
            var key = _keys.First(k => k.Button.Checked).Key;
            var (_, direction, mode) = _methods.First(m => m.Button.Checked);
            return new SortOrder(key, direction, mode);
        }
    }

    private static RadioButton Radio(Control parent, string text, int x, int y)
    {
        var button = new RadioButton { Text = text, AutoSize = true, Location = new Point(x, y) };
        parent.Controls.Add(button);
        return button;
    }
}
