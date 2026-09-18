using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using ReTAC.Domain.Navigation;
using ReTAC.Shell;

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

    /// <summary>先頭のアイコンを左クリックした。今のフォルダをエクスプローラーで開く（ブラウザのアドレスバーの鍵の位置）。</summary>
    public event EventHandler? IconClicked;

    /// <summary>先頭のフォルダのアイコン。中身を読まない汎用の絵（応答しないドライブで待たない。N-05）。</summary>
    private Bitmap? _icon;
    private readonly ToolTip _tip = new();
    /// <summary>アイコンの上で左ボタンを押した位置。ここから動かしたらドラッグ、動かさずに離したらクリック。</summary>
    private Point? _iconPress;

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
        _tip.SetToolTip(this, "ドラッグしてブックマークに追加・クリックでエクスプローラーで開く・右クリックでメニュー");
        _input.KeyDown += OnInputKeyDown;
        _input.RecallKey = key => PathRecall.HandleKey(_input, key, _history, _quickAccess);
        // フォーカスの移り変わりの途中で表示を書き換えない。後に回す
        _input.LostFocus += (_, _) => { if (_editing && !_holding) BeginInvoke(CancelUnlessRefocused); };
        _input.TextChanged += (_, _) =>
        {
            if (!_notFound) return;
            _notFound = false;
            Invalidate();
        };
    }

    /// <summary>TopRow が行の高さを決めるのに使う。文字の上下にも余白を取る（ブラウザのアドレスバーと同じ。詰まって見えるという実機指摘）。</summary>
    public int BarHeight => _input.PreferredHeight + LogicalToDeviceUnits(12);

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
                return;   // ↑ ↓ は AddressBox.ProcessCmdKey が受け持つ
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

    private int IconSize => LogicalToDeviceUnits(16);
    private Rectangle IconBounds => new(LogicalToDeviceUnits(6), (Height - IconSize) / 2, IconSize, IconSize);
    private bool OnIcon(Point p) => p.X < IconBounds.Right + LogicalToDeviceUnits(2);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _iconPress = e.Button == MouseButtons.Left && OnIcon(e.Location) ? e.Location : null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_iconPress is not { } origin || e.Button != MouseButtons.Left) return;
        if (Math.Abs(e.X - origin.X) < SystemInformation.DragSize.Width
            && Math.Abs(e.Y - origin.Y) < SystemInformation.DragSize.Height) return;
        _iconPress = null;
        if (_folder.Length == 0) return;
        var data = new DataObject();
        data.SetFileDropList([_folder]);
        // リンクだけを許す。ファイルリストやエクスプローラーへ落としても、フォルダをコピー・移動させない
        DoDragDrop(data, DragDropEffects.Link);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var pressed = _iconPress is not null;
        _iconPress = null;
        if (!OnIcon(e.Location) || _folder.Length == 0) return;
        if (e.Button == MouseButtons.Left && pressed) IconClicked?.Invoke(this, EventArgs.Empty);
        else if (e.Button == MouseButtons.Right) ShellContextMenu.Show(Handle, [_folder], Cursor.Position.X, Cursor.Position.Y);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(SystemColors.Window);
        _icon ??= LoadIcon();
        if (_icon is not null) e.Graphics.DrawImage(_icon, IconBounds);
        // 赤は設定にしない（見つからないことを知らせる固定の表示）
        ControlPaint.DrawBorder(e.Graphics, ClientRectangle, _notFound ? Color.Red : SystemColors.ControlDark, ButtonBorderStyle.Solid);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var padding = LogicalToDeviceUnits(8);
        var height = _input.PreferredHeight;
        var left = IconBounds.Right + LogicalToDeviceUnits(6);
        _input.SetBounds(left, (Height - height) / 2, Math.Max(0, Width - left - padding), height);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Height = BarHeight;
    }

    private Bitmap? LoadIcon()
    {
        using var icons = new ShellIcons(IconSize);
        return icons.ForFolder() is { } b ? new Bitmap(b) : null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _icon?.Dispose(); _tip.Dispose(); }
        base.Dispose(disposing);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        _icon?.Dispose();
        _icon = null;   // 次の描画で大きさを合わせて取り直す
        Height = BarHeight;
    }

    /// <summary>↑↓・Enter・Esc を、フォームのフォーカス移動に取られずに KeyDown まで届ける。</summary>
    private sealed class AddressBox : TextBox
    {
        /// <summary>↑ ↓ で履歴・クイックアクセスの一覧を開く。開いたら true。</summary>
        public Func<Keys, bool>? RecallKey;

        protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) switch
        {
            Keys.Up or Keys.Down or Keys.Enter or Keys.Escape => true,
            _ => base.IsInputKey(keyData),
        };

        /// <summary>
        /// R-87: ↑ ↓ は、補完の候補が出ていなければ履歴・クイックアクセスの一覧にする（利用者の決定）。
        /// 補完は入力欄の窓をサブクラス化して KeyDown より先にキーを取り、候補が出ていなくても ↓ で候補を開いてしまう
        /// （c:\dev\cc で ↓ が cc_web になった）。ここはメッセージを配る前に呼ばれるので、補完より先に判定できる
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData is Keys.Up or Keys.Down && !IsSuggestionOpen() && RecallKey?.Invoke(keyData) == true) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>補完の候補の窓（自分のスレッドの "Auto-Suggest Dropdown"）が見えているか（試作で確認した判定）。</summary>
        private static bool IsSuggestionOpen()
        {
            var open = false;
            EnumThreadWindows(GetCurrentThreadId(), (window, _) =>
            {
                var name = new StringBuilder(32);
                GetClassName(window, name, name.Capacity);
                if (name.ToString() == "Auto-Suggest Dropdown" && IsWindowVisible(window)) open = true;
                return !open;
            }, IntPtr.Zero);
            return open;
        }

        private delegate bool EnumWindowsProc(IntPtr window, IntPtr param);

        [DllImport("user32.dll")]
        private static extern bool EnumThreadWindows(uint threadId, EnumWindowsProc callback, IntPtr param);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
    }
}
