using System.Drawing;
using System.Windows.Forms;
using System.IO;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Navigation;
using ReTAC.Domain.Tools;

namespace ReTAC.App;

/// <summary>
/// 「クイックアクセスの設定」（16.2 節）。統合設定画面（R-102）のページ。追加 / 変更 / 削除 / フォルダ参照 / 並べ替え。
/// 実行時オプション 2 つもここで切り替える。
/// R-102-3・Q1: 独立ダイアログだった「アクセス」（選んだ項目へ直接移動する）と「閉じる」は無くす。
/// 一覧のダブルクリックは今どおり「変更」。編集はすべて下書きの <see cref="SettingsDraft.QuickAccess"/> へ直接向ける。
/// 「コマンドを追加」で選べるツールの一覧は下書きのツールで、外部ツールページの変更を
/// <see cref="SettingsDraft.ToolsChanged"/> で拾って一覧の表示（コマンド名）を作り直す（§2.4）。
/// </summary>
public sealed class QuickAccessPage : UserControl
{
    private readonly SettingsDraft _draft;
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

    private QuickAccessList List => _draft.QuickAccess;
    private IReadOnlyList<ExternalTool> Tools => _draft.ExternalTools;

    public QuickAccessPage(SettingsDraft draft, string currentFolder)
    {
        _draft = draft;
        _currentFolder = currentFolder;

        AutoScaleMode = AutoScaleMode.Inherit;   // R-102-3: 拡大は SettingsDialog だけが行う
        Size = new Size(600, 412);   // 旧ダイアログのクライアント領域から OK/キャンセルの分（アクセス・閉じるも消えた）を除いた大きさ

        _view.SetBounds(12, 32, 440, 312);
        // 列幅は 96 DPI の値で持ち、枠が親に入って拡大を済ませた後に ApplyDpi で当てる（R-66）
        _view.Columns.Add("タイトル", 120);
        _view.Columns.Add("登録先", 240);
        _view.Columns.Add("種類", 60);   // R-92
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
        _showTitles.Checked = List.ShowTitles;
        _fixMissing.Checked = List.FixMissingAutomatically;
        _showTitles.CheckedChanged += (_, _) => List.ShowTitles = _showTitles.Checked;
        _fixMissing.CheckedChanged += (_, _) => List.FixMissingAutomatically = _fixMissing.Checked;

        Controls.AddRange([listLabel, _view, _showTitles, _fixMissing]);

        // §2.4: 外部ツールの改名・削除を、コマンドを指す行の表示にすぐ反映する
        _draft.ToolsChanged += (_, _) => Reload(Selected);

        Reload(0);
    }

    /// <summary>枠が <see cref="AutoScaleMode"/> を当てた直後・<see cref="Form.DpiChanged"/> のたびに枠から呼ぶ。</summary>
    public void ApplyDpi(int dpi)
    {
        _view.Columns[0].Width = 120 * dpi / 96;
        _view.Columns[1].Width = 240 * dpi / 96;
        _view.Columns[2].Width = 60 * dpi / 96;
    }

    private int Selected => _view.SelectedIndices.Count > 0 ? _view.SelectedIndices[0] : -1;

    private void Reload(int select)
    {
        _view.BeginUpdate();
        _view.Items.Clear();
        foreach (var entry in List.Items)
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
        if (!List.Add(dialog.Entry))
        {
            Reload(List.IndexOfPath(dialog.Entry.Path));
            MessageBox.Show(this, "このフォルダは登録済みです。", "ReTAC",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Reload(List.Items.Count - 1);
    }

    /// <summary>コマンドは保存の形（"Refresh" や "tool:3"）ではなく名前で出す。</summary>
    private string TargetText(QuickAccessEntry entry) =>
        entry.Kind == BookmarkKind.Command ? CommandLabels.Of(CommandTarget.Parse(entry.Path), Tools) : entry.Path;

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
        if (CommandPickerDialog.Pick(this, Tools) is not { } target) return;
        AddEntry(new QuickAccessEntry("", target.Serialize(), BookmarkKind.Command));
    }

    private void AddEntry(QuickAccessEntry entry)
    {
        if (!List.Add(entry))
        {
            Reload(List.IndexOf(entry));
            MessageBox.Show(this, "登録済みです。", "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Reload(List.Items.Count - 1);
    }

    private void Edit()
    {
        if (Selected < 0) return;
        if (List.Items[Selected].Kind != BookmarkKind.Folder)
        {
            EditTitle();
            return;
        }
        using var dialog = new QuickAccessEntryDialog(List.Items[Selected], _currentFolder, isEdit: true);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var index = Selected;
        if (!List.Replace(index, dialog.Entry))
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
        var entry = List.Items[index];
        using var dialog = new TextInputDialog("タイトルの変更", "タイトル（空なら登録先の名前を出す）:", entry.Title);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        List.Replace(index, entry with { Title = dialog.Value });
        Reload(index);
    }

    private void Remove()
    {
        if (Selected < 0) return;
        var index = Selected;
        List.RemoveAt(index);
        Reload(index);
    }

    /// <summary>「フォルダ参照」ボタン。選んだ結果は登録項目のパスを置き換える。</summary>
    private void BrowseSelected()
    {
        if (Selected < 0) return;
        var index = Selected;
        var entry = List.Items[index];
        if (entry.Kind != BookmarkKind.Folder) return;   // R-92: フォルダの項目だけ
        if (FolderBrowser.Select(this, entry.Path, _currentFolder) is not { } selected) return;
        if (!List.Replace(index, entry with { Path = selected }))
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
        Reload(List.Move(Selected, delta));
    }
}
