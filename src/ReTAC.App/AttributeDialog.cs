using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>
/// ファイルの属性（`A` / 0x82E2）。16 節のダイアログに合わせる。
/// R-49: 「属性を変更」と「タイムスタンプを変更」は独立したブロックで、
/// <b>属性に触れずタイムスタンプだけを揃える</b>操作が成立すること。
/// R-49-2: 対象にフォルダを含むときだけ「サブフォルダのファイルも変更する」が効く。
/// R-50: 「以降全て」で残りの対象に同じ設定を適用する。
/// </summary>
public sealed class AttributeDialog : Form
{
    private readonly CheckBox _changeAttributes = new() { Text = "属性を変更(&A)", AutoSize = true, Location = new Point(16, 44) };
    private readonly CheckBox _readOnly = new() { Text = "書込禁止(&R)", AutoSize = true, Location = new Point(40, 70) };
    private readonly CheckBox _hidden = new() { Text = "隠し属性(&H)", AutoSize = true, Location = new Point(220, 70) };
    private readonly CheckBox _archive = new() { Text = "アーカイブ(&C)", AutoSize = true, Location = new Point(40, 96) };
    private readonly CheckBox _system = new() { Text = "システム(&S)", AutoSize = true, Location = new Point(220, 96) };

    private readonly CheckBox _changeTimestamp = new() { Text = "タイムスタンプを変更(&D)", AutoSize = true, Location = new Point(16, 130) };
    private readonly DateTimePicker _date = new()
    {
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "yyyy / MM / dd",
        ShowUpDown = true,                 // 年月日を個別に増減できる（R-49）
        Bounds = new Rectangle(90, 156, 180, 23),
    };
    private readonly DateTimePicker _time = new()
    {
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "HH : mm : ss",
        ShowUpDown = true,
        Bounds = new Rectangle(90, 186, 180, 23),
    };
    private readonly CheckBox _includeSubfolders = new()
    {
        Text = "サブフォルダのファイルも変更する(&F)",
        AutoSize = true,
        Location = new Point(16, 220),
    };

    public AttributeDialog(string name, FileAttributes attributes, DateTime lastWrite,
                           bool containsFolder, bool allowApplyToAll)
    {
        Text = "ファイルの属性";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;   // ダイアログはタスクバーに出さない（既定は true）
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(400, 300);

        Controls.Add(new Label { Text = $"名前  {name}", AutoSize = true, Location = new Point(16, 14), MaximumSize = new Size(370, 0) });

        _readOnly.Checked = attributes.HasFlag(FileAttributes.ReadOnly);
        _hidden.Checked = attributes.HasFlag(FileAttributes.Hidden);
        _archive.Checked = attributes.HasFlag(FileAttributes.Archive);
        _system.Checked = attributes.HasFlag(FileAttributes.System);
        _date.Value = _time.Value = lastWrite;

        Controls.AddRange([_changeAttributes, _readOnly, _hidden, _archive, _system,
                           _changeTimestamp, _date, _time, _includeSubfolders,
                           new Label { Text = "日付:", AutoSize = true, Location = new Point(40, 160) },
                           new Label { Text = "時刻:", AutoSize = true, Location = new Point(40, 190) }]);

        _changeAttributes.CheckedChanged += (_, _) => SyncEnabled();
        _changeTimestamp.CheckedChanged += (_, _) => SyncEnabled();
        _includeSubfolders.Enabled = containsFolder;   // R-49-2
        SyncEnabled();

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(60, 258, 90, 28) };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(160, 258, 90, 28) };
        var all = new Button { Text = "以降全て(&Z)", Bounds = new Rectangle(260, 258, 100, 28), Enabled = allowApplyToAll };
        all.Click += (_, _) => { ApplyToAll = true; DialogResult = DialogResult.OK; Close(); };
        Controls.AddRange([ok, cancel, all]);
        AcceptButton = ok;
        CancelButton = cancel;

        // C-1: AutoScaleMode の代入はその場で PerformAutoScale を走らせる。ClientSize と
        // Controls が揃ってからでないと、まだ 96 DPI のレイアウトを拡大できない
        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16: 座標と大きさは 96 DPI（100%）で書いてある
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    public bool ChangeAttributes => _changeAttributes.Checked;
    public bool ChangeTimestamp => _changeTimestamp.Checked;
    public bool IncludeSubfolders => _includeSubfolders.Enabled && _includeSubfolders.Checked;
    public bool ApplyToAll { get; private set; }

    public DateTime Timestamp => _date.Value.Date + _time.Value.TimeOfDay;

    public FileAttributes Attributes =>
        (_readOnly.Checked ? FileAttributes.ReadOnly : 0)
        | (_hidden.Checked ? FileAttributes.Hidden : 0)
        | (_archive.Checked ? FileAttributes.Archive : 0)
        | (_system.Checked ? FileAttributes.System : 0);

    private void SyncEnabled()
    {
        foreach (var box in new[] { _readOnly, _hidden, _archive, _system }) box.Enabled = _changeAttributes.Checked;
        _date.Enabled = _time.Enabled = _changeTimestamp.Checked;
    }
}
