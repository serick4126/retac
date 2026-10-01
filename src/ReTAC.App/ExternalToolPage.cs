using System.Drawing;
using System.Windows.Forms;
using ReTAC.Domain.Navigation;
using ReTAC.Domain.Tools;
using ReTAC.Shell;

namespace ReTAC.App;

/// <summary>
/// 外部ツールの設定（0x815A / F-04）。統合設定画面（R-102）のページ。件数可変の一覧を編集する。
/// 知らないマクロは保存させない（F-02）。パスが見つからない・実行できない種類は注意だけ出す
/// （後でインストールする・関連付けのアプリで開くことを狙う使い方がある）。
///
/// 一覧・次の ID・削除は <see cref="SettingsDraft"/> へ直接向ける。
/// 確定前の検証は他のページと違いここだけ必要なので、<see cref="Validate"/> に残す。
/// </summary>
public sealed class ExternalToolPage : UserControl
{
    /// <summary>
    /// R-127: 実行ファイルの欄の文字列を、保存・確認に使う形にする。囲みの二重引用符（「パスのコピー」の形）と端の空白を落とす。
    /// 落とす空白は ASCII だけ（B-01）。<c>Trim()</c> は全角空白も落とし、名前の一部の全角空白を消して別のファイルを指してしまう。
    /// </summary>
    internal static string NormalizePath(string text) => InputText.Unquote(text);

    private readonly SettingsDraft _draft;
    /// <summary>右の欄に出している項目。-1 なら無し</summary>
    private int _editing = -1;
    /// <summary>欄へ流し込む間は、変更を拾わない</summary>
    private bool _loading;
    private readonly string _currentFolder;
    private readonly FolderHistory _history;
    private readonly QuickAccessList? _quickAccess;
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
    private readonly Button _promptEdit = new() { Text = "入力ダイアログ編集(&Q)...", Bounds = new Rectangle(250, 166, 180, 26) };
    private readonly Button _macros = new() { Text = "マクロ(&R)...", Bounds = new Rectangle(648, 137, 92, 26) };
    // M-1: パス未検出・非実行種別・引数誤りが重なると 3 行以上になる。2 行分の 38 では欠ける
    // R-108-4: Firebrick は暗い地では沈むので、ダークでは #FF8A80 にする。ハイコントラストでは OS の文字色（R-108-3）。
    // そのときは地も OS の Window にする。WindowText はページの地（Control）と組になる色ではなく、独自のハイコントラスト配色では読めないことがある
    private readonly Label _warning = new()
    {
        Bounds = new Rectangle(250, 198, 490, 56),
        BackColor = Program.StartupOs.HighContrast ? Program.StartupOs.Window : Color.Empty,   // Empty は親の地を引き継ぐ（今までどおり）
        ForeColor = Program.StartupOs.HighContrast ? Program.StartupOs.WindowText
            : Program.StartupOs.Dark ? Color.FromArgb(0xFF, 0x8A, 0x80)
            : Color.Firebrick,
    };
    private readonly LinkLabel _scriptHelp = new() { Text = "実行できる書き方を見る(&H)", AutoSize = true, Location = new Point(250, 256), Visible = false };
    private readonly CheckBox _perItem = new() { Text = "マークした項目ごとに起動する(&L)", AutoSize = true, Location = new Point(250, 290) };
    private readonly CheckBox _popup = new() { Text = "ポップアップに表示する(&O)", AutoSize = true, Location = new Point(250, 318) };
    private readonly CheckBox _keepOpen = new() { Text = "終了後もウィンドウを閉じない(&W)", AutoSize = true, Location = new Point(250, 346) };
    private readonly CheckBox _confirm = new() { Text = "実行前に確認する(&C)", AutoSize = true, Location = new Point(250, 374) };
    private readonly ToolTip _tips = new();

    public ExternalToolPage(SettingsDraft draft, string currentFolder = "", FolderHistory? history = null, QuickAccessList? quickAccess = null)
    {
        _draft = draft;
        _currentFolder = currentFolder;
        _history = history ?? new FolderHistory();   // テストと、履歴を持たない呼び出し元のため
        _quickAccess = quickAccess;

        // 設定ファイルは手で直せる（R-55）。次の番号が既存の番号以下だと、足したツールの番号が重なって別のツールを指す
        _draft.NextExternalToolId = Math.Max(_draft.NextExternalToolId,
            _draft.ExternalTools.Count == 0 ? DefaultExternalTools.FirstFreeId : _draft.ExternalTools.Max(t => t.Id) + 1);

        AutoScaleMode = AutoScaleMode.Inherit;   // R-102-3: 拡大は SettingsDialog だけが行う
        Size = new Size(754, 448);   // 旧ダイアログのクライアント領域から OK/キャンセルの行を除いた大きさ

        // ラベルは入力欄の直前に足す。ニーモニック（&M など）はタブ順で次のコントロールへ移るため
        Controls.AddRange(
        [
            _list, _add, _delete, _up, _down,
            new Label { Text = "名前(&M):", AutoSize = true, Location = new Point(250, 14) }, _name,
            new Label { Text = "パス(&P):", AutoSize = true, Location = new Point(250, 66) }, _path, _browse,
            new Label { Text = "引数(&G):", AutoSize = true, Location = new Point(250, 118) }, _arguments, _macros,
            _promptEdit,
            _warning, _scriptHelp, _perItem, _popup, _keepOpen, _confirm,
        ]);
        OptionHelp.Attach(this, _tips,
            (_perItem, "ON: マークした数だけ、1 件ずつ順に起動する。前の 1 件が終わってから次を起動する。\nOFF: 1 回だけ起動し、マークした項目をまとめて引数に渡す。"),
            (_popup, "ON: コマンド「ポップアップメニューの表示」の「外部ツール ▶」に出る（既定のキーは G）。\nOFF: 出ない。メニュー「ツール」とキー割り当てからは、どちらでも起動できる。"),
            (_keepOpen, "ON: ツールが終わってもコンソールを閉じず、キーを押すまで結果を残す。\nOFF: 終わると同時に閉じる。\n\nコンソールを使わないツールには効かない。"),
            (_confirm, "ON: 実際に渡すコマンドラインを見せ、OK を押すまで起動しない。\nOFF: そのまま起動する。"));

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
        _promptEdit.Click += (_, _) => EditPrompt();
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

        RefillList(_draft.ExternalTools.Count > 0 ? 0 : -1);
    }

    /// <summary>
    /// 今の下書きの内容で検証する。問題があれば該当行を選び直し、表示していたメッセージを返す（null なら確定してよい）。
    /// <see cref="ContainerControl.Validate()"/>（子コントロールの検証イベントを起こすだけの別物）と名前が衝突するので隠す。
    /// </summary>
    public new string? Validate()
    {
        Commit();
        if (FirstProblem() is { } problem)
        {
            RefillList(problem.Index);
            return problem.Message;
        }
        // R-133: 引数欄から ${prompt} を消したツールの定義は捨てる（使われない定義を設定ファイルに残さない）
        if (Tools.Any(Orphan)) _draft.ExternalTools = [.. Tools.Select(t => Orphan(t) ? t with { Prompt = null } : t)];
        return null;

        static bool Orphan(ExternalTool tool) => tool.Prompt is not null && !ArgumentTemplate.Parse(tool.Arguments).HasPrompt;
    }

    private List<ExternalTool> Tools => _draft.ExternalTools;

    private static string DisplayName(ExternalTool tool) => tool.Name.Length > 0 ? tool.Name : "（名前なし）";

    private void RefillList(int select)
    {
        _loading = true;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var tool in Tools) _list.Items.Add(DisplayName(tool));
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
        _list.Items[index] = DisplayName(Tools[index]);
        _list.SelectedIndex = index;
        _loading = false;
    }

    private void ShowTool(int index)
    {
        _loading = true;
        _editing = index;
        var tool = index >= 0 ? Tools[index] : null;

        _name.Text = tool?.Name ?? "";
        _path.Text = tool?.Path ?? "";
        _arguments.Text = tool?.Arguments ?? "";
        _perItem.Checked = tool?.LaunchPerItem ?? false;
        _popup.Checked = tool?.ShowInPopup ?? false;
        _keepOpen.Checked = tool?.KeepWindowOpen ?? false;
        _confirm.Checked = tool?.ConfirmBeforeRun ?? false;

        var editable = tool is not null;
        foreach (Control control in new Control[] { _name, _path, _browse, _arguments, _macros, _promptEdit, _perItem, _popup, _keepOpen, _confirm, _delete, _up, _down })
            control.Enabled = editable;

        _loading = false;
        _ = CheckPathAsync();
    }

    private void Commit()
    {
        if (_loading || _editing < 0) return;
        var tools = Tools;
        tools[_editing] = tools[_editing] with
        {
            Name = _name.Text.Trim(),
            Path = NormalizePath(_path.Text),
            Arguments = _arguments.Text.Trim(),
            LaunchPerItem = _perItem.Checked,
            ShowInPopup = _popup.Checked,
            KeepWindowOpen = _keepOpen.Checked,
            ConfirmBeforeRun = _confirm.Checked,
        };
        // 改名もキー割り当て・クイックアクセスページへ ToolsChanged で伝える（F-01 / INV-TOOLTARGET-FK）
        _draft.ExternalTools = tools;
    }

    private void AddTool()
    {
        Commit();
        var tools = Tools;
        var id = _draft.NextExternalToolId;
        _draft.NextExternalToolId = id + 1;
        tools.Add(new ExternalTool { Id = id, Name = "新しい外部ツール" });
        _draft.ExternalTools = tools;
        RefillList(tools.Count - 1);
        _name.Focus();
        _name.SelectAll();
    }

    private void DeleteTool()
    {
        if (_editing < 0) return;
        var index = _editing;
        var id = Tools[index].Id;
        _editing = -1;
        _draft.RemoveTool(id);   // F-01・INV-TOOLTARGET-FK: キー割り当て・クイックアクセスの下書きからも一貫して外す
        RefillList(Math.Min(index, Tools.Count - 1));
    }

    private void MoveTool(int delta)
    {
        Commit();
        var tools = Tools;
        var to = _editing + delta;
        if (_editing < 0 || to < 0 || to >= tools.Count) return;
        (tools[_editing], tools[to]) = (tools[to], tools[_editing]);
        _draft.ExternalTools = tools;
        RefillList(to);
    }

    /// <summary>
    /// パスの注意。入力のたびには行わず、調べるのも裏のスレッドで行う（ネットワーク上のパスで待たされる・N-05）。
    /// 「見つからない」と「実行できない種類」は別々に判定して両方出す。
    /// </summary>
    private async Task CheckPathAsync()
    {
        var editing = _editing;
        var path = NormalizePath(_path.Text);
        _pathWarning = "";
        _pathIsScript = false;
        RenderWarning();
        if (editing < 0 || path.Length == 0) return;

        var (found, script) = await Task.Run(() =>
        {
            var resolved = ExecutableResolver.Resolve(path);
            // 種類は拡張子で分かるので、まだ置いていないスクリプトでも案内を出す
            var target = resolved ?? path;
            return (resolved is not null, System.IO.Path.HasExtension(target) && !ExecutableResolver.IsExecutableType(target));
        });

        // 調べている間に別のツールを選んだ・パスを打ち直したなら、この結果は捨てる
        if (IsDisposed || editing != _editing || path != NormalizePath(_path.Text)) return;

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
        var tools = Tools;
        for (var i = 0; i < tools.Count; i++)
        {
            var tool = tools[i];
            if (tool.Name.Length == 0) return (i, "名前を入れてください。");

            var template = ArgumentTemplate.Parse(tool.Arguments);
            if (!template.IsValid)
                return (i, $"「{tool.Name}」の引数に誤りがあります。{Environment.NewLine}{string.Join(Environment.NewLine, template.Errors.Select(e => e.Message))}");
            if (template.HasPrompt && tool.Prompt is { } prompt && PromptDefinitionRules.Validate(prompt) is [var first, ..])
                return (i, $"「{tool.Name}」の入力ダイアログの設定に誤りがあります。{Environment.NewLine}{first.Message}");
        }
        return null;
    }

    private void Browse()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "プログラム (*.exe;*.bat;*.cmd)|*.exe;*.bat;*.cmd|すべてのファイル (*.*)|*.*",
            FileName = NormalizePath(_path.Text),
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

        // R-133: ${prompt} を選んだら、入力ダイアログ編集と同じに動く（既にあれば 2 つ目は入れずに編集する）
        if (insert == "${prompt}")
        {
            EditPrompt();
            return;
        }

        var at = _arguments.SelectionStart;
        _arguments.SelectedText = insert;   // 選んでいる範囲があれば置き換える
        _arguments.Focus();
        _arguments.SelectionStart = at + entry.CaretOffset;
        _arguments.SelectionLength = 0;
    }

    /// <summary>
    /// R-133: 入力ダイアログ編集。引数欄に ${prompt} が無ければ、OK でカーソル位置に挿入する。あれば定義を編集する。
    /// インポートしたときは、パス欄を貼り付けたパスに、引数欄全体を ${prompt} にする（R-134）。
    /// </summary>
    private void EditPrompt()
    {
        if (_editing < 0) return;
        Commit();
        var tool = Tools[_editing];
        var hasPrompt = ArgumentTemplate.Parse(_arguments.Text).HasPrompt;
        if (!hasPrompt && WithPrompt(_arguments.Text, _arguments.SelectionStart, _arguments.SelectionLength) is null)
        {
            // 組み立てた後で入れられないと分かると、作業が無駄になる。開く前に止める
            MessageBox.Show(this, "引用符（\"）の中には ${prompt} を入れられません。カーソルを引用符の外に置いてください。",
                "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _arguments.Focus();
            return;
        }

        using var dialog = new PromptSettingsDialog(DefinitionToEdit(tool, _arguments.Text), tool.Name, _history, _quickAccess, _currentFolder);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        // 先に定義を下書きへ入れる。欄の書き換えで走る Commit は with で他の欄を残すので、定義は消えない
        var tools = Tools;
        tools[_editing] = tools[_editing] with { Prompt = DefinitionToSave(dialog.Definition) };
        _draft.ExternalTools = tools;

        if (dialog.ImportedPath is { } path)
        {
            _path.Text = path;
            _arguments.Text = "${prompt}";
            _ = CheckPathAsync();
            return;
        }
        if (hasPrompt || WithPrompt(_arguments.Text, _arguments.SelectionStart, _arguments.SelectionLength) is not (string text, int caret)) return;
        _arguments.Text = text;
        _arguments.Focus();
        _arguments.SelectionStart = caret;
        _arguments.SelectionLength = 0;
    }

    /// <summary>R-130・R-133: ヘルパーの結果が空（項目 0・引数 0）なら定義なしとして保存する（タイトルなども捨てる）。</summary>
    internal static PromptDefinition? DefinitionToSave(PromptDefinition result) => result.IsEmpty() ? null : result;

    /// <summary>
    /// R-133: ヘルパーに渡す定義。引数欄に ${prompt} が無ければ渡さない（空で開く）。
    /// 引数欄から ${prompt} を消した直後は、設定ページを OK するまで下書きに定義が残っているため。
    /// </summary>
    internal static PromptDefinition? DefinitionToEdit(ExternalTool tool, string argumentsText) =>
        ArgumentTemplate.Parse(argumentsText).HasPrompt ? tool.Prompt : null;

    /// <summary>R-130: ${prompt} はそれだけで 1 つの引数。選んでいる範囲を置き換え、前後の文字とつながるときだけ半角空白を挟む。</summary>
    /// <returns>
    /// 挿入後の文字と、挿入した ${prompt} の直後の位置。カーソルが引用符の中にある・選んでいる範囲が引用符を含むときは null
    /// （引用符の中の ${prompt} は構文の誤り。空白を挟んでも引用符の中では区切りにならない）
    /// </returns>
    internal static (string Text, int Caret)? WithPrompt(string text, int start, int length)
    {
        var end = start + length;
        if (text[..start].Count(c => c == '"') % 2 == 1 || text[start..end].Contains('"')) return null;
        var before = start > 0 && !ArgumentSplitter.IsSeparator(text[start - 1]) ? " " : "";
        var after = end < text.Length && !ArgumentSplitter.IsSeparator(text[end]) ? " " : "";
        var inserted = before + "${prompt}" + after;
        return (text[..start] + inserted + text[end..], start + before.Length + "${prompt}".Length);
    }
}
