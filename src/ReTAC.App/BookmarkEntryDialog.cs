using System.Drawing;
using System.Windows.Forms;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Navigation;
using ReTAC.Domain.Tools;

namespace ReTAC.App;

/// <summary>
/// R-89: ブックマーク 1 件の題名と登録先を変える（QuickAccessEntryDialog を土台にした）。
/// 登録先の欄は種類で変わる。フォルダ・ファイルはパス、コマンドは選び直し、グループは題名だけ。
/// 種類そのものは変えない（変えたければ消して足し直す）。
/// </summary>
public sealed class BookmarkEntryDialog : Form
{
    private readonly TextBox _title = new();
    private readonly TextBox _path = new();
    private readonly Label _command = new() { AutoEllipsis = true, BorderStyle = BorderStyle.Fixed3D, TextAlign = ContentAlignment.MiddleLeft };
    private readonly CheckBox _iconOnly = new() { Text = "バーではアイコンだけ表示(&L)", AutoSize = true };
    private readonly Bookmark _preset;
    private readonly string _currentFolder;
    private readonly IReadOnlyList<ExternalTool> _tools;
    private readonly Func<CommandTarget, string> _label;
    private string _target;

    /// <param name="preset">変える前の項目。Children（グループの中身）はそのまま引き継ぐ</param>
    /// <param name="currentFolder">手入力の相対パスの基準（Q11）と、参照ダイアログの初期位置</param>
    public BookmarkEntryDialog(Bookmark preset, string currentFolder, IReadOnlyList<ExternalTool> tools,
                               Func<CommandTarget, string> label, bool isEdit = true)
    {
        _preset = preset;
        _currentFolder = currentFolder;
        _tools = tools;
        _label = label;
        _target = preset.Target;
        var kindText = preset.Kind switch
        {
            BookmarkKind.Folder => "フォルダ",
            BookmarkKind.File => "ファイル",
            BookmarkKind.Command => "コマンド",
            _ => "グループ",
        };
        Text = $"{kindText}のブックマークの{(isEdit ? "編集" : "追加")}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(440, 180);

        _title.Text = preset.Title;
        // 題名が空なら、バーには末尾の名前（フォルダ・ファイル）かコマンドの名前が出る（DisplayName）。
        // 名前が要るのはグループだけ（R-106-2。グループには名前の代わりが無い）
        if (preset.Kind is BookmarkKind.Folder or BookmarkKind.File) _title.PlaceholderText = "空ならフォルダ名・ファイル名";
        var titleLabel = new Label { Text = "名前(&T):", AutoSize = true, Location = new Point(12, 18) };
        _title.SetBounds(110, 15, 310, 23);
        Controls.AddRange([titleLabel, _title]);

        // R-106-1: 種類を問わず出す（バーに直接置いていない項目でも、グループからバーへ移したときに効く）
        _iconOnly.Checked = preset.IconOnly;
        _iconOnly.Location = new Point(12, 98);
        Controls.Add(_iconOnly);

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(110, 138, 90, 28) };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(210, 138, 90, 28) };
        Controls.AddRange([ok, cancel]);

        if (preset.Kind != BookmarkKind.Group)
        {
            var targetLabel = new Label { Text = $"{kindText}(&F):", AutoSize = true, Location = new Point(12, 50) };
            var browse = new Button { Text = preset.Kind == BookmarkKind.Command ? "選び直す(&B)..." : "参照(&B)...", Bounds = new Rectangle(320, 138, 100, 28) };
            browse.Click += (_, _) => Browse();
            Controls.AddRange([targetLabel, browse]);
            if (preset.Kind == BookmarkKind.Command)
            {
                _command.SetBounds(110, 47, 310, 23);
                _command.Text = CommandText();
                Controls.Add(_command);
            }
            else
            {
                _path.Text = preset.Target;
                _path.SetBounds(110, 47, 310, 23);
                var hint = new Label { Text = "※ Shift + Enter : 参照", AutoSize = true, Location = new Point(110, 76) };   // R-47 / N-07
                Controls.AddRange([_path, hint]);
            }
        }
        AcceptButton = ok;
        CancelButton = cancel;

        Shown += (_, _) => { _title.Focus(); _title.SelectAll(); };   // R-46
        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK) return;
            if (CheckInput() is not { } error) return;
            // R-48: 誤りは操作を中止せず入力欄に戻す
            MessageBox.Show(this, error, "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
            // フォルダ・ファイルの誤りは登録先だけ。グループ・コマンドの誤りは名前だけ
            var field = _preset.Kind is BookmarkKind.Folder or BookmarkKind.File ? _path : _title;
            field.Focus();
            field.SelectAll();
        };

        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16
        AutoScaleMode = AutoScaleMode.Dpi;   // R-66
    }

    public Bookmark Bookmark => _preset with { Title = InputText.TrimEdge(_title.Text), Target = _target, IconOnly = _iconOnly.Checked };

    private string CommandText() => CommandTarget.Parse(_target) is { } target ? _label(target) : "";

    private string? CheckInput()
    {
        if (_preset.Kind is BookmarkKind.Folder or BookmarkKind.File)
        {
            // Q11: 手入力のパスは、開いているフォルダを基準に絶対パスにする。解決できなければ登録させない
            var input = InputText.TrimEdge(_path.Text);
            if (input.Length == 0) return "登録先を入れてください。";
            if (PathResolver.Resolve(_currentFolder, input) is not { } resolved) return $"{input} はパスとして読めません。";
            _target = resolved;
        }
        return BookmarkRules.Validate(Bookmark);
    }

    private void Browse()
    {
        switch (_preset.Kind)
        {
            case BookmarkKind.Command:
                if (CommandPickerDialog.Pick(this, _tools) is not { } target) return;
                _target = target.Serialize();
                _command.Text = CommandText();
                return;
            case BookmarkKind.Folder:
                if (FolderBrowser.Select(this, InputText.TrimEdge(_path.Text), _currentFolder) is not { } folder) return;
                _path.Text = folder;   // R-52-3: 選んだ結果は入力欄へ流し込む
                break;
            case BookmarkKind.File:
                using (var open = new OpenFileDialog { InitialDirectory = _currentFolder, CheckFileExists = true })
                {
                    if (open.ShowDialog(this) != DialogResult.OK) return;
                    _path.Text = open.FileName;
                }
                break;
            default:
                return;
        }
        _path.SelectAll();
    }

    /// <summary>AcceptButton に Enter が食われる前に Shift+Enter を受ける（N-07。QuickAccessEntryDialog と同じ）。</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData != (Keys.Enter | Keys.Shift) || _preset.Kind == BookmarkKind.Group) return base.ProcessCmdKey(ref msg, keyData);
        Browse();
        return true;
    }
}
