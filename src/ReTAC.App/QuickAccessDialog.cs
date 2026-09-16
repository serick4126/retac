using System.Drawing;
using System.Windows.Forms;
using ReTAC.Domain.Navigation;

namespace ReTAC.App;

/// <summary>
/// 「クイックアクセスの設定」（16.2 節）。追加 / 変更 / 削除 / 確認 / フォルダ参照 / 並べ替え。
/// 実行時オプション 2 つもここで切り替える。
/// </summary>
public sealed class QuickAccessDialog : Form
{
    private readonly QuickAccessList _list;
    private readonly string _currentFolder;
    private readonly ListView _view = new()
    {
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
    };
    private readonly CheckBox _showTitles = new() { Text = "タイトル（フォルダの別名）を表示する(&T)", AutoSize = true };
    private readonly CheckBox _fixMissing = new() { Text = "フォルダが存在しない時は、自動でリストを修正する(&C)", AutoSize = true };

    public QuickAccessDialog(QuickAccessList list, string currentFolder)
    {
        _list = list;
        _currentFolder = currentFolder;

        Text = "クイックアクセスの設定";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;   // ダイアログはタスクバーに出さない（既定は true）
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(600, 380);

        _view.SetBounds(12, 32, 440, 240);
        // 列幅は AutoScaleMode.Dpi の対象外なので自分で追従させる（R-66）
        _view.Columns.Add("タイトル", 130 * DeviceDpi / 96);
        _view.Columns.Add("フォルダパス", 290 * DeviceDpi / 96);
        _view.DoubleClick += (_, _) => Edit();

        var listLabel = new Label { Text = "アクセスリスト(&L):", AutoSize = true, Location = new Point(12, 12) };

        var buttons = new (string Text, Action Do)[]
        {
            ("追加(&A)", Add),
            ("変更(&M)", Edit),
            ("削除(&D)", Remove),
            ("フォルダ参照(&F)", BrowseSelected),
            ("↑(&U)", () => MoveSelected(-1)),
            ("↓(&W)", () => MoveSelected(1)),
        };
        var y = 32;
        foreach (var (text, action) in buttons)
        {
            var button = new Button { Text = text, Bounds = new Rectangle(470, y, 118, 30) };
            button.Click += (_, _) => action();
            Controls.Add(button);
            y += 36;
        }

        _showTitles.Location = new Point(16, 288);
        _fixMissing.Location = new Point(16, 312);
        _showTitles.Checked = _list.ShowTitles;
        _fixMissing.Checked = _list.FixMissingAutomatically;

        // 「アクセス」は選んだフォルダへそのまま移動する（卓駆の同ダイアログと同じ）
        var access = new Button { Text = "アクセス(&S)", DialogResult = DialogResult.OK, Bounds = new Rectangle(340, 340, 118, 30) };
        access.Click += (_, _) => ChosenPath = Selected >= 0 ? _list.Items[Selected].Path : null;
        var close = new Button { Text = "閉じる(&C)", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(470, 340, 118, 30) };
        AcceptButton = access;
        CancelButton = close;

        Controls.AddRange([listLabel, _view, _showTitles, _fixMissing, access, close]);
        FormClosing += (_, _) =>
        {
            _list.ShowTitles = _showTitles.Checked;
            _list.FixMissingAutomatically = _fixMissing.Checked;
        };

        Reload(0);

        // C-1: AutoScaleMode の代入はその場で PerformAutoScale を走らせる。ClientSize と
        // Controls が揃ってからでないと、まだ 96 DPI のレイアウトを拡大できない
        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16: 座標と大きさは 96 DPI（100%）で書いてある
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    /// <summary>「アクセス」で選ばれたフォルダ。閉じただけなら null。</summary>
    public string? ChosenPath { get; private set; }

    private int Selected => _view.SelectedIndices.Count > 0 ? _view.SelectedIndices[0] : -1;

    private void Reload(int select)
    {
        _view.BeginUpdate();
        _view.Items.Clear();
        foreach (var entry in _list.Items)
            _view.Items.Add(new ListViewItem([entry.Title, entry.Path]));
        _view.EndUpdate();

        if (_view.Items.Count == 0) return;
        var index = Math.Clamp(select, 0, _view.Items.Count - 1);
        _view.Items[index].Selected = true;
        _view.Items[index].Focused = true;
    }

    private void Add()
    {
        using var dialog = new QuickAccessEntryDialog(null, _currentFolder);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!_list.Add(dialog.Entry))
        {
            Reload(_list.IndexOfPath(dialog.Entry.Path));
            MessageBox.Show(this, "このフォルダは登録済みです。", "ReTAC",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Reload(_list.Items.Count - 1);
    }

    private void Edit()
    {
        if (Selected < 0) return;
        using var dialog = new QuickAccessEntryDialog(_list.Items[Selected], _currentFolder, isEdit: true);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var index = Selected;
        if (!_list.Replace(index, dialog.Entry))
        {
            MessageBox.Show(this, "このフォルダは既に別の項目で登録済みです。", "ReTAC",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Reload(index);
    }

    private void Remove()
    {
        if (Selected < 0) return;
        var index = Selected;
        _list.RemoveAt(index);
        Reload(index);
    }

    /// <summary>「フォルダ参照」ボタン。選んだ結果は登録項目のパスを置き換える。</summary>
    private void BrowseSelected()
    {
        if (Selected < 0) return;
        var index = Selected;
        var entry = _list.Items[index];
        if (FolderBrowser.Select(this, entry.Path, _currentFolder) is not { } selected) return;
        if (!_list.Replace(index, entry with { Path = selected }))
        {
            MessageBox.Show(this, "このフォルダは既に別の項目で登録済みです。", "ReTAC",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Reload(index);
    }

    private void MoveSelected(int delta)
    {
        if (Selected < 0) return;
        Reload(_list.Move(Selected, delta));
    }
}
