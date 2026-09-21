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

    public event EventHandler<string>? FolderCommitted;
    public event EventHandler<ShellTreeDropEventArgs>? FilesDropped;

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
        _selectionWatchdog.Tick += (_, _) => CheckSelectionTimeout();
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
