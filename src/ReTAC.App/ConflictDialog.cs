using System.Drawing;
using System.Windows.Forms;
using ReTAC.Domain.FileOps;
using ReTAC.Domain.Formatting;

namespace ReTAC.App;

/// <summary>
/// 同名ファイルの処理（16.8 節）。元と先の日時・サイズを並べ、複写条件を選ばせる（R-41-5）。
/// スコープ内は 4 択（新しい時に複写＝既定 / 上書き / 名前を変更し複写 / 複写しない）。
/// R-50: 「以降全て」は残りの衝突すべてに同じ判断を適用する。
/// </summary>
public sealed class ConflictDialog : Form
{
    private readonly (RadioButton Button, CopyCondition Condition)[] _choices;

    public ConflictDialog(CopyPlanner.Conflict conflict, CopyCondition current)
    {
        Text = "同名ファイルの処理";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;   // ダイアログはタスクバーに出さない（既定は true）
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(520, 290);

        Controls.Add(new Label
        {
            Text = $"{System.IO.Path.GetFileName(conflict.Source)} は複写先に既に存在します。",
            AutoSize = true,
            Location = new Point(16, 14),
            MaximumSize = new Size(490, 0),
        });

        Controls.Add(Detail("複写元", conflict.Source, conflict.SourceTime, conflict.SourceSize, 44));
        Controls.Add(Detail("複写先", conflict.Destination, conflict.DestinationTime, conflict.DestinationSize, 88));

        var panel = new Panel { Bounds = new Rectangle(0, 138, ClientSize.Width, 108) };
        Controls.Add(panel);
        _choices =
        [
            (Radio(panel, "新しい時に複写(&N)", 4), CopyCondition.NewerOnly),
            (Radio(panel, "上書きで複写(&O)", 30), CopyCondition.Overwrite),
            (Radio(panel, "名前を変更し複写(&R)", 56), CopyCondition.RenameCopy),
            (Radio(panel, "複写しない(&S)", 82), CopyCondition.Skip),
        ];
        foreach (var (button, condition) in _choices) button.Checked = condition == current;

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(200, 250, 90, 28) };
        var all = new Button { Text = "以降全て(&A)", Bounds = new Rectangle(300, 250, 100, 28) };
        var cancel = new Button { Text = "中止", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(410, 250, 90, 28) };
        all.Click += (_, _) => { ApplyToAll = true; DialogResult = DialogResult.OK; Close(); };
        Controls.AddRange([ok, all, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;

        // C-1: AutoScaleMode の代入はその場で PerformAutoScale を走らせる。ClientSize と
        // Controls が揃ってからでないと、まだ 96 DPI のレイアウトを拡大できない
        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16: 座標と大きさは 96 DPI（100%）で書いてある
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    public CopyCondition Condition => _choices.First(c => c.Button.Checked).Condition;

    /// <summary>R-50: 残りの衝突すべてに同じ判断を適用する。</summary>
    public bool ApplyToAll { get; private set; }

    private static Label Detail(string caption, string path, DateTime time, long size, int y) => new()
    {
        Text = $"{caption}: {Display.Size(size)}  {Display.Timestamp(time)}{Environment.NewLine}{path}",
        AutoSize = true,
        Location = new Point(24, y),
        MaximumSize = new Size(480, 0),
    };

    private static RadioButton Radio(Control parent, string text, int y)
    {
        var button = new RadioButton { Text = text, AutoSize = true, Location = new Point(24, y) };
        parent.Controls.Add(button);
        return button;
    }
}
