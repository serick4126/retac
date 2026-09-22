using System.Windows.Forms;
using ReTAC.Shell;

namespace ReTAC.App;

/// <summary>R-97: 現在位置のドライブまたはUNC共有だけをルートにするツリー。</summary>
public sealed class DriveTreeView : Control
{
    private const long SelectionTimeoutMilliseconds = 15_000;
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

    private const int WM_PARENTNOTIFY = 0x0210;
    private const int WM_LBUTTONDOWN = 0x0201;

    public event EventHandler<string>? FolderCommitted;
    public event EventHandler<ShellTreeDropEventArgs>? FilesDropped;
    /// <summary>Step5: Tab / Shift+Tab でツリーからファイルビューへ戻る合図。</summary>
    public event EventHandler? FocusFileViewRequested;

    public string? SelectedFolder { get; private set; }

    public DriveTreeView()
    {
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
        _selectionWatchdog.Tick += (_, _) => CheckSelectionTimeout();
    }

    /// <summary>NSTC の子ウィンドウが受けた WM_LBUTTONDOWN は、直接の親であるここへ通知が来る。</summary>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_PARENTNOTIFY && unchecked((int)(long)m.WParam & 0xFFFF) == WM_LBUTTONDOWN)
        {
            var lp = unchecked((int)(long)m.LParam);
            _pendingDownPoint = new Point(unchecked((short)(lp & 0xFFFF)), unchecked((short)((lp >> 16) & 0xFFFF)));
        }
        base.WndProc(ref m);
    }

    /// <summary>
    /// R-97-2: 確定はマウスを離した時（OnItemClick）だけで行う。ドラッグへ移行した操作はシェル自身が
    /// ドロップとして処理しここへは来ない想定だが、押下位置との距離チェックを二重の確認として残す。
    /// </summary>
    private void OnTreeItemClicked(object? sender, ShellTreeClickEventArgs e)
    {
        var downPoint = _pendingDownPoint ?? PointToClient(MousePosition);
        _pendingDownPoint = null;
        if (e.Path is not { } path) return;
        if ((e.ClickType & ShellTreeClickType.ButtonMask) != ShellTreeClickType.Left) return;

        var pending = new NameSpaceTreePolicy.PendingTreeClick(
            path, downPoint,
            OnIconOrLabel: (e.HitTest & (ShellTreeHitTest.Icon | ShellTreeHitTest.Label)) != 0,
            IsDoubleClick: (e.ClickType & ShellTreeClickType.DoubleClick) != 0);

        if (NameSpaceTreePolicy.ShouldCommit(pending, PointToClient(MousePosition), dragStarted: false, buttonReleased: true))
            FolderCommitted?.Invoke(this, path);
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

    protected override void OnHandleCreated(EventArgs e)

    {
        base.OnHandleCreated(e);
        CreateOrApply();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
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
            var root = NameSpaceTreePolicy.RootOf(folder);
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
                _host.SetRoot(root, folder, _requestedVisibility);
            }
            else if (!SamePath(_appliedFolder, folder))
            {
                _host.SelectPath(folder);
            }
            else
            {
                StartSelectionWatchdog(folder);
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
        _errorMessage.Text = $"ドライブツリーを表示できません。{Environment.NewLine}{exception.Message}";
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
        if (SamePath(SelectedFolder, folder))
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
