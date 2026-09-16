using System.Drawing;
using System.Windows.Forms;
using ReTAC.Domain.Navigation;

namespace ReTAC.App;

/// <summary>
/// 「アクセスフォルダの追加」（16.2 節）。タイトル欄 ＋ フォルダ欄の 2 項目。
/// §9.1: 使えるキー操作を画面に印字し（R-47）、入力の誤りは中止せず入力欄に戻す（R-48）。
/// </summary>
public sealed class QuickAccessEntryDialog : Form
{
    private readonly TextBox _title = new();
    private readonly TextBox _path = new();
    private readonly string _currentFolder;

    /// <param name="preset">入力欄の初期値。null なら空とカレントフォルダ</param>
    /// <param name="isEdit">既存項目の変更なら true。見出しだけが変わる</param>
    public QuickAccessEntryDialog(QuickAccessEntry? preset, string currentFolder, bool isEdit = false)
    {
        _currentFolder = currentFolder;
        Text = isEdit ? "アクセスフォルダの変更" : "アクセスフォルダの追加";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;   // ダイアログはタスクバーに出さない（既定は true）
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(440, 150);

        _path.Text = preset?.Path ?? currentFolder;
        // 卓駆と同じく、タイトルの初期値はフォルダ名。設定ダイアログの「追加」は preset を
        // 持たないので、埋めるのは呼び出し側ではなくここ（どの経路から来ても同じになる）
        _title.Text = preset?.Title is { Length: > 0 } title ? title : TitleFrom(_path.Text);

        var titleLabel = new Label { Text = "タイトル(&T):", AutoSize = true, Location = new Point(12, 18) };
        _title.SetBounds(110, 15, 310, 23);
        var pathLabel = new Label { Text = "フォルダ(&F):", AutoSize = true, Location = new Point(12, 50) };
        _path.SetBounds(110, 47, 310, 23);
        var hint = new Label
        {
            Text = "※ Shift + Enter : フォルダ参照",   // R-47 / N-07
            AutoSize = true,
            Location = new Point(110, 76),
        };

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(110, 108, 90, 28) };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(210, 108, 90, 28) };
        var browse = new Button { Text = "参照(&B)...", Bounds = new Rectangle(320, 108, 100, 28) };
        browse.Click += (_, _) => Browse();

        Controls.AddRange([titleLabel, _title, pathLabel, _path, hint, ok, cancel, browse]);
        AcceptButton = ok;
        CancelButton = cancel;

        // R-46: プリセットは全選択で渡す
        Shown += (_, _) => { _title.Focus(); _title.SelectAll(); };
        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK) return;
            if (Directory.Exists(InputText.TrimEdge(_path.Text))) return;
            // R-48: 誤りは操作を中止せず入力欄に戻す
            MessageBox.Show(this, $"{_path.Text} は存在しません。", "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
            _path.Focus();
            _path.SelectAll();
        };

        // C-1: AutoScaleMode の代入はその場で PerformAutoScale を走らせる。ClientSize と
        // Controls が揃ってからでないと、まだ 96 DPI のレイアウトを拡大できない
        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16: 座標と大きさは 96 DPI（100%）で書いてある
        AutoScaleMode = AutoScaleMode.Dpi;   // R-66: DPI に追従させる
    }

    public QuickAccessEntry Entry => new(InputText.TrimEdge(_title.Text), InputText.TrimEdge(_path.Text));

    /// <summary>フォルダ名をタイトルの初期値にする。ドライブ直下は名前が無いのでパスをそのまま。</summary>
    private static string TitleFrom(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        return name.Length > 0 ? name : path;
    }

    private void Browse()
    {
        if (FolderBrowser.Select(this, InputText.TrimEdge(_path.Text), _currentFolder) is { } selected)
        {
            _path.Text = selected;   // R-52-3: 選んだ結果は入力欄へ流し込む
            _path.SelectAll();
        }
    }

    /// <summary>
    /// AcceptButton を置くと Enter は ProcessDialogKey の段階で確定ボタンに食われ、
    /// 入力欄の KeyDown には届かない。Shift+Enter はここで先に受ける（N-07）。
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData != (Keys.Enter | Keys.Shift)) return base.ProcessCmdKey(ref msg, keyData);
        Browse();
        return true;
    }
}
