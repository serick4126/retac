using System.Drawing;
using System.Windows.Forms;
using ReTAC.Domain.Navigation;
using ReTAC.Domain.Tools;

namespace ReTAC.App;

/// <summary>
/// 外部ツールの入力ダイアログ（R-131）。定義の全項目を表示順に 1 列で並べ、起動の前に 1 回だけ出す。
/// ヘルパーのプレビュー（R-133）も同じものを出す。履歴と前回の値を書くのは呼び出し側（プレビューは書かない）。
/// 項目の部分だけをスクロールにし、ボタンの行は常に見せる（150% 表示でフォルダの項目 10 個は 1366×768 に収まらない）。
/// 2 列にはしない: ラベルの長さを利用者が決めるので、表示倍率との組み合わせで文字が重なる。
/// </summary>
public sealed class PromptDialog : Form
{
    /// <summary>本文の幅（96 DPI の論理値）。コピー・移動のダイアログ（536）と同じ外幅になる</summary>
    private const int BodyWidth = 512;
    /// <summary>
    /// 縦のスクロールバーの幅（論理値）。項目が多いと縦のスクロールバーが出る。その分を常に空けておかないと、
    /// 本文の幅いっぱいの入力欄が収まらず、横のスクロールバーまで出る
    /// </summary>
    private const int ScrollReserve = 18;

    private readonly PromptDefinition _definition;
    private readonly FolderHistory _history;
    private readonly QuickAccessList? _quickAccess;
    private readonly string _currentFolder;
    private readonly FlowLayoutPanel _body = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        Padding = new Padding(12, 8, 12 + ScrollReserve, 4),
    };
    private readonly Panel _buttons = new() { Dock = DockStyle.Bottom, Height = 48 };
    /// <summary>項目の番号 → 値を持つコントロール（TextBox・CheckBox・ComboBox）</summary>
    private readonly Dictionary<int, Control> _inputs = [];
    private readonly HashSet<TextBox> _folderBoxes = [];
    private readonly HashSet<TextBox> _fileBoxes = [];
    private bool _historyKeyGiven;
    private bool _browseKeyGiven;

    public PromptDialog(PromptDefinition definition, string toolName, FolderHistory history, QuickAccessList? quickAccess,
                        string currentFolder, IReadOnlyDictionary<int, PromptValue> initial)
    {
        _definition = definition;
        _history = history;
        _quickAccess = quickAccess;
        _currentFolder = currentFolder;

        Text = definition.Title.Length > 0 ? definition.Title : toolName;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;   // ダイアログはタスクバーに出さない
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;

        foreach (var item in definition.Items) AddItem(item);

        var reset = new Button { Text = "初期値に戻す(&R)", Bounds = new Rectangle(12, 10, 124, 28), Enabled = definition.Items.Count > 0 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(338 + ScrollReserve, 10, 90, 28) };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(434 + ScrollReserve, 10, 90, 28) };
        _buttons.Controls.AddRange([reset, ok, cancel]);
        // Fill を先に足す。ドッキングは後に足したものから場所を取るので、ボタンの行が先に下を取り、残りを本文が埋める
        Controls.Add(_body);
        Controls.Add(_buttons);
        AcceptButton = ok;
        CancelButton = cancel;   // R-18: Esc は常に中止

        reset.Click += (_, _) =>
        {
            // 確認を挟まない。開いているダイアログの入力だけを変え、定義・前回の値・履歴は変えない（R-131 / R-132）
            ResetValues();
            FocusFirst();
        };
        FormClosing += OnClosing;
        Shown += (_, _) => FocusFirst();

        SetValues(initial);
        ClientSize = new Size(BodyWidth + 24 + ScrollReserve, 400);   // 高さは OnLoad で中身に合わせる

        // C-1: ClientSize と Controls が揃ってから
        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    /// <summary>OK で閉じたときの入力欄の値（前回の値に使う）。</summary>
    public IReadOnlyDictionary<int, PromptValue> Values { get; private set; } = new Dictionary<int, PromptValue>();

    /// <summary>OK で閉じたときの、展開に使う値（フォルダは絶対パス、空欄はカレントフォルダ）。</summary>
    public IReadOnlyDictionary<int, PromptValue> Resolved { get; private set; } = new Dictionary<int, PromptValue>();

    /// <summary>OK で閉じたとき、フォルダ履歴に入れるパス。</summary>
    public IReadOnlyList<string> HistoryFolders { get; private set; } = [];

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // 拡大の後の実ピクセルで測る。モニターの作業領域を超えるなら、項目の部分をスクロールにする（R-131）
        var area = Screen.FromControl(Owner ?? (Control)this).WorkingArea;
        var wanted = _body.GetPreferredSize(new Size(_body.ClientSize.Width, 0)).Height + _buttons.Height;
        var frame = Height - ClientSize.Height;
        ClientSize = new Size(ClientSize.Width, Math.Min(wanted, area.Height - frame));

        // 呼び出し側（OwnerModal・CenterParent）は仮の高さで中央に置いている。高さが決まった今、置き直して作業領域に収める。
        // 置き直さないと、伸びた分だけ下端が画面の外に出て、OK・キャンセルが見えなくなる
        StartPosition = FormStartPosition.Manual;
        Location = DialogPlacement.Place(Owner?.Bounds ?? Bounds, Size, area);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // AcceptButton があると Enter は入力欄の KeyDown に届かない。修飾つきの Enter はここで受ける（PathInputDialog と同じ）
        if (keyData == (Keys.Enter | Keys.Shift) && ActiveControl is TextBox box && (_folderBoxes.Contains(box) || _fileBoxes.Contains(box)))
        {
            Browse(box);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void AddItem(PromptItem item)
    {
        if (item.Kind != PromptItemKind.CheckBox)
        {
            // ラベルは利用者が書く文字。& をアクセスキーにしない
            _body.Controls.Add(new Label
            {
                Text = item.Label, AutoSize = true, MaximumSize = new Size(BodyWidth, 0), UseMnemonic = false, Margin = new Padding(0, 6, 0, 2),
            });
        }

        Control input;
        switch (item.Kind)
        {
            case PromptItemKind.Text:
                input = new TextBox { Width = BodyWidth, Margin = new Padding(0, 0, 0, 4) };
                _body.Controls.Add(input);
                break;

            case PromptItemKind.Folder or PromptItemKind.File:
                var folder = item.Kind == PromptItemKind.Folder;
                var box = new TextBox { Width = folder ? 300 : 418, Margin = new Padding(0, 2, 6, 0) };
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
                row.Controls.Add(box);
                if (folder)
                {
                    // アクセスキーは最初のフォルダの項目にだけ付ける（重ねると押すたびに行き先が変わる）
                    var recall = new Button { Text = _historyKeyGiven ? "履歴 ▼" : "履歴 ▼(&H)", Size = new Size(110, 26), Margin = new Padding(0, 0, 6, 0) };
                    _historyKeyGiven = true;
                    recall.Click += (_, _) => PathRecall.ShowBoth(box, _history, _quickAccess);
                    row.Controls.Add(recall);
                    box.KeyDown += (_, e) =>
                    {
                        if (PathRecall.HandleKey(box, e.KeyCode, _history, _quickAccess)) e.Handled = e.SuppressKeyPress = true;
                    };
                    _folderBoxes.Add(box);
                }
                else
                {
                    _fileBoxes.Add(box);
                }
                var browse = new Button { Text = _browseKeyGiven ? "参照..." : "参照(&B)...", Size = new Size(86, 26), Margin = new Padding(0) };
                _browseKeyGiven = true;
                browse.Click += (_, _) => Browse(box);
                row.Controls.Add(browse);
                _body.Controls.Add(row);
                _body.Controls.Add(new Label
                {
                    Text = folder ? "Shift+Enter:フォルダ参照   ↑:フォルダ履歴   ↓:クイックアクセス" : "Shift+Enter:ファイル参照",
                    AutoSize = true, Margin = new Padding(0, 2, 0, 4),
                });
                input = box;
                break;

            case PromptItemKind.CheckBox:
                input = new CheckBox
                {
                    Text = item.Label, AutoSize = true, MaximumSize = new Size(BodyWidth, 0), UseMnemonic = false, Margin = new Padding(0, 6, 0, 2),
                };
                _body.Controls.Add(input);
                break;

            default:
                var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, Margin = new Padding(0, 0, 0, 4) };
                combo.DisplayMember = nameof(PromptChoice.Label);
                foreach (var choice in item.Choices) combo.Items.Add(choice);
                input = combo;
                _body.Controls.Add(combo);
                break;
        }

        // Tab で移った欄が見えるようにする（R-131）
        input.Enter += (_, _) => _body.ScrollControlIntoView(input);
        _inputs[item.Id] = input;
    }

    internal IReadOnlyDictionary<int, PromptValue> ReadValues()
    {
        // 番号が重なっていても例外にしない（後の項目が勝つ。重なりは定義の検査が知らせる）。ToDictionary は使わない
        var values = new Dictionary<int, PromptValue>();
        foreach (var item in _definition.Items)
        {
            values[item.Id] = _inputs[item.Id] switch
            {
                CheckBox check => new PromptValue(Checked: check.Checked),
                ComboBox combo => new PromptValue(ChoiceId: (combo.SelectedItem as PromptChoice)?.Id ?? 0),
                var text => new PromptValue(text.Text),
            };
        }
        return values;
    }

    internal void ResetValues() => SetValues(PromptAnswerRules.Defaults(_definition));

    private void SetValues(IReadOnlyDictionary<int, PromptValue> values)
    {
        foreach (var item in _definition.Items)
        {
            var value = values.GetValueOrDefault(item.Id) ?? new PromptValue();
            switch (_inputs[item.Id])
            {
                case CheckBox check:
                    check.Checked = value.Checked;
                    break;
                case ComboBox combo:
                    combo.SelectedItem = item.Choices.FirstOrDefault(c => c.Id == value.ChoiceId) ?? item.Choices.FirstOrDefault();
                    break;
                case var text:
                    text.Text = value.Text;
                    break;
            }
        }
    }

    private void FocusFirst()
    {
        if (_definition.Items.Count > 0) FocusItem(_definition.Items[0].Id);
    }

    private void FocusItem(int id)
    {
        var control = _inputs[id];
        control.Focus();
        if (control is TextBox box) box.SelectAll();   // R-46
        _body.ScrollControlIntoView(control);
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK) return;
        var raw = ReadValues();
        var check = PromptAnswerRules.Check(_definition, raw, _currentFolder);
        if (check.Values is null)
        {
            // R-48: 中止せず、その欄に戻す
            MessageBox.Show(this, check.Error, "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
            FocusItem(check.ItemId!.Value);
            return;
        }
        // OwnerModal のモードレス表示では Close の直後に破棄され、入力欄の文字が読めなくなる。
        // 呼び出し側は await の後で読むので、閉じると決まった今のうちに確保する（TextInputDialog と同じ理由）
        Values = raw;
        Resolved = check.Values;
        HistoryFolders = PromptAnswerRules.HistoryFolders(_definition, raw, check.Values);
    }

    /// <summary>
    /// 参照のダイアログの最初の場所。入力のまま <c>PathResolver.Resolve</c> に渡して解決する（引用符は解決の処理が 1 回だけ外す。R-127）。
    /// フォルダの参照（FolderBrowser）は解決の処理を通らないので、解決したパスを渡さないと、引用符付きや相対パスがカレントフォルダで開いてしまう。
    /// </summary>
    /// <returns>解決したパス。空欄・解決できないときは null（呼び出し側はカレントフォルダで開く）</returns>
    internal static string? BrowseStart(string text, string currentFolder) =>
        InputText.Unquote(text).Length == 0 ? null : PathResolver.Resolve(currentFolder, text);

    private void Browse(TextBox box)
    {
        var resolved = BrowseStart(box.Text, _currentFolder);
        if (_folderBoxes.Contains(box))
        {
            if (FolderBrowser.Select(this, resolved, _currentFolder) is not { } selected) return;
            box.Text = selected;
        }
        else
        {
            using var dialog = new OpenFileDialog
            {
                InitialDirectory = resolved is null ? _currentFolder
                    : Directory.Exists(resolved) ? resolved
                    : Path.GetDirectoryName(resolved) ?? _currentFolder,
                FileName = resolved is not null && File.Exists(resolved) ? Path.GetFileName(resolved) : "",
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            box.Text = dialog.FileName;
        }
        box.SelectAll();
    }
}

/// <summary>ダイアログの位置。大きさが決まった後に置き直すために使う（R-131）。</summary>
internal static class DialogPlacement
{
    /// <summary>親の中央に置き、作業領域の中へ収める。作業領域より大きければ左上に合わせる（ボタンの行を見せることを優先する）。</summary>
    public static Point Place(Rectangle owner, Size size, Rectangle area)
    {
        var x = owner.Left + (owner.Width - size.Width) / 2;
        var y = owner.Top + (owner.Height - size.Height) / 2;
        return new Point(
            Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - size.Width)),
            Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - size.Height)));
    }
}
