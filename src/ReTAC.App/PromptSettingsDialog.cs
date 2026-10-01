using System.Drawing;
using System.Windows.Forms;
using ReTAC.Domain.Navigation;
using ReTAC.Domain.Tools;

namespace ReTAC.App;

/// <summary>
/// 入力ダイアログの設定（R-133）。外部ツールの設定の「入力ダイアログ編集」と、マクロの一覧の ${prompt} から開く。
/// 表示する項目（表示順）と引数（送る順）の 2 つの表で組み立てる。送る順を表示順と別に持つのは、
/// 項目の間に ${file} を挟む形（WinRAR の展開など）を作るため。インポート・変換・まとめる（R-134）もここで行う。
/// </summary>
public sealed class PromptSettingsDialog : Form
{
    private readonly string _toolName;
    private readonly FolderHistory _history;
    private readonly QuickAccessList? _quickAccess;
    private readonly string _currentFolder;
    private readonly List<PromptItem> _items;
    private readonly List<PromptArgument> _arguments;
    /// <summary>プレビューで OK した値。結果の例はこれで作る（無ければ定義の初期値）</summary>
    private IReadOnlyDictionary<int, PromptValue>? _previewValues;
    /// <summary>元に戻すための、操作の前の状態（R-133）</summary>
    private readonly PromptEditHistory _undo = new();

    private readonly TextBox _title = new() { Bounds = new Rectangle(110, 14, 300, 23) };
    private readonly CheckBox _remember = new() { Text = "前回の入力を初期値にする(&R)", AutoSize = true, Location = new Point(424, 16) };
    private readonly ListView _itemList = NewList(new Rectangle(10, 22, 480, 150), multiSelect: false);
    private readonly ListView _argumentList = NewList(new Rectangle(10, 22, 480, 176), multiSelect: true);
    private readonly Button _itemAdd = Side("追加(&A) ▼", 22);
    private readonly Button _itemEdit = Side("編集(&E)...", 54);
    private readonly Button _itemDelete = Side("削除(&D)", 86);
    private readonly Button _itemUp = Side("上へ(&U)", 118);
    private readonly Button _itemDown = Side("下へ(&N)", 150);
    private readonly Button _argumentAdd = Side("追加(&G) ▼", 22);
    private readonly Button _argumentEdit = Side("編集(&K)...", 54);
    private readonly Button _argumentDelete = Side("削除(&X)", 86);
    private readonly Button _argumentUp = Side("上へ(&O)", 118);
    private readonly Button _argumentDown = Side("下へ(&W)", 150);
    private readonly Button _convert = Side("変換(&V) ▼", 182);
    private readonly Button _undoButton = new() { Text = "元に戻す(&Z)", Bounds = new Rectangle(256, 514, 100, 28), Enabled = false };
    private readonly TextBox _example = new() { ReadOnly = true, Bounds = new Rectangle(14, 476, 612, 23) };

    public PromptSettingsDialog(PromptDefinition? definition, string toolName, FolderHistory history, QuickAccessList? quickAccess, string currentFolder)
    {
        var source = definition ?? new PromptDefinition();
        _toolName = toolName;
        _history = history;
        _quickAccess = quickAccess;
        _currentFolder = currentFolder;
        _items = [.. source.Items];
        _arguments = [.. source.Arguments];
        Definition = source;

        Text = "入力ダイアログの設定";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;

        _title.Text = source.Title;
        _remember.Checked = source.RememberLast;
        _itemList.Columns.Add("種類", 110);
        _itemList.Columns.Add("ラベル", 200);
        _itemList.Columns.Add("初期値", 150);
        _argumentList.Columns.Add("種類", 60);
        _argumentList.Columns.Add("内容", 340);
        _argumentList.Columns.Add("印", 60);

        var items = new GroupBox { Text = "表示する項目（表示順）", Bounds = new Rectangle(14, 46, 612, 182) };
        items.Controls.AddRange([_itemList, _itemAdd, _itemEdit, _itemDelete, _itemUp, _itemDown]);
        var arguments = new GroupBox { Text = "引数（送る順）", Bounds = new Rectangle(14, 234, 612, 212) };
        arguments.Controls.AddRange([_argumentList, _argumentAdd, _argumentEdit, _argumentDelete, _argumentUp, _argumentDown, _convert]);

        var import = new Button { Text = "インポート(&M)...", Bounds = new Rectangle(14, 514, 120, 28) };
        var preview = new Button { Text = "プレビュー(&P)", Bounds = new Rectangle(140, 514, 110, 28) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(440, 514, 90, 28) };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(536, 514, 90, 28) };

        Controls.AddRange(
        [
            new Label { Text = "タイトル(&T):", AutoSize = true, Location = new Point(14, 17) }, _title, _remember,
            items, arguments,
            new Label { Text = "結果の例:", AutoSize = true, Location = new Point(14, 456) }, _example,
            import, preview, _undoButton, ok, cancel,
        ]);
        AcceptButton = ok;
        CancelButton = cancel;
        ClientSize = new Size(640, 556);

        _itemAdd.Click += (_, _) => KindMenu().Show(_itemAdd, new Point(0, _itemAdd.Height));
        _itemEdit.Click += (_, _) => EditItem(SelectedItemId());
        _itemList.DoubleClick += (_, _) => EditItem(SelectedItemId());
        _itemDelete.Click += (_, _) => DeleteItem();
        _itemUp.Click += (_, _) => MoveItem(-1);
        _itemDown.Click += (_, _) => MoveItem(1);
        _itemList.SelectedIndexChanged += (_, _) => UpdateButtons();

        _argumentAdd.Click += (_, _) => ArgumentMenu().Show(_argumentAdd, new Point(0, _argumentAdd.Height));
        _argumentEdit.Click += (_, _) => EditArgument();
        _argumentList.DoubleClick += (_, _) => EditArgument();
        _argumentDelete.Click += (_, _) => DeleteArgument();
        _argumentUp.Click += (_, _) => MoveArgument(-1);
        _argumentDown.Click += (_, _) => MoveArgument(1);
        _convert.Click += async (_, _) => await ShowConvertMenuAsync();
        _argumentList.SelectedIndexChanged += (_, _) => UpdateButtons();

        import.Click += (_, _) => Import();
        preview.Click += (_, _) => Preview();
        _undoButton.Click += (_, _) => Undo();
        FormClosing += OnClosing;

        RefillItems(-1);
        RefillArguments([]);

        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    /// <summary>OK で閉じたときの定義。</summary>
    public PromptDefinition Definition { get; private set; }

    /// <summary>インポートしたときのツールのパス。設定ページはこれでパス欄を置き換え、引数欄を ${prompt} だけにする（R-134）。</summary>
    public string? ImportedPath { get; private set; }

    internal bool CanUndo => _undo.CanUndo;

    /// <summary>編集できるテキストボックスにフォーカスがあるときの Ctrl+Z は、テキストボックス自身の取り消しに任せる（R-133）。</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Z) && ActiveControl is not TextBoxBase { ReadOnly: false })
        {
            Undo();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>定義を変える操作の、変更を確定する直前に呼ぶ（R-133）。タイトルと前回の入力の設定は積まない。</summary>
    private void PushUndo()
    {
        _undo.Push(_items, _arguments, ImportedPath);
        _undoButton.Enabled = true;
    }

    private void Undo()
    {
        if (!_undo.TryUndo(out var state)) return;
        _items.Clear();
        _items.AddRange(state.Items);
        _arguments.Clear();
        _arguments.AddRange(state.Arguments);
        ImportedPath = state.ImportedPath;
        _previewValues = null;   // 戻した定義には合わないので捨てる
        _undoButton.Enabled = _undo.CanUndo;
        RefillItems(-1);
        RefillArguments([]);
    }

    internal static string KindName(PromptItemKind kind) => kind switch
    {
        PromptItemKind.Text => "テキスト",
        PromptItemKind.Folder => "フォルダ",
        PromptItemKind.File => "ファイル",
        PromptItemKind.CheckBox => "チェックボックス",
        _ => "ドロップダウンリスト",
    };

    private static ListView NewList(Rectangle bounds, bool multiSelect) => new()
    {
        View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = multiSelect, Bounds = bounds,
    };

    private static Button Side(string text, int y) => new() { Text = text, Bounds = new Rectangle(498, y, 104, 28) };

    private PromptDefinition Current() => new()
    {
        Title = InputText.TrimEdge(_title.Text),   // B-01: 全角空白は名前の一部なので落とさない
        RememberLast = _remember.Checked,
        Items = [.. _items],
        Arguments = [.. _arguments],
    };

    // ---- 表示 ----------------------------------------------------------

    private void RefillItems(int selectIndex)
    {
        _itemList.BeginUpdate();
        _itemList.Items.Clear();
        foreach (var item in _items)
        {
            var initial = item.Kind switch
            {
                PromptItemKind.CheckBox => item.InitialChecked ? "オン" : "オフ",
                PromptItemKind.DropDown => item.Choices.FirstOrDefault(c => c.Id == item.InitialChoiceId)?.Label ?? "",
                _ => item.Initial.Length > 0 ? item.Initial : "（空欄）",
            };
            _itemList.Items.Add(new ListViewItem([KindName(item.Kind), item.Label, initial]));
        }
        _itemList.EndUpdate();
        if (selectIndex >= 0 && selectIndex < _itemList.Items.Count) _itemList.Items[selectIndex].Selected = true;
        UpdateButtons();
        RenderExample();
    }

    private void RefillArguments(IReadOnlyList<int> select)
    {
        _argumentList.BeginUpdate();
        _argumentList.Items.Clear();
        foreach (var argument in _arguments)
        {
            var (kind, content, mark) = argument.Kind switch
            {
                PromptArgumentKind.Fixed => ("固定", argument.Text.Length == 0 ? "\"\"" : argument.Text, PromptImport.Mark(argument.Text) switch
                {
                    ArgumentMark.Switch => "スイッチ",
                    ArgumentMark.Path => "パス",
                    _ => "",
                }),
                PromptArgumentKind.Template => ("マクロ", argument.Text, ""),
                _ => ("項目", _items.FirstOrDefault(i => i.Id == argument.ItemId)?.Label ?? "（無い項目）", ""),
            };
            _argumentList.Items.Add(new ListViewItem([kind, content, mark]));
        }
        _argumentList.EndUpdate();
        foreach (var index in select.Where(i => i >= 0 && i < _argumentList.Items.Count)) _argumentList.Items[index].Selected = true;
        UpdateButtons();
        RenderExample();
    }

    private void RenderExample() =>
        _example.Text = PromptExample.Render(Current(), _previewValues ?? PromptAnswerRules.Defaults(Current()));

    private int SelectedItemIndex() => _itemList.SelectedIndices.Count == 1 ? _itemList.SelectedIndices[0] : -1;

    private int SelectedItemId() => SelectedItemIndex() is var i and >= 0 ? _items[i].Id : -1;

    private List<int> SelectedArguments() => [.. _argumentList.SelectedIndices.Cast<int>().Order()];

    private void UpdateButtons()
    {
        var item = SelectedItemIndex();
        _itemAdd.Enabled = _items.Count < PromptDefinitionRules.MaxItems;
        _itemEdit.Enabled = _itemDelete.Enabled = item >= 0;
        _itemUp.Enabled = item > 0;
        _itemDown.Enabled = item >= 0 && item < _items.Count - 1;

        // 2 行以上を選んでいる間は、行ごとの操作を止める（まとめるだけを出す。R-134）
        var selected = SelectedArguments();
        var single = selected.Count == 1 ? selected[0] : -1;
        _argumentEdit.Enabled = _argumentDelete.Enabled = single >= 0;
        _argumentUp.Enabled = single > 0;
        _argumentDown.Enabled = single >= 0 && single < _arguments.Count - 1;
        _convert.Enabled = selected.Count >= 2 || (single >= 0 && _arguments[single].Kind == PromptArgumentKind.Fixed);
    }

    // ---- 項目 ----------------------------------------------------------

    private ContextMenuStrip KindMenu()
    {
        var menu = new ContextMenuStrip();
        foreach (var kind in Enum.GetValues<PromptItemKind>())
            menu.Items.Add(KindName(kind), null, (_, _) => AddItem(kind));
        return menu;
    }

    private void AddItem(PromptItemKind kind)
    {
        var draft = new PromptItem { Id = Current().NextItemId(), Kind = kind };
        using var dialog = new PromptItemDialog(draft);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        PushUndo();
        _items.Add(dialog.Result);
        _arguments.Add(PromptArgument.Item(dialog.Result.Id));   // 引数の末尾にも入れる（R-133）
        _previewValues = null;
        RefillArguments([]);
        RefillItems(_items.Count - 1);
    }

    private void EditItem(int id)
    {
        var index = _items.FindIndex(i => i.Id == id);
        if (index < 0) return;
        using var dialog = new PromptItemDialog(_items[index]);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (PromptEditHistory.SameItem(dialog.Result, _items[index])) return;   // 何も変わらなければ積まない
        PushUndo();
        _items[index] = dialog.Result;
        _previewValues = null;
        RefillArguments(SelectedArguments());
        RefillItems(index);
    }

    private void DeleteItem()
    {
        var index = SelectedItemIndex();
        if (index < 0) return;
        var id = _items[index].Id;
        PushUndo();
        _items.RemoveAt(index);
        _arguments.RemoveAll(a => a.Kind == PromptArgumentKind.Item && a.ItemId == id);   // 項目を消したら、その行も消す（R-133）
        _previewValues = null;
        RefillArguments([]);
        RefillItems(Math.Min(index, _items.Count - 1));
    }

    private void MoveItem(int delta)
    {
        var index = SelectedItemIndex();
        var to = index + delta;
        if (index < 0 || to < 0 || to >= _items.Count) return;
        PushUndo();
        (_items[index], _items[to]) = (_items[to], _items[index]);
        RefillItems(to);
    }

    // ---- 引数 ----------------------------------------------------------

    private ContextMenuStrip ArgumentMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("固定の引数...", null, (_, _) => AddArgument(PromptArgumentKind.Fixed));
        menu.Items.Add("マクロを含む引数...", null, (_, _) => AddArgument(PromptArgumentKind.Template));
        var item = new ToolStripMenuItem("項目") { Enabled = _items.Count > 0 };
        foreach (var each in _items)
        {
            var id = each.Id;
            var entry = new ToolStripMenuItem(each.Label);
            entry.Click += (_, _) => InsertArgument(PromptArgument.Item(id));
            item.DropDownItems.Add(entry);
        }
        menu.Items.Add(item);
        return menu;
    }

    private void AddArgument(PromptArgumentKind kind)
    {
        using var dialog = new PromptArgumentDialog(kind, "");
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        InsertArgument(kind == PromptArgumentKind.Fixed ? PromptArgument.Fixed(dialog.Value) : PromptArgument.Template(dialog.Value));
    }

    /// <summary>選んでいる行の次に入れる。選んでいなければ末尾。</summary>
    private void InsertArgument(PromptArgument argument)
    {
        var selected = SelectedArguments();
        var at = selected.Count == 1 ? selected[0] + 1 : _arguments.Count;
        PushUndo();
        _arguments.Insert(at, argument);
        RefillArguments([at]);
    }

    private void EditArgument()
    {
        if (SelectedArguments() is not [var index]) return;
        var argument = _arguments[index];
        if (argument.Kind == PromptArgumentKind.Item)
        {
            EditItem(argument.ItemId);
            return;
        }
        using var dialog = new PromptArgumentDialog(argument.Kind, argument.Text);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (dialog.Value == argument.Text) return;
        PushUndo();
        _arguments[index] = argument with { Text = dialog.Value };
        RefillArguments([index]);
    }

    private void DeleteArgument()
    {
        if (SelectedArguments() is not [var index]) return;
        PushUndo();
        _arguments.RemoveAt(index);
        RefillArguments([Math.Min(index, _arguments.Count - 1)]);
    }

    private void MoveArgument(int delta)
    {
        if (SelectedArguments() is not [var index]) return;
        var to = index + delta;
        if (to < 0 || to >= _arguments.Count) return;
        PushUndo();
        (_arguments[index], _arguments[to]) = (_arguments[to], _arguments[index]);
        RefillArguments([to]);
    }

    private async Task ShowConvertMenuAsync()
    {
        var selected = SelectedArguments();
        var menu = new ContextMenuStrip();
        if (selected.Count >= 2)
        {
            var merge = new ToolStripMenuItem("チェックボックスにまとめる...") { Enabled = PromptImport.CanMerge(Current(), selected) };
            merge.Click += (_, _) => Merge(selected);
            menu.Items.Add(merge);
        }
        else if (selected is [var index] && _arguments[index].Kind == PromptArgumentKind.Fixed)
        {
            var value = _arguments[index].Text;
            // パスの存在は押した時点で確かめる。ネットワークのパスで待つことがあるので、画面のスレッドでは調べない（N-05）
            PathKind? kind = null;
            if (PromptImport.Mark(value) == ArgumentMark.Path)
            {
                var folder = _currentFolder;
                kind = await Task.Run(() => PathResolver.Resolve(folder, value) is not { } resolved ? PathKind.Missing
                    : File.Exists(resolved) ? PathKind.File
                    : Directory.Exists(resolved) ? PathKind.Folder
                    : PathKind.Missing);
                if (IsDisposed || index >= _arguments.Count || _arguments[index].Text != value) return;   // 調べている間に変わった
            }
            foreach (var candidate in PromptImport.Candidates(value, kind, _items.Count))
            {
                var target = candidate.Target;
                menu.Items.Add(candidate.Label, null, (_, _) => Convert(index, target));
            }
            if (menu.Items.Count == 0) menu.Items.Add(new ToolStripMenuItem("変換できません") { Enabled = false });
        }
        else
        {
            return;
        }
        menu.Show(_convert, new Point(0, _convert.Height));
    }

    private void Convert(int index, ConversionTarget target)
    {
        var converted = PromptImport.Convert(Current(), index, target);
        PushUndo();
        Replace(converted);
        RefillItems(-1);
        RefillArguments([index]);
    }

    private void Merge(IReadOnlyList<int> indices)
    {
        var definition = Current();
        using var dialog = new PromptItemDialog(PromptImport.MergeDraft(definition, indices));
        if (dialog.ShowDialog(this) != DialogResult.OK) return;   // キャンセルなら何も変えない
        PushUndo();
        Replace(PromptImport.ApplyMerge(definition, indices, dialog.Result));
        RefillItems(-1);
        RefillArguments([indices.Min()]);
    }

    private void Replace(PromptDefinition definition)
    {
        _items.Clear();
        _items.AddRange(definition.Items);
        _arguments.Clear();
        _arguments.AddRange(definition.Arguments);
        _previewValues = null;
    }

    // ---- インポート・プレビュー・OK ----------------------------------------

    private void Import()
    {
        using var input = new TextInputDialog("インポート",
            $"コマンドラインを貼り付けてください。{Environment.NewLine}パスと引数は、貼り付けたコマンドで置き換わります。", "",
            text => PromptImport.Parse(text) is null ? "コマンドラインを読み取れません。引用符（\"）が閉じているか確かめてください。" : null);
        if (input.ShowDialog(this) != DialogResult.OK || PromptImport.Parse(input.Value) is not { } command) return;
        if ((_items.Count > 0 || _arguments.Count > 0)
            && MessageBox.Show(this, "今の項目と引数を置き換えます。よろしいですか。", "ReTAC", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;

        PushUndo();
        Replace(new PromptDefinition { Arguments = [.. command.Arguments] });
        ImportedPath = command.Path;
        RefillItems(-1);
        RefillArguments([]);
    }

    private void Preview()
    {
        var definition = Current();
        if (PromptDefinitionRules.Validate(definition) is [var first, ..])
        {
            ShowProblem(first);
            return;
        }
        // 起動はしない。履歴と前回の値は書かない（R-133）
        using var dialog = new PromptDialog(definition, _toolName, _history, _quickAccess, _currentFolder, PromptAnswerRules.Initial(definition));
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _previewValues = dialog.Values;
        RenderExample();
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK) return;
        var definition = Current();
        if (PromptDefinitionRules.Validate(definition) is [var first, ..])
        {
            ShowProblem(first);
            e.Cancel = true;
            return;
        }
        // R-132: オフにしたら、そのツールの前回の値をすべて消す
        Definition = definition.RememberLast ? definition : PromptAnswerRules.ForgetLast(definition);
    }

    private void ShowProblem(PromptProblem problem)
    {
        MessageBox.Show(this, problem.Message, "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        if (problem.ItemId is { } id && _items.FindIndex(i => i.Id == id) is var index and >= 0)
        {
            _itemList.SelectedIndices.Clear();
            _itemList.Items[index].Selected = true;
            _itemList.Focus();
        }
        else if (problem.ArgumentIndex is { } row && row < _argumentList.Items.Count)
        {
            _argumentList.SelectedIndices.Clear();
            _argumentList.Items[row].Selected = true;
            _argumentList.Focus();
        }
    }
}
