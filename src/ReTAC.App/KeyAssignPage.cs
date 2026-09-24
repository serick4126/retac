using System.Drawing;
using System.Windows.Forms;
using System.IO;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Keys;
using ReTAC.Domain.Tools;

namespace ReTAC.App;

/// <summary>
/// キー割り当ての設定（0x8155・5-2 節）。統合設定画面（R-102）のページ。ReTAC のキーの枠のそれぞれに、
/// 組み込みのコマンドか外部ツールを割り当てる。R-12: 実行時のキー解決はここで作った <see cref="KeyMap"/> の
/// 1 経路だけを通る（このページでは <see cref="SettingsDraft.KeyBindings"/> を直接編集する）。
///
/// コマンドは分類ごとにグループ化して並べ、右端に「今どのキーに付いているか」を出す。
/// 50 個超を 1 本のコンボから探すのは現実的でないため（実機指摘）。
/// 同じコマンドを複数のキーに割り当てられるので、キーは列挙して出す。
/// 名前の一部で絞り込める。割り当ては<b>ダブルクリック</b>で確定する。
/// 選ぶだけで割り当たると、誤クリックで設定が変わってしまうため（実機指摘）。
/// F-06: 登録した外部ツールは「登録した外部ツール」の分類に並ぶ。外部ツールページでの追加・改名・削除は
/// <see cref="SettingsDraft.ToolsChanged"/> で拾い、コマンドの一覧と枠の表示を作り直す（§2.4）。
/// </summary>
public sealed class KeyAssignPage : UserControl
{
    private readonly ListView _slots = new()
    {
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
        Bounds = new Rectangle(14, 40, 300, 380),
    };
    private readonly ListView _commands = new()
    {
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
        ShowGroups = true,
        Bounds = new Rectangle(330, 66, 410, 354),
    };
    private readonly TextBox _filter = new()
    {
        Bounds = new Rectangle(330, 36, 410, 23),
        PlaceholderText = "絞り込み（名前の一部を入力。「コピー」「外部ツール」など）",
    };

    private readonly SettingsDraft _draft;
    /// <summary>下書きそのもの（コピーではない）。ここでの割り当てはそのまま下書きに反映される。</summary>
    private readonly Dictionary<KeyBinding, CommandTarget?> _assignments;

    public KeyAssignPage(SettingsDraft draft)
    {
        _draft = draft;
        _assignments = draft.KeyBindings;

        AutoScaleMode = AutoScaleMode.Inherit;   // R-102-3: 拡大は SettingsDialog だけが行う
        Size = new Size(754, 496);   // 旧ダイアログのクライアント領域から OK/キャンセルの分を除いた大きさ（既定に戻すは残す）

        Controls.Add(new Label { Text = "キー／マウスボタンを選んで、右のコマンドを割り当てます。", AutoSize = true, Location = new Point(14, 14) });
        Controls.Add(new Label { Text = "割り当てるコマンド(&C):", AutoSize = true, Location = new Point(330, 14) });

        // 列幅は 96 DPI の値で持ち、枠が親に入って拡大を済ませた後に ApplyDpi で当てる（R-66。150% で文字が切れる）
        _slots.Columns.Add("キー／マウスボタン", 110);
        _slots.Columns.Add("コマンド", 166);   // 横スクロールバーが出ない幅に収める

        _commands.Columns.Add("コマンド", 250);
        _commands.Columns.Add("割り当て済み", 130);
        BuildCommandList();

        ReloadSlots(0);
        _slots.SelectedIndexChanged += (_, _) => SyncSelection();

        // 選ぶだけでは割り当てない。誤クリックで設定が変わらないようにする。
        // Enter は画面下端の OK に譲る
        _commands.DoubleClick += (_, _) => Assign();
        _filter.TextChanged += (_, _) => BuildCommandList();

        Controls.Add(new Label
        {
            Text = "コマンドをダブルクリックすると、左で選んでいる枠に割り当てます。",
            AutoSize = true,
            Location = new Point(14, 428),
        });

        var reset = new Button { Text = "既定に戻す(&D)", Bounds = new Rectangle(14, 468, 130, 28) };
        reset.Click += (_, _) =>
        {
            var defaults = DefaultKeyMap.Create();
            foreach (var slot in KeySlots.All)
            {
                var target = defaults.Resolve(slot);
                // 初期登録のツールを削除していたら、そのキーは空のままにする（F-01）
                _assignments[slot] = target is ToolTarget tool && _draft.ExternalTools.All(t => t.Id != tool.ToolId) ? null : target;
            }
            ReloadSlots(_slots.SelectedIndices.Count > 0 ? _slots.SelectedIndices[0] : 0);
        };

        var export = new Button { Text = "エクスポート(&E)...", Bounds = new Rectangle(154, 468, 110, 28) };
        export.Click += (_, _) => ExportKeyBindings();

        var import = new Button { Text = "インポート(&I)...", Bounds = new Rectangle(274, 468, 110, 28) };
        import.Click += (_, _) => ImportKeyBindings();

        Controls.AddRange([_slots, _commands, _filter, reset, export, import]);

        // 外部ツールの改名・削除は、コマンド一覧だけでなく枠一覧の表示（ラベル・未割り当ての灰色）にも出ている
        _draft.ToolsChanged += (_, _) =>
        {
            BuildCommandList();
            ReloadSlots(_slots.SelectedIndices.Count > 0 ? _slots.SelectedIndices[0] : 0);
        };
    }

    /// <summary>枠が <see cref="AutoScaleMode"/> を当てた直後・<see cref="Form.DpiChanged"/> のたびに枠から呼ぶ。</summary>
    public void ApplyDpi(int dpi)
    {
        _slots.Columns[0].Width = 110 * dpi / 96;
        _slots.Columns[1].Width = 166 * dpi / 96;
        _commands.Columns[0].Width = 250 * dpi / 96;
        _commands.Columns[1].Width = 130 * dpi / 96;
    }

    /// <summary>絞り込みの語で一覧を作り直す。分類名でも名前でも当たる。</summary>
    private void BuildCommandList()
    {
        CommandCatalog.Fill(_commands, _filter.Text, _draft.ExternalTools, includeUnassigned: true);
        RefreshAssignedKeys();
        SyncSelection();
    }

    /// <summary>右端の「割り当て済みのキー」を今の割り当てから作り直す。</summary>
    private void RefreshAssignedKeys()
    {
        foreach (ListViewItem item in _commands.Items)
        {
            // 同じコマンドを複数のキーに割り当てられる。キーの並びは枠の順
            var keys = item.Tag is CommandTarget target
                ? string.Join(" ", KeySlots.All.Where(slot => Equals(_assignments[slot], target)).Select(KeySlots.Display))
                : "";
            item.SubItems[1].Text = keys;
        }
    }

    private void ReloadSlots(int select)
    {
        _slots.BeginUpdate();
        _slots.Items.Clear();
        foreach (var slot in KeySlots.All)
        {
            var item = new ListViewItem([KeySlots.Display(slot), CommandLabels.Of(_assignments[slot], _draft.ExternalTools)]);
            // 未割り当ての行は薄くする。「(割り当てなし)」がコマンド名と同じ濃さで並ぶと、
            // どのキーが空いているか目で拾えない
            if (_assignments[slot] is null) item.ForeColor = SystemColors.GrayText;
            _slots.Items.Add(item);
        }
        _slots.EndUpdate();
        RefreshAssignedKeys();

        if (_slots.Items.Count == 0) return;
        var index = Math.Clamp(select, 0, _slots.Items.Count - 1);
        _slots.Items[index].Selected = true;
        _slots.Items[index].EnsureVisible();
        SyncSelection();
    }

    /// <summary>選んだキーに付いているコマンドを右の一覧で選び直す。</summary>
    private void SyncSelection()
    {
        if (_slots.SelectedIndices.Count == 0) return;
        var target = _assignments[KeySlots.All[_slots.SelectedIndices[0]]];

        foreach (ListViewItem item in _commands.Items)
        {
            var match = Equals(item.Tag, target);
            item.Selected = match;
            if (match) item.EnsureVisible();
        }
    }

    private void Assign()
    {
        // 分類の見出しをクリックしただけのときは項目が選ばれていない。何もしない
        if (_slots.SelectedIndices.Count == 0 || _commands.SelectedItems.Count == 0) return;

        var index = _slots.SelectedIndices[0];
        var slot = KeySlots.All[index];
        _assignments[slot] = (CommandTarget?)_commands.SelectedItems[0].Tag;

        _slots.Items[index].SubItems[1].Text = CommandLabels.Of(_assignments[slot], _draft.ExternalTools);
        _slots.Items[index].ForeColor = _assignments[slot] is null ? SystemColors.GrayText : _slots.ForeColor;
        RefreshAssignedKeys();
    }

    /// <summary>R-103-1: 下書きの割り当てをファイルに書き出す。書き込みは一時ファイル経由（V-07 と同じ理由）。</summary>
    private void ExportKeyBindings()
    {
        using var dialog = new SaveFileDialog
        {
            FileName = "retac.keys.json",
            Filter = "JSON ファイル (*.json)|*.json",
            DefaultExt = "json",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var json = KeyBindingFile.Export(_assignments);
            var temp = dialog.FileName + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, dialog.FileName, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "書き出せませんでした。", "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>1 MB を超えるファイルは読む前に弾く。読み込んだファイルの内容自体は信用しない入力（R-103-2）。</summary>
    private const long MaxImportFileBytes = 1_000_000;

    /// <summary>R-103-2: ファイルを読んで下書きの割り当てを丸ごと差し替える。壊れていれば何も変えない。</summary>
    private void ImportKeyBindings()
    {
        using var dialog = new OpenFileDialog { Filter = "JSON ファイル (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        string json;
        try
        {
            if (new FileInfo(dialog.FileName).Length > MaxImportFileBytes)
                throw new IOException("too large"); // メッセージは使わない。大きすぎる場合も「読み込めませんでした」に合流させる
            json = File.ReadAllText(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "読み込めませんでした。", "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var result = KeyBindingFile.Import(json, _assignments, _draft.ExternalTools.Select(t => t.Id));
        if (result is null)
        {
            MessageBox.Show(this, "読み込めませんでした。", "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        foreach (var slot in KeySlots.All) _assignments[slot] = result.Assignments[slot];
        ReloadSlots(_slots.SelectedIndices.Count > 0 ? _slots.SelectedIndices[0] : 0);

        MessageBox.Show(this, KeyBindingFile.FormatMessage(result), "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
