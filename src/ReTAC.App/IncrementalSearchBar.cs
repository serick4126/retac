using System.Drawing;
using System.Windows.Forms;
using ReTAC.Domain.Selection;

namespace ReTAC.App;

/// <summary>
/// R-80: インクリメンタルサーチの入力欄。ステータスバーの一段上に、検索中だけ出す。
/// 入力中はキーがこの欄に届くので、ファイルリストの 1 打鍵コマンドは働かない（Space もマークにならない。R-11）。
/// </summary>
public sealed class IncrementalSearchBar : FlowLayoutPanel
{
    /// <summary>一致が無いときの入力欄の背景。設定画面には置かない（B-05）。</summary>
    private static readonly Color NoMatchBackground = Color.FromArgb(255, 224, 224);

    private readonly FileListView _list;
    private readonly SearchBox _input = new() { Width = 240, ImeMode = ImeMode.NoControl };
    private readonly Label _result = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private IReadOnlyList<int> _matches = [];
    /// <summary>検索前のカーソルの項目。自動更新で添字がずれるので名前で覚える。</summary>
    private string? _originName;
    private bool _open;

    public IncrementalSearchBar(FileListView list)
    {
        _list = list;
        Dock = DockStyle.Bottom;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        WrapContents = false;
        Visible = false;

        // IME は有効のまま（B-04 が IME を切り離しているのはファイルリストのウィンドウだけ）。
        // 変換中は TextChanged が起きないので、確定した文字だけで探す
        Controls.Add(new Label { Text = "検索:", AutoSize = true, Anchor = AnchorStyles.Left });
        Controls.Add(_input);
        Controls.Add(_result);

        _input.TextChanged += (_, _) => OnTextChanged();
        _input.KeyDown += OnInputKeyDown;
        // フォーカスの切り替えの途中で隠したりフォーカスを動かしたりすると ActiveControl が乱れる。後に回す
        _input.LostFocus += (_, _) => { if (_open) BeginInvoke(() => Close(restore: false)); };
    }

    /// <summary>`Ctrl+F`。開いていれば入力済みの文字を全選択する。</summary>
    public bool Open()
    {
        if (!_open)
        {
            _open = true;
            _originName = _list.State.Cursor?.Name;
            _input.Text = "";
            _matches = [];
            ShowResult();
            Visible = true;
        }
        _input.Focus();
        _input.SelectAll();
        return true;
    }

    /// <summary>同じフォルダの再表示の後に呼ぶ。カーソルは再表示の規則に任せ、ここでは動かさない。</summary>
    public void Rematch()
    {
        if (!_open) return;
        _matches = IncrementalMatch.Find(_list.State.Entries, _input.Text);
        ShowResult();
    }

    /// <param name="restore">検索前の位置へ戻すか（Esc）。確定（Enter・フォーカスが外れた・別のフォルダへ移った）は false</param>
    public void Close(bool restore)
    {
        if (!_open) return;   // Enter / Esc で閉じた後に LostFocus が来る
        _open = false;
        if (restore) RestoreOrigin();
        Visible = false;
        // 隠した入力欄が ActiveControl に残ると、ウィンドウに戻ってきたときキーがどこにも届かない
        if (FindForm() is { } form) form.ActiveControl = _list;
    }

    private void OnTextChanged()
    {
        _matches = IncrementalMatch.Find(_list.State.Entries, _input.Text);
        if (_input.Text.Length == 0)
        {
            RestoreOrigin();
        }
        else if (IncrementalMatch.Pick(_matches, _list.State.CursorIndex) is var target and >= 0)
        {
            _list.MoveCursorTo(target);
        }
        ShowResult();
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Down or Keys.Up:
                var target = IncrementalMatch.Step(_matches, _list.State.CursorIndex, forward: e.KeyCode == Keys.Down);
                if (target >= 0) _list.MoveCursorTo(target);
                ShowResult();
                break;
            case Keys.Enter:
                Close(restore: false);   // 確定。項目は開かない
                break;
            case Keys.Escape:
                Close(restore: true);
                break;
            case Keys.F when e.Control:
                _input.SelectAll();      // TextBox は Ctrl+F で何もしない
                break;
            default:
                return;
        }
        e.Handled = true;
        e.SuppressKeyPress = true;       // 入力欄への文字の入力と警告音を止める
    }

    private void RestoreOrigin()
    {
        if (_originName is null) return;
        var entries = _list.State.Entries;
        for (var i = 0; i < entries.Count; i++)
        {
            if (!string.Equals(entries[i].Name, _originName, StringComparison.OrdinalIgnoreCase)) continue;
            _list.MoveCursorTo(i);
            return;
        }
    }

    private void ShowResult()
    {
        var noMatch = _input.Text.Length > 0 && _matches.Count == 0;
        _input.BackColor = noMatch ? NoMatchBackground : SystemColors.Window;
        var position = -1;
        for (var i = 0; i < _matches.Count; i++)
            if (_matches[i] == _list.State.CursorIndex) { position = i; break; }
        _result.Text = _input.Text.Length == 0 ? ""
                     : noMatch ? "一致なし"
                     : position >= 0 ? $"{_matches.Count} 件中 {position + 1} 件目"
                     : $"{_matches.Count} 件";
    }

    /// <summary>↑↓・Enter・Esc を、フォームのフォーカス移動やボタンに取られずに KeyDown まで届ける。</summary>
    private sealed class SearchBox : TextBox
    {
        protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) switch
        {
            Keys.Up or Keys.Down or Keys.Enter or Keys.Escape => true,
            _ => base.IsInputKey(keyData),
        };
    }
}
