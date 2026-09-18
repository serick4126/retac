using System.Drawing;
using System.Windows.Forms;
using System.IO;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Navigation;
using ReTAC.Domain.Tools;

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

    private readonly IReadOnlyList<ExternalTool> _tools;

    public QuickAccessDialog(QuickAccessList list, string currentFolder, IReadOnlyList<ExternalTool> tools)
    {
        _list = list;
        _tools = tools;
        _currentFolder = currentFolder;

        Text = "クイックアクセスの設定";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;   // ダイアログはタスクバーに出さない（既定は true）
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(600, 452);

        _view.SetBounds(12, 32, 440, 312);
        // 列幅は AutoScaleMode.Dpi の対象外なので自分で追従させる（R-66）
        _view.Columns.Add("タイトル", 120 * DeviceDpi / 96);
        _view.Columns.Add("登録先", 240 * DeviceDpi / 96);
        _view.Columns.Add("種類", 60 * DeviceDpi / 96);   // R-92
        _view.DoubleClick += (_, _) => Edit();

        var listLabel = new Label { Text = "アクセスリスト(&L):", AutoSize = true, Location = new Point(12, 12) };

        var buttons = new (string Text, Action Do)[]
        {
            ("フォルダを追加(&A)...", Add),
            ("ファイルを追加(&I)...", AddFile),     // R-92
            ("コマンドを追加(&O)...", AddCommand),  // R-92
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

        _showTitles.Location = new Point(16, 360);
        _fixMissing.Location = new Point(16, 384);
        _showTitles.Checked = _list.ShowTitles;
        _fixMissing.Checked = _list.FixMissingAutomatically;

        // 「アクセス」は選んだフォルダへそのまま移動する（卓駆の同ダイアログと同じ）
        var access = new Button { Text = "アクセス(&S)", DialogResult = DialogResult.OK, Bounds = new Rectangle(340, 412, 118, 30) };
        access.Click += (_, _) => Chosen = Selected >= 0 ? _list.Items[Selected] : null;
        var close = new Button { Text = "閉じる(&C)", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(470, 412, 118, 30) };
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

    /// <summary>「アクセス」で選ばれた項目（R-92: フォルダならジャンプ、ファイルは開く、コマンドは実行）。閉じただけなら null。</summary>
    public QuickAccessEntry? Chosen { get; private set; }

    private int Selected => _view.SelectedIndices.Count > 0 ? _view.SelectedIndices[0] : -1;

    private void Reload(int select)
    {
        _view.BeginUpdate();
        _view.Items.Clear();
        foreach (var entry in _list.Items)
            _view.Items.Add(new ListViewItem([entry.Title, TargetText(entry), KindText(entry.Kind)]));
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

    /// <summary>コマンドは保存の形（"Refresh" や "tool:3"）ではなく名前で出す。</summary>
    private string TargetText(QuickAccessEntry entry) =>
        entry.Kind == BookmarkKind.Command ? CommandLabels.Of(CommandTarget.Parse(entry.Path), _tools) : entry.Path;

    private static string KindText(BookmarkKind kind) => kind switch
    {
        BookmarkKind.File => "ファイル",
        BookmarkKind.Command => "コマンド",
        _ => "フォルダ",
    };

    /// <summary>R-92: ファイルを登録する。題名はファイル名。</summary>
    private void AddFile()
    {
        using var open = new OpenFileDialog { InitialDirectory = _currentFolder, CheckFileExists = true };
        if (open.ShowDialog(this) != DialogResult.OK) return;
        AddEntry(new QuickAccessEntry(Path.GetFileName(open.FileName), open.FileName, BookmarkKind.File));
    }

    /// <summary>R-92: 組み込みコマンド・外部ツールを登録する。題名は空（表示はコマンドの名前）。</summary>
    private void AddCommand()
    {
        if (CommandPickerDialog.Pick(this, _tools) is not { } target) return;
        AddEntry(new QuickAccessEntry("", target.Serialize(), BookmarkKind.Command));
    }

    private void AddEntry(QuickAccessEntry entry)
    {
        if (!_list.Add(entry))
        {
            Reload(_list.IndexOf(entry));
            MessageBox.Show(this, "登録済みです。", "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Reload(_list.Items.Count - 1);
    }

    private void Edit()
    {
        if (Selected < 0) return;
        if (_list.Items[Selected].Kind != BookmarkKind.Folder)
        {
            EditTitle();
            return;
        }
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

    /// <summary>ファイル・コマンドは題名だけを変える（登録先はフォルダの編集ダイアログの対象外）。</summary>
    private void EditTitle()
    {
        var index = Selected;
        var entry = _list.Items[index];
        using var dialog = new TextInputDialog("タイトルの変更", "タイトル（空なら登録先の名前を出す）:", entry.Title);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _list.Replace(index, entry with { Title = dialog.Value });
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
        if (entry.Kind != BookmarkKind.Folder) return;   // R-92: フォルダの項目だけ
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
