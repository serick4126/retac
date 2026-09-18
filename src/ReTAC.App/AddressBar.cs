using System.Drawing;
using System.Windows.Forms;
using ReTAC.Domain.Navigation;

namespace ReTAC.App;

/// <summary>
/// R-87: アドレスバー。今いるフォルダのフルパスを出し、クリック・`T`・`Ctrl+L` で編集を始める。
/// 扱うのはファイルシステムのパスだけ（R-39-2）。キーはダイレクトジャンプのダイアログと同じ。
/// 入力欄なので Ctrl+Z / C / X / V は入力欄の編集として働く（ファイル操作のキーはファイルリストだけが拾う）。
/// </summary>
public sealed class AddressBar : Control
{
    private readonly AddressBox _input = new()
    {
        BorderStyle = BorderStyle.None,
        // IME は有効のまま（日本語のフォルダ名を打つ。B-04 が切り離すのはファイルリストだけ）
        ImeMode = ImeMode.NoControl,
        // OS のフォルダ名補完（中で SHAutoComplete を呼ぶ）。候補が開いている間の ↑ ↓ Esc は補完が受け取り、
        // Enter は確定した後に届くので、こちらのキーとは衝突しない（試作で確認）
        AutoCompleteMode = AutoCompleteMode.SuggestAppend,
        AutoCompleteSource = AutoCompleteSource.FileSystemDirectories,
    };
    private readonly FolderHistory _history;
    private readonly QuickAccessList _quickAccess;
    private string _folder = "";
    private bool _editing;
    private bool _notFound;
    /// <summary>
    /// フォルダ参照のダイアログを出している間。ダイアログに移るときに LostFocus が来て、
    /// そのまま取り消すと、ダイアログを閉じたときに入力が消えている（試作で確認）。
    /// </summary>
    private bool _holding;
    /// <summary>クリックで編集を始めたとき、MouseUp のキャレット位置の設定より後に全選択する（R-46）。</summary>
    private bool _selectAllOnMouseUp;

    /// <summary>Enter（Explorer = false）/ Ctrl+Enter（true）。判定と移動は呼び出し側（JumpInput）。</summary>
    public event EventHandler<(string Text, bool Explorer)>? JumpRequested;

    /// <summary>Esc で取り消した。呼び出し側がファイルリストへフォーカスを返す。</summary>
    public event EventHandler? Cancelled;

    public AddressBar(FolderHistory history, QuickAccessList quickAccess)
    {
        _history = history;
        _quickAccess = quickAccess;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = SystemColors.Window;
        Controls.Add(_input);
        Height = BarHeight;

        // Enter ではなく GotFocus で見る。別のウィンドウから戻ったときは Enter が来ないが、
        // フォーカスが外れた時点で取り消しているので、戻ったら編集をやり直す
        _input.GotFocus += (_, _) =>
        {
            _editing = true;
            _selectAllOnMouseUp = MouseButtons != MouseButtons.None;
        };
        _input.MouseUp += (_, _) =>
        {
            if (!_selectAllOnMouseUp) return;
            _selectAllOnMouseUp = false;
            _input.SelectAll();
        };
        _input.KeyDown += OnInputKeyDown;
        // フォーカスの移り変わりの途中で表示を書き換えない。後に回す
        _input.LostFocus += (_, _) => { if (_editing && !_holding) BeginInvoke(CancelUnlessRefocused); };
        _input.TextChanged += (_, _) =>
        {
            if (!_notFound) return;
            _notFound = false;
            Invalidate();
        };
    }

    /// <summary>TopRow が行の高さを決めるのに使う。</summary>
    public int BarHeight => _input.PreferredHeight + LogicalToDeviceUnits(6);

    /// <summary>今いるフォルダが変わった。編集中は入力を上書きしない（自動更新でも呼ばれる）。</summary>
    public void ShowFolder(string folder)
    {
        _folder = folder;
        if (!_editing) ShowPath();
    }

    /// <summary>`T` / `Ctrl+L`。フォーカスを移して全体を選ぶ（R-46）。</summary>
    public bool BeginEdit()
    {
        _input.Focus();
        _input.SelectAll();
        return true;
    }

    /// <summary>見つからなかった。入力を残したまま枠を赤くする。次に入力を変えたら戻す。</summary>
    public void ShowNotFound()
    {
        _notFound = true;
        Invalidate();
    }

    /// <summary>
    /// ジャンプした。表示だけの状態に戻し、いったん今のフォルダを出す。移れたら ShowFolder で新しいパスに変わる。
    /// 入力のまま残すと、フォルダを開けなかったとき（応答しないドライブなど）に、居ない場所のパスが出続ける。
    /// </summary>
    public void EndEdit() => Cancel();

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyData)
        {
            case Keys.Enter:
                JumpRequested?.Invoke(this, (_input.Text, false));
                break;
            case Keys.Enter | Keys.Control:
                JumpRequested?.Invoke(this, (_input.Text, true));   // D-08
                break;
            case Keys.Enter | Keys.Shift:
                Browse();                                          // N-07
                break;
            case Keys.Escape:
                Cancel();
                Cancelled?.Invoke(this, EventArgs.Empty);
                break;
            default:
                if (e.Modifiers != Keys.None || !PathRecall.HandleKey(_input, e.KeyCode, _history, _quickAccess)) return;
                break;
        }
        e.Handled = e.SuppressKeyPress = true;
    }

    private void Browse()
    {
        _holding = true;
        try
        {
            if (FolderBrowser.Select(FindForm()!, _input.Text, _folder) is not { } selected) return;
            _input.Text = selected;   // R-52-3
            _input.SelectAll();
        }
        finally
        {
            _holding = false;
            _input.Focus();
        }
    }

    /// <summary>候補をマウスで選んだときなど、フォーカスが入力欄へ戻っていれば編集を続ける。</summary>
    private void CancelUnlessRefocused()
    {
        if (!_input.Focused) Cancel();
    }

    private void Cancel()
    {
        _editing = false;
        _notFound = false;
        ShowPath();
        Invalidate();
    }

    /// <summary>末尾を見せる。フォーカスが無くても末尾へスクロールし、外れてもそのまま（試作で確認）。</summary>
    private void ShowPath()
    {
        _input.Text = _folder;
        _input.Select(_folder.Length, 0);
        _input.ScrollToCaret();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(SystemColors.Window);
        // 赤は設定にしない（見つからないことを知らせる固定の表示）
        ControlPaint.DrawBorder(e.Graphics, ClientRectangle, _notFound ? Color.Red : SystemColors.ControlDark, ButtonBorderStyle.Solid);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var padding = LogicalToDeviceUnits(4);
        var height = _input.PreferredHeight;
        _input.SetBounds(padding, (Height - height) / 2, Math.Max(0, Width - padding * 2), height);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Height = BarHeight;
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Height = BarHeight;
    }

    /// <summary>↑↓・Enter・Esc を、フォームのフォーカス移動に取られずに KeyDown まで届ける。</summary>
    private sealed class AddressBox : TextBox
    {
        protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) switch
        {
            Keys.Up or Keys.Down or Keys.Enter or Keys.Escape => true,
            _ => base.IsInputKey(keyData),
        };
    }
}
