using System.Drawing;
using System.Windows.Forms;
using ReTAC.Domain.Navigation;

namespace ReTAC.App;

/// <summary>
/// パスを入力するダイアログ。§9.1 の 5 原則をこの 1 か所で満たす（T6-1）。
/// ① 使えるキー操作を画面に印字（R-47） ② プリセットは全選択（R-46）
/// ③ 意味のないプリセットはしない（R-46-4） ④ 一括指定を実行前に置く（R-41-6）
/// ⑤ 入力の誤りは中止せず入力欄に戻す（R-48。判断は呼び出し側）
///
/// T6-2: `↑` フォルダ履歴 / `↓` クイックアクセス / `Shift+Enter` フォルダ参照（N-07）。
/// 履歴は移動とコピー先で共通のひとつ（N-02）。
/// </summary>
public sealed class PathInputDialog : Form
{
    private readonly TextBox _input = new();
    private readonly CheckBox? _option;
    private readonly FolderHistory _history;
    private readonly QuickAccessList? _quickAccess;
    private readonly string _currentFolder;

    /// <param name="heading">見出し。「&lt;対象&gt; のコピー先は？」のように実効対象を示す</param>
    /// <param name="optionText">実行前の一括指定（R-41-6 の差分チェックボックスなど）。null なら置かない</param>
    /// <param name="secondaryText">「開く」のような 2 つめの確定ボタン。null なら置かない</param>
    public PathInputDialog(string caption, string heading, string preset,
                           FolderHistory history, QuickAccessList? quickAccess, string currentFolder,
                           string acceptText, string? secondaryText = null,
                           string[]? hints = null, string? optionText = null, bool optionChecked = false)
    {
        _history = history;
        _quickAccess = quickAccess;
        _currentFolder = currentFolder;

        Text = caption;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;   // ダイアログはタスクバーに出さない（既定は true）
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;

        var y = 12;
        if (heading.Length > 0)
        {
            // 長いファイル名は折り返す。行数ぶん下へずらさないと入力欄に重なる（実機指摘）
            var label = new Label { Text = heading, AutoSize = true, Location = new Point(14, y), MaximumSize = new Size(510, 0) };
            Controls.Add(label);
            // I-1: PreferredSize は現在の DPI（実ピクセル）で測る。後で自動拡大がもう一度掛かるので、
            // ここに積む座標は論理値（96 DPI）に戻しておく
            y += Math.Max(26, label.PreferredSize.Height * 96 / DeviceDpi + 8);
        }

        _input.SetBounds(12, y, 300, 23);
        _input.Text = preset;

        // マウスだけでも履歴とクイックアクセスへ行けるようにする（実機指摘）。
        // 一覧の中で区切り線で分ける。キーボードの ↑ ↓ は従来どおり別々に開く
        var recall = new Button { Text = "履歴 ▼(&H)", Bounds = new Rectangle(320, y - 1, 110, 26) };
        recall.Click += (_, _) => PathRecall.ShowBoth(_input, _history, _quickAccess);

        var browse = new Button { Text = "参照(&B)...", Bounds = new Rectangle(438, y - 1, 86, 26) };
        browse.Click += (_, _) => Browse();
        Controls.AddRange([_input, recall, browse]);
        y += 32;

        foreach (var hint in hints ?? [])
        {
            Controls.Add(new Label { Text = hint, AutoSize = true, Location = new Point(14, y) });
            y += 22;
        }

        if (optionText is not null)
        {
            // R-41-6: 一括指定は実行前に置く。衝突のたびに問う方式より優先される
            _option = new CheckBox { Text = optionText, AutoSize = true, Checked = optionChecked, Location = new Point(14, y + 4) };
            Controls.Add(_option);
            y += 30;
        }

        var accept = new Button { Text = acceptText, DialogResult = DialogResult.OK, Bounds = new Rectangle(246, y + 8, 90, 28) };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(434, y + 8, 90, 28) };
        Controls.AddRange([accept, cancel]);
        AcceptButton = accept;
        CancelButton = cancel;   // R-18: Esc は常に中止

        if (secondaryText is not null)
        {
            var secondary = new Button { Text = secondaryText, Bounds = new Rectangle(340, y + 8, 90, 28) };
            secondary.Click += (_, _) => Finish(secondaryChosen: true);
            Controls.Add(secondary);
        }

        ClientSize = new Size(536, y + 48);

        _input.KeyDown += OnInputKeyDown;
        Shown += (_, _) => { _input.Focus(); _input.SelectAll(); };   // R-46

        // C-1: AutoScaleMode の代入はその場で PerformAutoScale を走らせる。ClientSize と
        // Controls が揃ってからでないと、まだ 96 DPI のレイアウトを拡大できない
        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16: 座標と大きさは 96 DPI（100%）で書いてある
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    /// <summary>入力されたパス。相対パスの解決は呼び出し側で行う（R-61）。</summary>
    public string Path => InputText.TrimEdge(_input.Text);

    /// <summary>2 つめのボタン（「開く」）で確定した。</summary>
    public bool SecondaryChosen { get; private set; }

    /// <summary>一括指定のチェック状態。置いていなければ false。</summary>
    public bool OptionChecked => _option?.Checked ?? false;

    /// <summary>
    /// AcceptButton を置くと Enter は ProcessDialogKey の段階で確定ボタンに食われ、
    /// 入力欄の KeyDown には届かない。修飾つきの Enter はここで先に受ける。
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Enter | Keys.Shift:
                Browse();                                   // N-07
                return true;
            case Keys.Enter | Keys.Control when Controls.OfType<Button>().Any(b => b.Text.Contains("開く")):
                Finish(secondaryChosen: true);              // 16.3 節
                return true;
            default:
                return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (PathRecall.HandleKey(_input, e.KeyCode, _history, _quickAccess)) e.Handled = e.SuppressKeyPress = true;
    }

    private void Finish(bool secondaryChosen)
    {
        SecondaryChosen = secondaryChosen;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void Browse()
    {
        if (FolderBrowser.Select(this, Path, _currentFolder) is not { } selected) return;
        _input.Text = selected;   // R-52-3
        _input.SelectAll();
    }
}
