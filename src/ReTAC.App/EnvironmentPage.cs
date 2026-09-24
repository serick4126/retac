using System.Drawing;
using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>
/// 動作環境の設定（0x8318）。統合設定画面（R-102）のページ。14.1 節のうち、現に挙動を変える項目だけを置く。
/// チェックの変更はその場で <see cref="SettingsDraft"/> へ反映する（OK 待ちにしない）。
/// </summary>
public sealed class EnvironmentPage : UserControl
{
    private readonly SettingsDraft _draft;

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

    public EnvironmentPage(SettingsDraft draft)
    {
        _draft = draft;

        // R-102-3: 拡大は SettingsDialog だけが行う。ページは 96 DPI のまま書き、親の拡大に任せる
        AutoScaleMode = AutoScaleMode.Inherit;
        Size = new Size(440, 144);   // 旧ダイアログのクライアント領域から OK/キャンセルの行を除いた大きさ

        _resident.Checked = draft.Resident;
        _startMinimized.Checked = draft.StartMinimized;
        _keepLastFolder.Checked = draft.KeepLastFolder;
        _suppressMultiple.Checked = draft.SuppressMultipleToolLaunch;
        Controls.AddRange([_resident, _startMinimized, _keepLastFolder, _suppressMultiple]);
        OptionHelp.Attach(this, _tips,
            (_suppressMultiple, "ON: 複数マークしていても、カーソル位置の 1 件だけで 1 回起動する。外部ツールの設定の「マークした項目ごとに起動する」は灰色になる。\nOFF: 各ツールの設定に従う。"));

        _resident.CheckedChanged += (_, _) =>
        {
            _draft.Resident = _resident.Checked;
            _startMinimized.Enabled = _resident.Checked;
        };
        _startMinimized.Enabled = _resident.Checked;
        _startMinimized.CheckedChanged += (_, _) => _draft.StartMinimized = _startMinimized.Checked;
        _keepLastFolder.CheckedChanged += (_, _) => _draft.KeepLastFolder = _keepLastFolder.Checked;
        // F-09: SuppressMultipleToolLaunch の set は変わったときだけ SuppressMultipleChanged を上げる（外部ツールページの灰色表示用）
        _suppressMultiple.CheckedChanged += (_, _) => _draft.SuppressMultipleToolLaunch = _suppressMultiple.Checked;
    }
}
