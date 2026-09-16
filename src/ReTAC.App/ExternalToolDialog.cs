using System.Drawing;
using System.Windows.Forms;
using ReTAC.Domain.Tools;
using ReTAC.Shell;

namespace ReTAC.App;

/// <summary>
/// 外部ツールの設定（0x815A / F-04）。件数可変の一覧を編集する。
/// 知らないマクロは保存させない（予定 §7 C-1）。パスが見つからない・実行できない種類は注意だけ出す
/// （後でインストールする・関連付けのアプリで開くことを狙う使い方がある）。
/// </summary>
public sealed class ExternalToolDialog : Form
{
    private readonly List<ExternalTool> _tools;
    private readonly List<int> _removed = [];
    private readonly bool _suppressMultiple;
    private int _nextId;
    /// <summary>右の欄に出している項目。-1 なら無し</summary>
    private int _editing = -1;
    /// <summary>欄へ流し込む間は、変更を拾わない</summary>
    private bool _loading;
    private string _pathWarning = "";
    private bool _pathIsScript;

    private readonly ListBox _list = new() { Bounds = new Rectangle(14, 14, 220, 360), IntegralHeight = false };
    private readonly Button _add = new() { Text = "追加(&A)", Bounds = new Rectangle(14, 382, 106, 28) };
    private readonly Button _delete = new() { Text = "削除(&D)", Bounds = new Rectangle(128, 382, 106, 28) };
    private readonly Button _up = new() { Text = "上へ(&U)", Bounds = new Rectangle(14, 414, 106, 28) };
    private readonly Button _down = new() { Text = "下へ(&N)", Bounds = new Rectangle(128, 414, 106, 28) };

    private readonly TextBox _name = new() { Bounds = new Rectangle(250, 34, 490, 23) };
    private readonly TextBox _path = new() { Bounds = new Rectangle(250, 86, 390, 23) };
    private readonly Button _browse = new() { Text = "参照(&B)...", Bounds = new Rectangle(648, 85, 92, 26) };
    private readonly TextBox _arguments = new() { Bounds = new Rectangle(250, 138, 390, 23) };
    private readonly Button _macros = new() { Text = "マクロ(&R)...", Bounds = new Rectangle(648, 137, 92, 26) };
    // M-1: パス未検出・非実行種別・引数誤りが重なると 3 行以上になる。2 行分の 38 では欠ける
    private readonly Label _warning = new() { Bounds = new Rectangle(250, 166, 490, 56), ForeColor = Color.Firebrick };
    private readonly LinkLabel _scriptHelp = new() { Text = "実行できる書き方を見る(&H)", AutoSize = true, Location = new Point(250, 224), Visible = false };
    private readonly CheckBox _perItem = new() { Text = "マークした項目ごとに起動する(&L)", AutoSize = true, Location = new Point(250, 258) };
    private readonly CheckBox _popup = new() { Text = "ポップアップに表示する(&O)", AutoSize = true, Location = new Point(250, 286) };
    private readonly CheckBox _keepOpen = new() { Text = "終了後もウィンドウを閉じない(&W)", AutoSize = true, Location = new Point(250, 314) };
    private readonly CheckBox _confirm = new() { Text = "実行前に確認する(&C)", AutoSize = true, Location = new Point(250, 342) };
    private readonly ToolTip _tips = new();

    /// <param name="suppressMultiple">全体の設定「連続起動はしない」。ON なら「マークした項目ごとに起動する」は灰色（F-09）</param>
    public ExternalToolDialog(IReadOnlyList<ExternalTool> tools, int nextId, bool suppressMultiple)
    {
        _tools = [.. tools];
        // 設定ファイルは手で直せる（R-55）。次の番号が既存の番号以下だと、足したツールの番号が重なって別のツールを指す
        _nextId = Math.Max(nextId, _tools.Count == 0 ? DefaultExternalTools.FirstFreeId : _tools.Max(t => t.Id) + 1);
        _suppressMultiple = suppressMultiple;

        Text = "外部ツールの設定";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(754, 488);   // M-1: _warning を 2 行から 3 行分に広げた分だけ足す

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(550, 448, 90, 28) };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(650, 448, 90, 28) };

        // ラベルは入力欄の直前に足す。ニーモニック（&M など）はタブ順で次のコントロールへ移るため
        Controls.AddRange(
        [
            _list, _add, _delete, _up, _down,
            new Label { Text = "名前(&M):", AutoSize = true, Location = new Point(250, 14) }, _name,
            new Label { Text = "パス(&P):", AutoSize = true, Location = new Point(250, 66) }, _path, _browse,
            new Label { Text = "引数(&G):", AutoSize = true, Location = new Point(250, 118) }, _arguments, _macros,
            _warning, _scriptHelp, _perItem, _popup, _keepOpen, _confirm, ok, cancel,
        ]);
        OptionHelp.Attach(this, _tips,
            (_perItem, "ON: マークした数だけ、1 件ずつ順に起動する。前の 1 件が終わってから次を起動する。\nOFF: 1 回だけ起動し、マークした項目をまとめて引数に渡す。"),
            (_popup, "ON: コマンド「ポップアップメニューの表示」の「外部ツール ▶」に出る（既定のキーは G）。\nOFF: 出ない。メニュー「ツール」とキー割り当てからは、どちらでも起動できる。"),
            (_keepOpen, "ON: ツールが終わってもコンソールを閉じず、キーを押すまで結果を残す。\nOFF: 終わると同時に閉じる。\n\nコンソールを使わないツールには効かない。"),
            (_confirm, "ON: 実際に渡すコマンドラインを見せ、OK を押すまで起動しない。\nOFF: そのまま起動する。"));
        AcceptButton = ok;
        CancelButton = cancel;

        _list.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            Commit();
            ShowTool(_list.SelectedIndex);
        };
        _add.Click += (_, _) => AddTool();
        _delete.Click += (_, _) => DeleteTool();
        _up.Click += (_, _) => MoveTool(-1);
        _down.Click += (_, _) => MoveTool(1);
        _browse.Click += (_, _) => Browse();
        _macros.Click += (_, _) => InsertMacro(showScripts: false);
        _scriptHelp.LinkClicked += (_, _) => InsertMacro(showScripts: true);

        _name.TextChanged += (_, _) =>
        {
            if (_loading || _editing < 0) return;
            Commit();
            RenameInList();
        };
        _path.TextChanged += (_, _) => Commit();
        _path.Leave += (_, _) => _ = CheckPathAsync();
        _arguments.TextChanged += (_, _) => { Commit(); RenderWarning(); };
        foreach (var box in new[] { _perItem, _popup, _keepOpen, _confirm })
            box.CheckedChanged += (_, _) => Commit();

        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK) return;
            Commit();
            if (FirstProblem() is not { } problem) return;

            e.Cancel = true;
            RefillList(problem.Index);
            MessageBox.Show(this, problem.Message, "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };

        RefillList(_tools.Count > 0 ? 0 : -1);

        // C-1: ClientSize と Controls が揃ってから
        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    public IReadOnlyList<ExternalTool> Tools => _tools;
    public IReadOnlyList<int> RemovedToolIds => _removed;
    public int NextId => _nextId;

    private static string DisplayName(ExternalTool tool) => tool.Name.Length > 0 ? tool.Name : "（名前なし）";

    private void RefillList(int select)
    {
        _loading = true;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var tool in _tools) _list.Items.Add(DisplayName(tool));
        _list.EndUpdate();
        _list.SelectedIndex = select;   // -1 も可
        _loading = false;
        ShowTool(select);
    }

    /// <summary>名前を打つたびに一覧の表示を直す。選び直しの通知で欄を読み込み直さないよう _loading で止める。</summary>
    private void RenameInList()
    {
        _loading = true;
        var index = _editing;
        _list.Items[index] = DisplayName(_tools[index]);
        _list.SelectedIndex = index;
        _loading = false;
    }

    private void ShowTool(int index)
    {
        _loading = true;
        _editing = index;
        var tool = index >= 0 ? _tools[index] : null;

        _name.Text = tool?.Name ?? "";
        _path.Text = tool?.Path ?? "";
        _arguments.Text = tool?.Arguments ?? "";
        _perItem.Checked = tool?.LaunchPerItem ?? false;
        _popup.Checked = tool?.ShowInPopup ?? false;
        _keepOpen.Checked = tool?.KeepWindowOpen ?? false;
        _confirm.Checked = tool?.ConfirmBeforeRun ?? false;

        var editable = tool is not null;
        foreach (Control control in new Control[] { _name, _path, _browse, _arguments, _macros, _popup, _keepOpen, _confirm, _delete, _up, _down })
            control.Enabled = editable;
        _perItem.Enabled = editable && !_suppressMultiple;

        _loading = false;
        _ = CheckPathAsync();
    }

    private void Commit()
    {
        if (_loading || _editing < 0) return;
        _tools[_editing] = _tools[_editing] with
        {
            Name = _name.Text.Trim(),
            Path = _path.Text.Trim(),
            Arguments = _arguments.Text.Trim(),
            LaunchPerItem = _perItem.Checked,
            ShowInPopup = _popup.Checked,
            KeepWindowOpen = _keepOpen.Checked,
            ConfirmBeforeRun = _confirm.Checked,
        };
    }

    private void AddTool()
    {
        Commit();
        _tools.Add(new ExternalTool { Id = _nextId++, Name = "新しい外部ツール" });
        RefillList(_tools.Count - 1);
        _name.Focus();
        _name.SelectAll();
    }

    private void DeleteTool()
    {
        if (_editing < 0) return;
        var index = _editing;
        _removed.Add(_tools[index].Id);
        _tools.RemoveAt(index);
        _editing = -1;
        RefillList(Math.Min(index, _tools.Count - 1));
    }

    private void MoveTool(int delta)
    {
        Commit();
        var to = _editing + delta;
        if (_editing < 0 || to < 0 || to >= _tools.Count) return;
        (_tools[_editing], _tools[to]) = (_tools[to], _tools[_editing]);
        RefillList(to);
    }

    /// <summary>
    /// パスの注意。入力のたびには行わず、調べるのも裏のスレッドで行う（ネットワーク上のパスで待たされる・N-05）。
    /// 「見つからない」と「実行できない種類」は別々に判定して両方出す。
    /// </summary>
    private async Task CheckPathAsync()
    {
        var editing = _editing;
        var path = _path.Text.Trim();
        _pathWarning = "";
        _pathIsScript = false;
        RenderWarning();
        if (editing < 0 || path.Length == 0) return;

        var (found, script) = await Task.Run(() =>
        {
            var resolved = ExecutableResolver.Resolve(path);
            // 種類は拡張子で分かるので、まだ置いていないスクリプトでも案内を出す（予定 §1.4 A-4）
            var target = resolved ?? path;
            return (resolved is not null, System.IO.Path.HasExtension(target) && !ExecutableResolver.IsExecutableType(target));
        });

        // 調べている間に別のツールを選んだ・パスを打ち直したなら、この結果は捨てる
        if (IsDisposed || editing != _editing || path != _path.Text.Trim()) return;

        var messages = new List<string>();
        if (!found) messages.Add("パスが見つかりません（後でインストールする場合は、このまま保存できます）。");
        if (script) messages.Add("実行できる種類のファイルではありません。関連付けのアプリで開かれるだけで、実行はされません。");
        _pathWarning = string.Join(Environment.NewLine, messages);
        _pathIsScript = script;
        RenderWarning();
    }

    private void RenderWarning()
    {
        var messages = new List<string>();
        if (_pathWarning.Length > 0) messages.Add(_pathWarning);
        if (_editing >= 0) messages.AddRange(ArgumentTemplate.Parse(_arguments.Text).Errors.Select(e => e.Message));
        _warning.Text = string.Join(Environment.NewLine, messages);
        _scriptHelp.Visible = _pathIsScript;
    }

    private (int Index, string Message)? FirstProblem()
    {
        for (var i = 0; i < _tools.Count; i++)
        {
            var tool = _tools[i];
            if (tool.Name.Length == 0) return (i, "名前を入れてください。");

            var template = ArgumentTemplate.Parse(tool.Arguments);
            if (!template.IsValid)
                return (i, $"「{tool.Name}」の引数に誤りがあります。{Environment.NewLine}{string.Join(Environment.NewLine, template.Errors.Select(e => e.Message))}");
        }
        return null;
    }

    private void Browse()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "プログラム (*.exe;*.bat;*.cmd)|*.exe;*.bat;*.cmd|すべてのファイル (*.*)|*.*",
            FileName = _path.Text,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _path.Text = dialog.FileName;
        _ = CheckPathAsync();
    }

    private void InsertMacro(bool showScripts)
    {
        if (_editing < 0) return;
        using var dialog = new MacroReferenceDialog(showScripts);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is not { Insert: { } insert } entry) return;

        var at = _arguments.SelectionStart;
        _arguments.SelectedText = insert;   // 選んでいる範囲があれば置き換える
        _arguments.Focus();
        _arguments.SelectionStart = at + entry.CaretOffset;
        _arguments.SelectionLength = 0;
    }
}
