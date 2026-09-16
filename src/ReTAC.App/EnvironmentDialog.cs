using System.Drawing;
using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>
/// 動作環境の設定（0x8318）。14.1 節のうち、現に挙動を変える項目だけを置く。
/// 配色・フォント / キー割り当て / 表示ドライブ の各画面は T7-7 で足す。
/// </summary>
public sealed class EnvironmentDialog : Form
{
    private readonly AppSettings _settings;

    private readonly CheckBox _resident = new() { Text = "常駐する（終了操作で最小化するだけにする)(&J)", AutoSize = true, Location = new Point(20, 14) };
    private readonly CheckBox _startMinimized = new() { Text = "起動時はウィンドウを表示しない(&M)", AutoSize = true, Location = new Point(40, 42) };
    private readonly CheckBox _keepLastFolder = new() { Text = "終了時のフォルダを保持する(&K)", AutoSize = true, Location = new Point(20, 84) };
    private readonly CheckBox _suppressMultiple = new()
    {
        Text = "複数選択の時外部ツールの連続起動はしない(&V)",
        AutoSize = true,
        Location = new Point(20, 112),
    };
    private readonly ToolTip _tips = new();

    public EnvironmentDialog(AppSettings settings)
    {
        _settings = settings;

        Text = "動作環境の設定";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;   // ダイアログはタスクバーに出さない（既定は true）
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(440, 184);

        _resident.Checked = settings.Resident;
        _startMinimized.Checked = settings.StartMinimized;
        _keepLastFolder.Checked = settings.KeepLastFolder;
        _suppressMultiple.Checked = settings.SuppressMultipleToolLaunch;
        Controls.AddRange([_resident, _startMinimized, _keepLastFolder, _suppressMultiple]);
        OptionHelp.Attach(this, _tips,
            (_suppressMultiple, "ON: 複数マークしていても、カーソル位置の 1 件だけで 1 回起動する。外部ツールの設定の「マークした項目ごとに起動する」は灰色になる。\nOFF: 各ツールの設定に従う。"));

        _resident.CheckedChanged += (_, _) => _startMinimized.Enabled = _resident.Checked;
        _startMinimized.Enabled = _resident.Checked;

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(230, 144, 90, 28) };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(330, 144, 90, 28) };
        Controls.AddRange([ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;

        FormClosing += (_, _) =>
        {
            if (DialogResult != DialogResult.OK) return;
            _settings.Resident = _resident.Checked;
            _settings.StartMinimized = _startMinimized.Checked;
            _settings.KeepLastFolder = _keepLastFolder.Checked;
            _settings.SuppressMultipleToolLaunch = _suppressMultiple.Checked;
        };

        // C-1: AutoScaleMode の代入はその場で PerformAutoScale を走らせる。ClientSize と
        // Controls が揃ってからでないと、まだ 96 DPI のレイアウトを拡大できない
        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16: 座標と大きさは 96 DPI（100%）で書いてある
        AutoScaleMode = AutoScaleMode.Dpi;
    }
}
