using System.Drawing;
using System.Windows.Forms;
using ReTAC.App.Rendering;
using ReTAC.Shell;

namespace ReTAC.App;

/// <summary>R-97: ドライブツリー(現在位置のドライブ/UNC共有だけがルート)とデスクトップツリー
/// (PC全体で単一・固定のルート)の両方を、ルートの決め方だけを切り替えて 1 つの型で持つ。</summary>
public sealed class DriveTreeView : Control, IMessageFilter
{
    private const long SelectionTimeoutMilliseconds = 15_000;
    private readonly NameSpaceTreeRootKind _rootKind;
    private readonly NameSpaceTreeHost _host = new();
    private readonly System.Windows.Forms.Timer _selectionWatchdog = new() { Interval = 100 };
    private readonly Label _errorMessage = new()
    {
        Dock = DockStyle.Fill,
        Padding = new Padding(12),
        TextAlign = ContentAlignment.MiddleCenter,
    };
    private readonly Button _retry = new()
    {
        Dock = DockStyle.Bottom,
        Height = 32,
        Text = "再試行",
    };
    private readonly Panel _failure = new() { Dock = DockStyle.Fill, Visible = false };

    private string? _requestedFolder;
    private string? _appliedFolder;
    private string? _appliedRoot;
    private ShellTreeVisibility _requestedVisibility;
    private ShellTreeVisibility _appliedVisibility;
    private bool _resetPending = true;
    private bool _hostCreated;
    private long _selectionDeadline;
    /// <summary>R-97-2: NSTC は別ウィンドウの子なので、押下位置は WM_PARENTNOTIFY で拾っておく。</summary>
    private Point? _pendingDownPoint;
    /// <summary>
    /// R-97-3 / Q83: 左ボタンを押した位置にあった実フォルダのパス。NSTCS_DISABLEDRAGDROP で
    /// NSTC 自身のドラッグ(既定の大きな画像。実機 NG)を止めた分、しきい値を超えて動いたら
    /// このパス 1 件だけを対象に自前で DoDragDrop を始める。仮想項目なら null のままでドラッグを始めない。
    /// </summary>
    private string? _dragCandidatePath;
    private Point _dragDownScreenPoint;

    private const int WM_PARENTNOTIFY = 0x0210;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_MOUSEMOVE = 0x0200;

    public event EventHandler<string>? FolderCommitted;
    public event EventHandler<ShellTreeDropEventArgs>? FilesDropped;
    /// <summary>Step5: Tab / Shift+Tab でツリーからファイルビューへ戻る合図。</summary>
    public event EventHandler? FocusFileViewRequested;
    /// <summary>R-97: パスを持たない項目を確定した(選択・展開はできるが、ファイル表示パネルは動かせない)。</summary>
    public event EventHandler? NoPathItemCommitted;
    /// <summary>R-97-3: Enter/Tab 以外のキー。ReTAC の割り当てがあれば Handled にして NSTC 既定の処理を止める。</summary>
    public event EventHandler<ShellTreeKeyEventArgs>? CommandKeyRequested;

    public string? SelectedFolder { get; private set; }

    public DriveTreeView(NameSpaceTreeRootKind rootKind = NameSpaceTreeRootKind.Drive)
    {
        _rootKind = rootKind;
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        _failure.Controls.Add(_errorMessage);
        _failure.Controls.Add(_retry);
        Controls.Add(_failure);

        _retry.Click += (_, _) =>
        {
            _resetPending = true;
            CreateOrApply();
        };
        _host.SelectionChanged += (_, e) =>
        {
            SelectedFolder = e.Path;
            if (_requestedFolder is { } requested && SamePath(e.Path, requested)) _selectionWatchdog.Stop();
        };
        _host.SynchronizationFailed += (_, e) =>
        {
            _selectionWatchdog.Stop();
            _resetPending = true;
            ShowFailure(e.Exception);
        };
        _host.FilesDropped += (_, e) => FilesDropped?.Invoke(this, e);
        _host.ItemClicked += OnTreeItemClicked;
        // R-97-2: Enter は確定のあとファイルビューへフォーカスを戻す（マウス確定はフォーカスを動かさない）
        _host.CommitRequested += (_, _) => { CommitSelectedFolder(); FocusFileViewRequested?.Invoke(this, EventArgs.Empty); };
        _host.TabPressed += (_, _) => FocusFileViewRequested?.Invoke(this, EventArgs.Empty);
        _host.KeyPressed += (_, e) => CommandKeyRequested?.Invoke(this, e);
        _selectionWatchdog.Tick += (_, _) => CheckSelectionTimeout();
    }

    /// <summary>NSTC の子ウィンドウが受けた WM_LBUTTONDOWN は、直接の親であるここへ通知が来る。</summary>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_PARENTNOTIFY && unchecked((int)(long)m.WParam & 0xFFFF) == WM_LBUTTONDOWN)
        {
            var lp = unchecked((int)(long)m.LParam);
            _pendingDownPoint = new Point(unchecked((short)(lp & 0xFFFF)), unchecked((short)((lp >> 16) & 0xFFFF)));
            // R-97-3 / Q83: 押した位置が実フォルダなら、後続の移動でドラッグを自前で始める候補にする。
            // 仮想項目(null)ならこのまま候補にしない(ドラッグ元にしない)
            _dragCandidatePath = _hostCreated ? _host.RealFolderAt(_pendingDownPoint.Value) : null;
            _dragDownScreenPoint = MousePosition;
        }
        base.WndProc(ref m);
    }

    /// <summary>
    /// R-97-3 / Q83: NSTCS_DISABLEDRAGDROP で NSTC 自身のドラッグを止めた分、しきい値を超えて動いたら
    /// ここで自前の DoDragDrop を始める(FileListView の R-78 と同じ、小さい画像付き)。
    /// アプリ全体のマウス移動を見る必要があるため IMessageFilter を使う(MouseButtonFilter と同じ理由)。
    /// </summary>
    bool IMessageFilter.PreFilterMessage(ref Message m)
    {
        if (_dragCandidatePath is not { } path) return false;
        if (m.Msg == WM_LBUTTONUP) { _dragCandidatePath = null; return false; }
        if (m.Msg != WM_MOUSEMOVE) return false;
        if (Control.FromChildHandle(m.HWnd) != this) return false;
        if (!Control.MouseButtons.HasFlag(MouseButtons.Left)) { _dragCandidatePath = null; return false; }

        var current = MousePosition;
        if (Math.Abs(current.X - _dragDownScreenPoint.X) < SystemInformation.DragSize.Width
            && Math.Abs(current.Y - _dragDownScreenPoint.Y) < SystemInformation.DragSize.Height)
            return false;

        _dragCandidatePath = null;
        StartDrag(path);
        return false;   // 移動そのものは通常どおり処理させる(消費しない)
    }

    private void StartDrag(string path)
    {
        var paths = new System.Collections.Specialized.StringCollection { path };
        var data = new DataObject();
        data.SetFileDropList(paths);
        using var image = TreeDragImage(path);
        // R-78 と同じ: 画像付きで始める(useDefaultDragImage:false)。既定の大きな画像は指す先を隠す(実機 NG)
        DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link,
            image, new Point(Scaled(8), Scaled(8)), useDefaultDragImage: false);
    }

    /// <summary>Q83: ドラッグ中にカーソルへ付ける小さな画像。ツリーは Theme を持たないのでシステム配色を使う。</summary>
    private Bitmap TreeDragImage(string path)
    {
        using var icons = new ShellIcons(Scaled(16));
        var icon = icons.ForPath(path);
        var text = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        if (text.Length == 0) text = path;   // ドライブルート等は末尾が空になる
        return DragImageRenderer.Render(icon, icons.Size, text, Font, SystemColors.WindowText, SystemColors.Window, Scaled(4));
    }

    private int Scaled(int logical) => logical * DeviceDpi / 96;

    /// <summary>
    /// R-97-2: 確定はマウスを離した時（OnItemClick）だけで行う。ドラッグへ移行した操作は自前の
    /// DoDragDrop がマウスを掴むため、ここへは来ない想定だが、押下位置との距離チェックを二重の確認として残す。
    /// </summary>
    private void OnTreeItemClicked(object? sender, ShellTreeClickEventArgs e)
    {
        var downPoint = _pendingDownPoint ?? PointToClient(MousePosition);
        _pendingDownPoint = null;

        // R-97-3: 右クリックなどは確定(ファイル表示パネルの移動)をしない。シェルメニューは NSTC 自身が出す
        if ((e.ClickType & ShellTreeClickType.ButtonMask) != ShellTreeClickType.Left) return;

        // R-97: パスを持たない項目もクリックの確定候補になる(選択・展開はできるが、ファイル表示パネルは動かせない)。
        var pending = new NameSpaceTreePolicy.PendingTreeClick(
            e.Path ?? "", downPoint,
            OnIconOrLabel: (e.HitTest & (ShellTreeHitTest.Icon | ShellTreeHitTest.Label)) != 0,
            IsDoubleClick: (e.ClickType & ShellTreeClickType.DoubleClick) != 0);

        if (!NameSpaceTreePolicy.ShouldCommit(pending, PointToClient(MousePosition), dragStarted: false, buttonReleased: true))
            return;

        if (e.Path is { } path) FolderCommitted?.Invoke(this, path);
        else NoPathItemCommitted?.Invoke(this, EventArgs.Empty);
    }

    public void SyncCurrentFolder(string currentFolder, ShellTreeVisibility visibility, bool resetExpansion)
    {
        _ = NameSpaceTreePolicy.RootOf(currentFolder);
        if (!SamePath(_requestedFolder, currentFolder) || _requestedVisibility != visibility || resetExpansion)
            SelectedFolder = null;
        _requestedFolder = currentFolder;
        _requestedVisibility = visibility;
        _resetPending |= resetExpansion;
        if (_hostCreated) ApplyRequestedFolder();
    }

    /// <summary>R-97-2: WinForms の Control.Focus() はこの外側の窓に止まり、NSTC はキーを受け取れない。</summary>
    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        if (_hostCreated && !_failure.Visible) _host.Focus();
    }

    /// <summary>
    /// R-97-2: NSTC の窓は WinForms のコントロールではないので、その窓宛てのキーも前処理はここを通る。
    /// 入力キー扱いにしないと、矢印はフォームの矢印キー移動（上端の選択欄へ飛ぶ）、Tab / Shift+Tab は
    /// 既定のタブ移動に取られて NSTC まで届かない。Enter / Tab の扱いは NSTC の OnKeyboardInput が決める。
    /// Alt 付きはメニューのために通常どおり流す。
    /// </summary>
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.Alt) == 0 || base.IsInputKey(keyData);

    protected override bool IsInputChar(char charCode) => true;

    /// <summary>
    /// R-97-2: Tab / Enter の WM_KEYDOWN は NSTC の OnKeyboardInput で処理済みだが、続く WM_CHAR が
    /// SysTreeView32 まで届くと、ツリーが扱えない文字として警告音を鳴らす。文字の側だけここで捨てる。
    /// </summary>
    public override bool PreProcessMessage(ref Message msg)
    {
        // Esc などほかの制御文字も、ツリーでは警告音になるだけなので同じく捨てる
        if (msg.Msg == WM_CHAR && (int)(long)msg.WParam < ' ') return true;
        return base.PreProcessMessage(ref msg);
    }

    private const int WM_CHAR = 0x0102;

    protected override void OnHandleCreated(EventArgs e)

    {
        base.OnHandleCreated(e);
        Application.AddMessageFilter(this);   // Q83: 自前のドラッグ開始検出(ハンドル再生成のたびに掛け直す)
        CreateOrApply();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        Application.RemoveMessageFilter(this);
        _dragCandidatePath = null;
        if (_hostCreated)
        {
            _selectionWatchdog.Stop();
            _host.Dispose();
            _hostCreated = false;
        }
        _appliedFolder = null;
        _appliedRoot = null;
        SelectedFolder = null;
        _resetPending = true;
        _pendingDownPoint = null;
        base.OnHandleDestroyed(e);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (_hostCreated) ResizeHost();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        if (_hostCreated) ResizeHost();
    }

    internal void CommitSelectedFolder()
    {
        if (SelectedFolder is { } path) FolderCommitted?.Invoke(this, path);
        else if (_host.HasSelectionWithoutPath) NoPathItemCommitted?.Invoke(this, EventArgs.Empty);
    }


    private void CreateOrApply()
    {
        if (!IsHandleCreated) return;
        try
        {
            if (!_hostCreated)
            {
                _host.Create(Handle, ClientRectangle);
                _hostCreated = true;
            }
            ApplyRequestedFolder();
        }
        catch (Exception ex)
        {
            ShowFailure(ex);
        }
    }

    private void ApplyRequestedFolder()
    {
        if (_requestedFolder is not { } folder) return;
        try
        {
            var root = NameSpaceTreePolicy.RootOf(_rootKind, folder);
            var rootChanged = _appliedRoot is not null
                && NameSpaceTreePolicy.MustRebuildRoot(_appliedRoot, root);
            var rebuild = _resetPending || _appliedRoot is null || rootChanged
                || _appliedVisibility != _requestedVisibility;

            if (rebuild)
            {
                // RemoveAllRoots は非同期列挙中の古いルートを残す場合があるため、別ルートへの移動では NSTC 自体を作り直す。
                if (rootChanged)
                {
                    _host.Dispose();
                    _hostCreated = false;
                    _host.Create(Handle, ClientRectangle);
                    _hostCreated = true;
                }
                if (_rootKind == NameSpaceTreeRootKind.Drive) _host.SetRoot(root, folder, _requestedVisibility);
                else _host.SetDesktopRoot(folder, _requestedVisibility);
            }
            else if (!SamePath(_appliedFolder, folder))
            {
                _host.SelectPath(folder);
            }
            else
            {
                // R-97-2: 同じ現在位置での再同期。利用者がキーで選択を動かしていることがあるので、
                // 展開がまだ終わっていないときだけ見張る。常に見張ると、動かした選択を「展開の失敗」と取り違える
                if (_host.SelectionPending) StartSelectionWatchdog(folder);
                HideFailure();
                return;
            }

            _appliedRoot = root;
            _appliedFolder = folder;
            _appliedVisibility = _requestedVisibility;
            _resetPending = false;
            StartSelectionWatchdog(folder);
            HideFailure();
        }
        catch (Exception ex)
        {
            _resetPending = true;
            ShowFailure(ex);
        }
    }

    private void ResizeHost()
    {
        try { _host.SetBounds(ClientRectangle); }
        catch (Exception ex) { ShowFailure(ex); }
    }

    private void ShowFailure(Exception exception)
    {
        _errorMessage.Text = $"{(_rootKind == NameSpaceTreeRootKind.Drive ? "ドライブ" : "デスクトップ")}ツリーを表示できません。{Environment.NewLine}{exception.Message}";
        _failure.Visible = true;
        _failure.BringToFront();
    }

    private void HideFailure()
    {
        _failure.Visible = false;
        ResizeHost();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _selectionWatchdog.Stop();
        try { base.Dispose(disposing); }
        finally { if (disposing) _selectionWatchdog.Dispose(); }
    }

    private void StartSelectionWatchdog(string folder)
    {
        if (SamePath(SelectedFolder, folder))
        {
            _selectionWatchdog.Stop();
            return;
        }
        _selectionDeadline = Environment.TickCount64 + SelectionTimeoutMilliseconds;
        _selectionWatchdog.Start();
    }

    private void CheckSelectionTimeout()
    {
        if (!_hostCreated || _requestedFolder is not { } folder)
        {
            _selectionWatchdog.Stop();
            return;
        }
        // R-97-2: 自動の選択が残っていなければ失敗ではない。利用者がキーで動かすと自動の選択は捨てられ、
        // 選択は現在位置と一致しなくなる
        if (SamePath(SelectedFolder, folder) || !_host.SelectionPending)
        {
            _selectionWatchdog.Stop();
            return;
        }
        if (Environment.TickCount64 < _selectionDeadline) return;

        _selectionWatchdog.Stop();
        _host.CancelPendingSelection();
        _resetPending = true;
        ShowFailure(new IOException("現在位置までツリーを展開できませんでした。"));
    }

    private static bool SamePath(string? left, string? right)
    {
        if (left is null || right is null) return false;
        var normalizedLeft = Path.TrimEndingDirectorySeparator(Path.GetFullPath(left));
        var normalizedRight = Path.TrimEndingDirectorySeparator(Path.GetFullPath(right));
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }
}
