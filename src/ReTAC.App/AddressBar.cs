using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using ReTAC.Domain.Entries;
using ReTAC.Domain.Navigation;
using ReTAC.Shell;

namespace ReTAC.App;

/// <summary>
/// R-87: アドレスバー。`T`・`Ctrl+L`・余白のクリックで編集を始める。
/// R-94: 編集していないときはパンくず（段・`▸`・`…`）で出す。段のクリックでジャンプ、`▸` で子フォルダの一覧。フォーカスは取らない。
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
    /// <summary>▸ の一覧の中身。展開表示と同じ規則（今のソート・表示するファイルタイプ。IBookmarkHost.Enumerate）。</summary>
    private readonly Func<string, IReadOnlyList<Entry>> _enumerate;
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

    public AddressBar(FolderHistory history, QuickAccessList quickAccess, Func<string, IReadOnlyList<Entry>> enumerate)
    {
        _history = history;
        _quickAccess = quickAccess;
        _enumerate = enumerate;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        // R-94: 段・▸・… のクリックでフォーカスを奪わない（ファイルリストのまま）。編集は入力欄がフォーカスを持つ
        SetStyle(ControlStyles.Selectable, false);
        BackColor = SystemColors.Window;
        _input.Visible = false;   // 編集中だけ出す
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
        if (_editing) return;
        ShowPath();
        LayoutCrumbs();
    }

    /// <summary>
    /// `T` / `Ctrl+L` / 余白のクリック。フォーカスを移して全体を選ぶ（R-46）。
    /// 隠れた入力欄にはフォーカスが入らないので、先に出して並べ、今のパスを入れてから Focus する。
    /// </summary>
    public bool BeginEdit()
    {
        if (!_input.Visible)
        {
            ShowPath();
            _input.Visible = true;
            PerformLayout();
        }
        _input.Focus();
        _input.SelectAll();
        _selectAllOnMouseUp = false;   // 余白のクリックの MouseUp は入力欄に来ない。次のクリックで全選択し直さない
        Invalidate();
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
        _input.Visible = false;   // パンくずに戻す
        LayoutCrumbs();
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

    // ---- R-94: パンくず ------------------------------------------------------------

    private enum PartKind { Segment, Arrow, Ellipsis }

    /// <summary>描いた部品と当たり判定。Index は段の添字（… は畳んだ段の数）。</summary>
    private readonly record struct Part(PartKind Kind, int Index, Rectangle Bounds);

    private IReadOnlyList<BreadcrumbSegment> _segments = [];
    private List<Part> _parts = [];
    /// <summary>ホバーしている部品の添字（_parts）。先頭のアイコンなら IconHover、どちらでもなければ -1（余白）。</summary>
    private int _hover = -1;
    private const int IconHover = -2;

    private const string ArrowText = "▸";
    private const string EllipsisText = "…";
    private const TextFormatFlags CrumbFlags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

    private int CrumbPadding => LogicalToDeviceUnits(4);
    /// <summary>パンくずの左端（アイコンの右）。</summary>
    private int CrumbLeft => IconBounds.Right + LogicalToDeviceUnits(4);
    /// <summary>余白として必ず残す幅。すべての段が入らないときでも、ここを押せば編集を始められる。</summary>
    private int ReservedMargin => LogicalToDeviceUnits(24);

    private int TextWidth(string text) => TextRenderer.MeasureText(text, Font, Size.Empty, CrumbFlags).Width + CrumbPadding * 2;

    /// <summary>
    /// 段・▸・… の矩形を計算し直す。Resize・Font・DPI・フォルダの変更のたびに呼ぶ。
    /// 段の名前は 1 段あたりバーの幅の 1/3 までにし、末尾を … で省く（仕様書 §7.2）。
    /// </summary>
    private void LayoutCrumbs()
    {
        _segments = Breadcrumb.Split(_folder);
        _parts = [];
        _hover = -1;
        if (_segments.Count == 0 || Width <= 0) { Invalidate(); return; }

        var arrow = TextWidth(ArrowText);
        var ellipsis = TextWidth(EllipsisText);
        var cap = Math.Max(1, Width / 3);
        var names = _segments.Select(s => Math.Min(TextWidth(s.Name), cap)).ToList();
        // Breadcrumb.FirstShown の契約: 各幅は段の名前とその直後の ▸、available はパンくずに使える幅
        var available = Width - CrumbLeft - LogicalToDeviceUnits(8) - ReservedMargin;
        var first = Breadcrumb.FirstShown([.. names.Select(n => n + arrow)], ellipsis, available);

        var x = CrumbLeft;
        var top = LogicalToDeviceUnits(3);
        var height = Math.Max(0, Height - top * 2);
        if (first > 0)
        {
            _parts.Add(new Part(PartKind.Ellipsis, first, new Rectangle(x, top, ellipsis, height)));
            x += ellipsis;
        }
        var right = Width - LogicalToDeviceUnits(8);
        for (var i = first; i < _segments.Count; i++)
        {
            // 最後の段も入りきらないときは、残りの幅に切り詰める（矩形の幅を負にしない）
            var name = Math.Max(0, Math.Min(names[i], right - arrow - x));
            _parts.Add(new Part(PartKind.Segment, i, new Rectangle(x, top, name, height)));
            x += name;
            _parts.Add(new Part(PartKind.Arrow, i, new Rectangle(x, top, arrow, height)));
            x += arrow;
        }
        Invalidate();
    }

    private int PartAt(Point p) => _input.Visible ? -1 : _parts.FindIndex(part => part.Bounds.Contains(p));

    private string TooltipOf(int index) => index switch
    {
        IconHover => "ドラッグしてブックマークに追加・クリックでエクスプローラーで開く・右クリックでメニュー",
        < 0 => "クリックでアドレスを編集",
        _ => _parts[index] switch
        {
            { Kind: PartKind.Segment } part => _segments[part.Index].Path,
            { Kind: PartKind.Arrow } part => $"{_segments[part.Index].Name} のサブフォルダ",
            _ => "畳んだフォルダ",
        },
    };

    private void DrawCrumbs(Graphics g)
    {
        for (var i = 0; i < _parts.Count; i++)
        {
            var part = _parts[i];
            if (part.Bounds.Width <= 0) continue;
            if (i == _hover && _hover >= 0)
            {
                using var brush = new SolidBrush(SystemColors.ControlLight);
                g.FillRectangle(brush, part.Bounds);
            }
            var text = part.Kind switch
            {
                PartKind.Segment => _segments[part.Index].Name,
                PartKind.Arrow => ArrowText,
                _ => EllipsisText,
            };
            TextRenderer.DrawText(g, text, Font, part.Bounds, SystemColors.WindowText,
                CrumbFlags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>▸ の一覧。子フォルダだけを 1 段で並べ、クリックでジャンプ。次の段（今いる経路）は太字にし、そこが見えるように送る。</summary>
    private void ShowChildren(int index, Rectangle below)
    {
        var folder = _segments[index].Path;
        var next = index + 1 < _segments.Count ? _segments[index + 1].Path : null;
        var menu = NewMenu();
        var bold = new Font(menu.Font, FontStyle.Bold);
        menu.Disposed += (_, _) => bold.Dispose();
        var loading = BookmarkItems.Placeholder("読み込み中…");
        menu.Items.Add(loading);
        MenuSpacing.Apply(menu.Items, DeviceDpi);   // R-88

        FolderExpansion.Load(this, folder, _enumerate,
            // 閉じた後に届いた結果で書き換えない
            stillWanted: () => !menu.IsDisposed && menu.Visible,
            apply: (outcome, entries) =>
            {
                var folders = entries.Where(e => !e.IsParent && e.Kind == EntryKind.Folder).ToList();
                var items = new List<ToolStripItem>();
                ToolStripItem? current = null;
                foreach (var entry in folders.Take(FolderExpansion.MaxItems))
                {
                    var item = JumpItem(entry.Name, entry.FullPath);
                    if (next is not null && PathEquals(entry.FullPath, next))
                    {
                        item.Font = bold;
                        current = item;
                    }
                    items.Add(item);
                }
                if (folders.Count > FolderExpansion.MaxItems)
                    items.Add(JumpItem($"ほか {folders.Count - FolderExpansion.MaxItems} 件 — このフォルダへジャンプ", folder));
                if (items.Count == 0)
                    items.Add(BookmarkItems.Placeholder(outcome switch
                    {
                        FolderExpansion.LoadOutcome.Failed => "読み込めませんでした",
                        FolderExpansion.LoadOutcome.Missing => "見つかりません",
                        _ => "（サブフォルダなし）",
                    }));
                MenuSpacing.Apply(items, DeviceDpi);
                menu.SuspendLayout();
                try
                {
                    menu.Items.Remove(loading);
                    loading.Dispose();
                    menu.Items.AddRange([.. items]);
                }
                finally { menu.ResumeLayout(); }
                if (current is not null) ToolStripExtras.ScrollIntoView(menu, current);
            });
        menu.Show(this, new Point(below.Left, below.Bottom));
    }

    /// <summary>… の一覧。畳んだ段を近い順に。選ぶとそこへジャンプ。</summary>
    private void ShowCollapsed(int count, Rectangle below)
    {
        var menu = NewMenu();
        for (var i = count - 1; i >= 0; i--) menu.Items.Add(JumpItem(_segments[i].Name, _segments[i].Path));
        MenuSpacing.Apply(menu.Items, DeviceDpi);   // R-88
        menu.Show(this, new Point(below.Left, below.Bottom));
    }

    /// <summary>一度だけ使うメニュー。閉じたら項目ごと捨てる（開くたびに作るので、捨てないと GDI ハンドルが積み上がる）。</summary>
    private ContextMenuStrip NewMenu()
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };
        ToolStripExtras.EnableWheel(menu);
        menu.Closed += (_, _) => BeginInvoke(() =>
        {
            BookmarkItems.Clear(menu.Items);
            menu.Dispose();
        });
        return menu;
    }

    private ToolStripMenuItem JumpItem(string text, string path)
    {
        var item = new ToolStripMenuItem(text.Replace("&", "&&"));
        item.Click += (_, _) => JumpRequested?.Invoke(this, (path, false));
        return item;
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover == -1) return;
        _hover = -1;
        Invalidate();
    }

    // ---------------------------------------------------------------------------------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _iconPress = e.Button == MouseButtons.Left && OnIcon(e.Location) ? e.Location : null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hover = OnIcon(e.Location) ? IconHover : PartAt(e.Location);
        if (hover != _hover)
        {
            _hover = hover;
            _tip.SetToolTip(this, TooltipOf(hover));
            Invalidate();
        }
        if (_iconPress is not { } origin || e.Button != MouseButtons.Left) return;
        if (Math.Abs(e.X - origin.X) < SystemInformation.DragSize.Width
            && Math.Abs(e.Y - origin.Y) < SystemInformation.DragSize.Height) return;
        _iconPress = null;
        if (_folder.Length == 0) return;
        // シェルのデータで渡す（FileDrop も入っている）。FileDrop だけではエクスプローラーがショートカットを作らない。
        // リンクだけを許す。ファイルリストやエクスプローラーへ落としても、フォルダをコピー・移動させない
        var shell = ShellDataObject.For(_folder);
        object data = shell ?? FileDrop(_folder);
        try { DoDragDrop(data, DragDropEffects.Link); }
        finally
        {
            // シェルの資源を GC まで握らない（繰り返しドラッグすると溜まる）。DoDragDrop は同期なので、戻れば使い終わっている
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
        }
    }

    private static DataObject FileDrop(string path)
    {
        var files = new DataObject();
        files.SetFileDropList([path]);
        return files;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var pressed = _iconPress is not null;
        _iconPress = null;
        if (_folder.Length == 0) return;
        // 先頭のアイコン（9.2）を先に見る。段の当たり判定と競合させない
        if (OnIcon(e.Location))
        {
            if (e.Button == MouseButtons.Left && pressed) IconClicked?.Invoke(this, EventArgs.Empty);
            else if (e.Button == MouseButtons.Right) ShellContextMenu.Show(Handle, [_folder], Cursor.Position.X, Cursor.Position.Y);
            return;
        }
        if (e.Button != MouseButtons.Left || _input.Visible) return;
        if (PartAt(e.Location) is var index and >= 0)
        {
            var part = _parts[index];
            switch (part.Kind)
            {
                // 最後の段は今のフォルダの読み直しになる（JumpInput がそのまま開き直す）
                case PartKind.Segment: JumpRequested?.Invoke(this, (_segments[part.Index].Path, false)); break;
                case PartKind.Arrow: ShowChildren(part.Index, part.Bounds); break;
                case PartKind.Ellipsis: ShowCollapsed(part.Index, part.Bounds); break;
            }
            return;
        }
        BeginEdit();   // 余白
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(SystemColors.Window);
        _icon ??= LoadIcon();
        if (_icon is not null) e.Graphics.DrawImage(_icon, IconBounds);
        if (!_input.Visible) DrawCrumbs(e.Graphics);
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
        LayoutCrumbs();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Height = BarHeight;
        LayoutCrumbs();
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
        LayoutCrumbs();
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
