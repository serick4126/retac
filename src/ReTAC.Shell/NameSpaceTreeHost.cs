using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using static ReTAC.Shell.NameSpaceTreeInterop;

namespace ReTAC.Shell;

public readonly record struct ShellTreeVisibility(bool ShowHidden, bool ShowSystem);

[Flags]
public enum ShellTreeHitTest : uint
{
    Nowhere = 0x0001,
    Icon = 0x0002,
    Label = 0x0004,
    Indent = 0x0008,
    Button = 0x0010,
    Right = 0x0020,
    StateIcon = 0x0040,
    Item = 0x0046,
    TabButton = 0x1000,
}

[Flags]
public enum ShellTreeClickType : uint
{
    Left = 0x0001,
    Middle = 0x0002,
    Right = 0x0003,
    ButtonMask = 0x0003,
    DoubleClick = 0x0004,
}

public sealed class ShellTreeSelectionChangedEventArgs(string? path) : EventArgs
{
    public string? Path { get; } = path;
}

public sealed class ShellTreeClickEventArgs(string? path, ShellTreeHitTest hitTest, ShellTreeClickType clickType) : EventArgs
{
    public string? Path { get; } = path;
    public ShellTreeHitTest HitTest { get; } = hitTest;
    public ShellTreeClickType ClickType { get; } = clickType;
}

public sealed class ShellTreeDropEventArgs(
    string[] files, string destination, uint keyState, DragDropEffects allowedEffect) : EventArgs
{
    private const uint MK_CONTROL = 0x0008, MK_SHIFT = 0x0004;

    public string[] Files { get; } = files;
    public string Destination { get; } = destination;
    public uint KeyState { get; } = keyState;
    public DragDropEffects AllowedEffect { get; } = allowedEffect;
    /// <summary>§9: ドロップ時点の修飾キー(OLE の KeyState は DragEventArgs と同じ MK_* ビット)。</summary>
    public bool Ctrl => (KeyState & MK_CONTROL) != 0;
    public bool Shift => (KeyState & MK_SHIFT) != 0;
}

public sealed class ShellTreeSynchronizationFailedEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}

/// <summary>R-97-3: WM_KEYDOWN の通知。Handled にすると NSTC 既定の処理（頭文字検索等）をさせない。</summary>
public sealed class ShellTreeKeyEventArgs(Keys keyData) : EventArgs
{
    public Keys KeyData { get; } = keyData;
    public bool Handled { get; set; }
}

/// <summary>R-97: Windows Shellの名前空間ツリーを、作成したSTA上で所有する。</summary>
public sealed class NameSpaceTreeHost : IDisposable
{
    private const int S_OK = 0;
    private const int S_FALSE = 1;
    private const int E_FAIL = unchecked((int)0x80004005);
    private const int E_NOTIMPL = unchecked((int)0x80004001);
    private const int E_INVALIDARG = unchecked((int)0x80070057);
    private const uint SICHINT_CANONICAL = 0x10000000;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_CHAR = 0x0102;
    private const int VK_RETURN = 0x0D;
    private const int VK_TAB = 0x09;

    private INameSpaceTreeControl? _tree;
    private EventSink? _sink;
    private IShellItem? _rootItem;
    private CurrentPathShellItemFilter? _rootFilter;
    private uint _adviseCookie;
    private IntPtr _treeHwnd;
    private int _ownerThreadId;
    private bool _acceptEvents;
    private string? _rootPath;
    private string? _selectedPath;
    private HashSet<string> _pendingExpandedPaths = new(StringComparer.OrdinalIgnoreCase);
    private long _expansionRestoreDeadline;
    private string? _pendingSelectionPath;
    private SynchronizationContext? _ownerContext;
    private bool _selectionContinuationPosted;
    private bool _selectionContinuationRunning;
    private bool _selectionSignalPending;
    private bool _ignoreSelectionEvents;
    private bool _selectingItem;
    private int _selectionVersion;
    private int _lifetime;
    private CancellationTokenSource? _selectionFallback;
    private string[] _dropSources = [];
    /// <summary>R-97: デスクトップツリー(PC全体で単一・固定のルート)かどうか。SelectItem の辿り方を変える。</summary>
    private bool _desktopMode;

    public event EventHandler<ShellTreeSelectionChangedEventArgs>? SelectionChanged;
    public event EventHandler<ShellTreeClickEventArgs>? ItemClicked;
    public event EventHandler<ShellTreeDropEventArgs>? FilesDropped;
    public event EventHandler<ShellTreeSynchronizationFailedEventArgs>? SynchronizationFailed;
    /// <summary>R-97-2: Enter キー。選択中の実フォルダを確定する合図（矢印等は既定のツリー処理へ渡すだけで、ここへは来ない）。</summary>
    public event EventHandler? CommitRequested;
    /// <summary>Step5: Tab / Shift+Tab。ツリーへフォーカスがある間はツリー標準のタブ移動をさせず、ファイルビューへ戻す合図にする。</summary>
    public event EventHandler? TabPressed;
    /// <summary>R-97-3: Enter/Tab 以外の WM_KEYDOWN。ReTAC の割り当てを優先させるため、NSTC 既定の処理より先に通知する。</summary>
    public event EventHandler<ShellTreeKeyEventArgs>? KeyPressed;

    public string? SelectedPath => _selectedPath;
    /// <summary>R-97: ツリーで何かは選択されているが、実パスを持たない項目である。</summary>
    public bool HasSelectionWithoutPath { get; private set; }

    public void Create(IntPtr parentHwnd, Rectangle bounds)
    {
        if (parentHwnd == IntPtr.Zero) throw new ArgumentException("親ウィンドウが必要です。", nameof(parentHwnd));
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("名前空間ツリーはSTAスレッドで作成してください。");

        Dispose();
        _lifetime++;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _ownerContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        try
        {
            var type = Type.GetTypeFromCLSID(ClsidNameSpaceTreeControl)
                ?? throw new InvalidOperationException("名前空間ツリーを作成できません。");
            _tree = (INameSpaceTreeControl)Activator.CreateInstance(type)!;
            _sink = new EventSink(this);
            var nativeBounds = new NativeRect(bounds);
            var style = TreeStyle.HasExpandos | TreeStyle.HasLines | TreeStyle.HorizontalScroll
                      | TreeStyle.RootHasExpando | TreeStyle.ShowSelectionAlways | TreeStyle.NoEditLabels | TreeStyle.TabStop
                      | TreeStyle.DisableDragDrop;
            Check(_tree.Initialize(parentHwnd, ref nativeBounds, style), "名前空間ツリーを初期化できません。");
            var style2 = TreeStyle2.NeverInsertNonEnumerated;
            Check(((INameSpaceTreeControl2)_tree).SetControlStyle2(style2, style2),
                "名前空間ツリーの列挙方法を設定できません。");
            var sinkEvents = Marshal.GetComInterfaceForObject(_sink, typeof(INameSpaceTreeControlEvents));
            try { Check(_tree.TreeAdvise(sinkEvents, out _adviseCookie), "名前空間ツリーの通知を購読できません。"); }
            finally { Marshal.Release(sinkEvents); }
            Check(((IOleWindow)_tree).GetWindow(out _treeHwnd), "名前空間ツリーのウィンドウを取得できません。");
            _acceptEvents = true;
            SetBounds(bounds);
        }
        catch
        {
            Cleanup();
            throw;
        }
    }

    public void SetBounds(Rectangle bounds)
    {
        VerifyOwner();
        if (_treeHwnd == IntPtr.Zero) throw new InvalidOperationException("名前空間ツリーが作成されていません。");
        if (!SetWindowPos(_treeHwnd, IntPtr.Zero, bounds.X, bounds.Y, Math.Max(0, bounds.Width), Math.Max(0, bounds.Height),
                SWP_NOZORDER | SWP_NOACTIVATE))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "名前空間ツリーをリサイズできません。");
    }

    public void SetRoot(string rootPath, string currentPath, ShellTreeVisibility visibility)
        => SetRoot(rootPath, currentPath, visibility, null);

    private void SetRoot(string rootPath, string currentPath, ShellTreeVisibility visibility,
        IReadOnlySet<string>? expandedPaths)
    {
        VerifyOwner();
        var normalizedRoot = ShellItemPath.RootOf(rootPath);
        if (!string.Equals(normalizedRoot, ShellItemPath.RootOf(currentPath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("現在位置は指定したルート内にありません。", nameof(currentPath));
        ApplyRoot(desktopMode: false, normalizedRoot, () => ShellItemPath.Create(normalizedRoot),
            currentPath, visibility, allowVirtualItems: false, expandedPaths);
    }

    /// <summary>R-97: デスクトップツリーのルート。PC 全体で単一・固定で、ドライブ/UNC共有の変化では作り直さない。</summary>
    public void SetDesktopRoot(string currentPath, ShellTreeVisibility visibility)
        => SetDesktopRoot(currentPath, visibility, null);

    private void SetDesktopRoot(string currentPath, ShellTreeVisibility visibility, IReadOnlySet<string>? expandedPaths)
    {
        VerifyOwner();
        ApplyRoot(desktopMode: true, NameSpaceTreePolicy.DesktopRootMarker, ShellItemPath.CreateDesktopRoot,
            currentPath, visibility, allowVirtualItems: true, expandedPaths);
    }

    /// <summary>SetRoot と SetDesktopRoot の共通部分（ルート項目の生成先だけが違う）。</summary>
    private void ApplyRoot(bool desktopMode, string rootMarker, Func<IShellItem> createRoot, string currentPath,
        ShellTreeVisibility visibility, bool allowVirtualItems, IReadOnlySet<string>? expandedPaths)
    {
        var tree = RequireTree();
        _desktopMode = desktopMode;

        var removeHr = tree.RemoveAllRoots();
        if (_rootItem is not null || removeHr != E_INVALIDARG)
            Check(removeHr, "名前空間ツリーのルートを消去できません。");
        Release(ref _rootItem);
        _rootFilter = null;
        _rootPath = null;
        _selectedPath = null;
        PrepareSelection(currentPath);

        // 現在位置の祖先が隠し・システム属性でもフィルターへ届くよう、最初の列挙候補には含める。
        // 同じ階層の不要な項目は CurrentPathShellItemFilter が表示設定に従って除外する。
        var enumFlags = EnumFlags.Folders | EnumFlags.IncludeHidden | EnumFlags.IncludeSuperHidden;

        IShellItem? newRoot = createRoot();
        var newFilter = new CurrentPathShellItemFilter(currentPath, visibility, allowVirtualItems);
        try
        {
            var filterPointer = Marshal.GetComInterfaceForObject(newFilter, typeof(IShellItemFilter));
            try
            {
                Check(tree.AppendRoot(newRoot, enumFlags, RootStyle.Expanded, filterPointer),
                    "名前空間ツリーへルートを追加できません。");
            }
            finally { Marshal.Release(filterPointer); }
            _rootItem = newRoot;
            _rootFilter = newFilter;
            newRoot = null;
            _rootPath = rootMarker;
            _pendingExpandedPaths = expandedPaths is null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : expandedPaths.Where(newFilter.IncludesBranch).ToHashSet(StringComparer.OrdinalIgnoreCase);
            ContinuePendingSelection();
        }
        catch
        {
            _pendingSelectionPath = null;
            tree.RemoveAllRoots();
            Release(ref _rootItem);
            _rootFilter = null;
            _rootPath = null;
            _selectedPath = null;
            _pendingExpandedPaths.Clear();
            throw;
        }
        finally { Release(ref newRoot); }
    }

    public void SelectPath(string path)
    {
        var tree = RequireTree();
        VerifyOwner();
        if (_rootPath is null) throw new InvalidOperationException("先にルートを設定してください。");
        // R-97: デスクトップツリーは PC 全体で単一のルートなので、選択先がドライブ/UNC共有をまたいでもよい。
        if (!_desktopMode && !string.Equals(_rootPath, ShellItemPath.RootOf(path), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("選択先は現在のルート内にありません。", nameof(path));

        var filter = _rootFilter!;
        var previousPath = filter.UpdateCurrentPath(path);
        if (!SamePath(previousPath, path) && ExceptionBranchesChanged(filter, previousPath, path))
        {
            // NSTC には枝の再フィルター API がないため、例外枝が変わる場合だけ展開状態を退避してルートを入れ直す。
            var expandedPaths = ExpandedPaths(tree);
            if (_desktopMode) SetDesktopRoot(path, filter.Visibility, expandedPaths);
            else SetRoot(_rootPath, path, filter.Visibility, expandedPaths);
            return;
        }

        // R-97-2: ツリーで選んだフォルダへ移ったときの同期。選び直すと祖先から順に EnsureItemVisible して
        // 選択が最下段付近へスクロールしてしまうので、既に選ばれていれば状態だけ揃えて表示位置は動かさない
        if (CurrentSelectionPath(tree) is { } selected && SamePath(selected, path))
        {
            CompleteSelection(path);
            return;
        }

        BeginSelection(path);
    }

    private static string? CurrentSelectionPath(INameSpaceTreeControl tree)
    {
        if (tree.GetSelectedItems(out var selection) < 0 || selection == IntPtr.Zero) return null;
        try { return ShellItemPath.FileSystemPathsOf(selection).FirstOrDefault(); }
        finally { Marshal.Release(selection); }
    }

    /// <summary>
    /// R-97-2: IOleWindow.GetWindow が返すのは外枠の窓で、キーを処理するのはその中の SysTreeView32。
    /// 外枠へ SetFocus してもキーは内側へ届かないので、内側のツリーへ直接フォーカスを置く。
    /// </summary>
    public void Focus()
    {
        VerifyOwner();
        if (_treeHwnd == IntPtr.Zero) return;
        var inner = FindWindowEx(_treeHwnd, IntPtr.Zero, "SysTreeView32", null);
        SetFocus(inner != IntPtr.Zero ? inner : _treeHwnd);
    }

    /// <summary>
    /// R-97-3 / Q83: クライアント座標 clientPoint にある項目の実パス。仮想項目・当たり無しは null。
    /// 自前のドラッグを始めるかどうかの判定に使う(実フォルダだけドラッグ元にする)。
    /// </summary>
    public string? RealFolderAt(Point clientPoint)
    {
        VerifyOwner();
        if (_tree is not { } tree) return null;
        var point = new NativePoint { X = clientPoint.X, Y = clientPoint.Y };
        var hr = ((INameSpaceTreeControl2)tree).HitTest(ref point, out var item);
        if (hr < 0 || item is null) return null;
        try { return ShellItemPath.FileSystemPathOf(item); }
        finally { Marshal.ReleaseComObject(item); }
    }

    /// <summary>
    /// R-97-3 / N-06: キー起動のシェルメニューを選択項目の直下に出すための位置(クライアント座標)。
    /// 選択が無い・矩形が取れないときは null(呼び出し側は何もしないか、既定の位置を使う)。
    /// </summary>
    public Point? SelectedItemAnchor()
    {
        VerifyOwner();
        if (_tree is not { } tree) return null;
        if (tree.GetSelectedItems(out var selection) < 0 || selection == IntPtr.Zero) return null;

        IShellItemArray? items = null;
        try
        {
            items = (IShellItemArray)Marshal.GetObjectForIUnknown(selection);
            Marshal.Release(selection);
            selection = IntPtr.Zero;
            if (items.GetCount(out var count) < 0 || count == 0) return null;
            if (items.GetItemAt(0, out var item) < 0 || item is null) return null;
            try
            {
                if (((INameSpaceTreeControl2)tree).GetItemRect(item, out var rect) < 0) return null;
                return new Point(rect.Left, rect.Bottom);
            }
            finally { Marshal.ReleaseComObject(item); }
        }
        finally
        {
            if (selection != IntPtr.Zero) Marshal.Release(selection);
            if (items is not null) Marshal.ReleaseComObject(items);
        }
    }

    /// <summary>現在位置への展開・選択がまだ終わっていないか。</summary>
    public bool SelectionPending => _pendingSelectionPath is not null;

    /// <summary>
    /// R-97-2: 利用者がツリーをキーで操作したら、現在位置への自動の展開・選択は捨てる。残すと、
    /// 利用者が動かした選択を自動の選択が奪い返したり、見張りが「展開の失敗」と取り違えたりする。
    /// OnItemClick はコードからの選択でも届くので、マウスはきっかけにしない（マウスの確定は新しい移動になる）。
    /// </summary>
    private void AbandonPendingSelection()
    {
        if (_pendingSelectionPath is null) return;
        _pendingSelectionPath = null;
        _pendingExpandedPaths.Clear();
        _selectionVersion++;
        _selectionContinuationPosted = false;
        _selectionSignalPending = false;
        CancelSelectionFallback();
        _ignoreSelectionEvents = false;
    }

    public void CancelPendingSelection()
    {
        _pendingSelectionPath = null;
        _selectionContinuationPosted = false;
        _selectionSignalPending = false;
        CancelSelectionFallback();
        _ignoreSelectionEvents = true;
        _selectionVersion++;
    }

    public void Dispose()
    {
        if (_tree is null && _sink is null) return;
        VerifyOwner();
        Cleanup();
    }

    private void Cleanup()
    {
        _acceptEvents = false;
        _dropSources = [];
        if (_tree is { } tree)
        {
            try { tree.RemoveAllRoots(); } catch (COMException) { }
            if (_adviseCookie != 0)
            {
                try { tree.TreeUnadvise(_adviseCookie); } catch (COMException) { }
                _adviseCookie = 0;
            }
            _sink?.Detach();
            _sink = null;
            var treeHwnd = _treeHwnd;
            _treeHwnd = IntPtr.Zero;
            if (treeHwnd != IntPtr.Zero && IsWindow(treeHwnd)) DestroyWindow(treeHwnd);
            Release(ref _rootItem);
            Marshal.FinalReleaseComObject(tree);
            _rootFilter = null;
        }
        else
        {
            _sink?.Detach();
            Release(ref _rootItem);
            _rootFilter = null;
        }
        _sink = null;
        _tree = null;
        _rootPath = null;
        _selectedPath = null;
        _pendingExpandedPaths.Clear();
        _pendingSelectionPath = null;
        _ownerContext = null;
        _selectionContinuationPosted = false;
        _selectionContinuationRunning = false;
        _selectionSignalPending = false;
        CancelSelectionFallback();
        _ignoreSelectionEvents = false;
        _selectionVersion++;
        _lifetime++;
        _ownerThreadId = 0;
    }

    private INameSpaceTreeControl RequireTree() =>
        _tree ?? throw new InvalidOperationException("名前空間ツリーが作成されていません。");

    private void VerifyOwner()
    {
        if (_ownerThreadId != 0 && _ownerThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("名前空間ツリーは作成したSTAスレッドで操作してください。");
    }

    private static void Check(int hr, string message)
    {
        if (hr < 0) throw ShellItemPath.Error(hr, message);
    }

    private static void Release(ref IShellItem? item)
    {
        if (item is null) return;
        Marshal.ReleaseComObject(item);
        item = null;
    }

    private void BeginSelection(string path)
    {
        PrepareSelection(path);
        try { ContinuePendingSelection(); }
        catch
        {
            _pendingSelectionPath = null;
            throw;
        }
    }

    private void PrepareSelection(string path)
    {
        _selectionVersion++;
        _selectionContinuationPosted = false;
        _selectionSignalPending = false;
        CancelSelectionFallback();
        _pendingSelectionPath = path;
        _expansionRestoreDeadline = 0;
        _selectedPath = null;
        _ignoreSelectionEvents = true;
    }

    private void ContinuePendingSelection()
    {
        if (_pendingSelectionPath is not { } path || _tree is not { } tree || _rootItem is null) return;
        if (_desktopMode && !NameSpaceTreePolicy.CanAutoSelectInDesktopTree(path))
        {
            // ponytail: UNC 現在位置をマップ済みドライブ文字へ変換しての解決はやらない。This PC 配下は
            // ドライブ文字でしか一致しないため、リトライを続けても原理的に解決しない。選択なしで完了とする。
            CompleteSelection(path);
            return;
        }
        if (_selectionContinuationRunning)
        {
            _selectionSignalPending = true;
            return;
        }

        _selectionContinuationPosted = false;
        _selectionContinuationRunning = true;
        _selectionSignalPending = false;
        CancelSelectionFallback();
        bool completed;
        try
        {
            var selected = SelectItem(tree, _rootItem, path);
            completed = selected && (RestorePendingExpansion(tree) || ExpansionRestoreExpired());
        }
        finally { _selectionContinuationRunning = false; }

        if (!completed)
        {
            if (_selectionSignalPending)
            {
                _selectionSignalPending = false;
                ScheduleSelectionContinuation("coalesced-during-continuation");
            }
            else
            {
                // UNC は状態が進んでも最終イベントを出さない場合があるため、イベントが無い時だけ一度遅延確認する。
                ScheduleSelectionFallback();
            }
            return;
        }

        CompleteSelection(path);
    }

    private void CompleteSelection(string path)
    {
        _pendingSelectionPath = null;
        _selectionVersion++;
        _selectionContinuationPosted = false;
        _selectionSignalPending = false;
        CancelSelectionFallback();
        _ignoreSelectionEvents = false;
        PublishSelection(path, hasSelection: true);
    }

    private void ScheduleSelectionContinuation(string source)
    {
        if (_pendingSelectionPath is null || _ownerContext is null) return;
        CancelSelectionFallback();
        if (_selectionContinuationRunning)
        {
            _selectionSignalPending = true;
            return;
        }
        if (_selectionContinuationPosted) return;
        _selectionContinuationPosted = true;
        var lifetime = _lifetime;
        var selectionVersion = _selectionVersion;
        _ownerContext.Post(_ =>
        {
            if (lifetime != _lifetime || selectionVersion != _selectionVersion) return;
            _selectionContinuationPosted = false;
            if (_pendingSelectionPath is null || _tree is null) return;
            try { ContinuePendingSelection(); }
            catch (Exception ex)
            {
                _pendingSelectionPath = null;
                _selectionVersion++;
                _selectionContinuationPosted = false;
                SynchronizationFailed?.Invoke(this, new ShellTreeSynchronizationFailedEventArgs(ex));
            }
        }, null);
    }

    private void ScheduleSelectionFallback()
    {
        if (_pendingSelectionPath is null || _selectionFallback is not null || _ownerContext is null) return;
        var cancellation = new CancellationTokenSource();
        _selectionFallback = cancellation;
        var lifetime = _lifetime;
        var selectionVersion = _selectionVersion;
        _ = DelaySelectionContinuation(cancellation, lifetime, selectionVersion);
    }

    private async Task DelaySelectionContinuation(
        CancellationTokenSource cancellation, int lifetime, int selectionVersion)
    {
        try { await Task.Delay(250, cancellation.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }

        var context = _ownerContext;
        if (context is null) return;
        context.Post(_ =>
        {
            if (!ReferenceEquals(_selectionFallback, cancellation))
            {
                return;
            }
            _selectionFallback = null;
            cancellation.Dispose();
            if (lifetime != _lifetime || selectionVersion != _selectionVersion
                || _pendingSelectionPath is null || _tree is null) return;
            try { ContinuePendingSelection(); }
            catch (Exception ex)
            {
                _pendingSelectionPath = null;
                _selectionVersion++;
                SynchronizationFailed?.Invoke(this, new ShellTreeSynchronizationFailedEventArgs(ex));
            }
        }, null);
    }

    private void CancelSelectionFallback()
    {
        var cancellation = _selectionFallback;
        _selectionFallback = null;
        if (cancellation is null) return;
        cancellation.Cancel();
        cancellation.Dispose();
    }

    private bool SelectItem(INameSpaceTreeControl tree, IShellItem root, string path)
    {
        if (!_desktopMode)
        {
            var rootPath = Path.TrimEndingDirectorySeparator(ShellItemPath.FileSystemPathOf(root) ?? "");
            if (string.Equals(rootPath, Path.TrimEndingDirectorySeparator(path), StringComparison.OrdinalIgnoreCase))
                return SelectVisibleRoot(tree, root);
            return DescendAndSelect(tree, root, ShellItemPath.ParentPathsFromRoot(path), path);
        }

        // R-97: デスクトップツリーは Desktop → This PC(仮想) → ドライブ文字、の順でしか辿れない
        // (実機ゲートで確認済み。マップ済みネットワークドライブもドライブ文字としてのみ現れる)。
        // ContinuePendingSelection が UNC を先に弾くので、ここに来る path は常にローカルドライブのパス。
        // R-97: デスクトップの実フォルダとその配下は、PC の下ではなく最上段（デスクトップ直下）から選ぶ。
        // 利用者が見ているのはそこで、PC → ドライブ経由では辿れないこともある（リダイレクトされたデスクトップ等）
        if (ShellItemPath.FileSystemPathOf(root) is { Length: > 0 } desktopPath)
        {
            var desktop = Path.TrimEndingDirectorySeparator(desktopPath);
            var target = Path.TrimEndingDirectorySeparator(path);
            if (string.Equals(desktop, target, StringComparison.OrdinalIgnoreCase))
                return SelectVisibleRoot(tree, root);
            if (target.StartsWith(desktop + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return DescendAndSelect(tree, root, ShellItemPath.ParentPathsFrom(desktop, path), path);
        }

        var computer = ShellItemPath.CreateComputerFolder();
        IShellItem? computerInTree = null;
        IShellItem? driveInTree = null;
        try
        {
            computerInTree = FindChildMatching(tree, root, computer);
            if (computerInTree is null) return false;

            // This PC は Desktop 直下の仮想項目で RootStyle.Expanded の対象外なので、明示的に展開する。
            // 展開直後は子(ドライブ)がまだ列挙されていないことがあるが、それは false を返して既存の
            // OnAfterExpand / OnItemAdded 経由の再試行に任せる(SelectItem 全体が再実行される)。
            Check(tree.SetItemState(computerInTree, ItemState.Expanded, ItemState.Expanded),
                "名前空間ツリーの枝を展開できません。");
            var visibleHr = tree.EnsureItemVisible(computerInTree);
            if (visibleHr == E_INVALIDARG) return false;
            Check(visibleHr, "名前空間ツリーの枝を表示できません。");

            var driveRoot = ShellItemPath.RootOf(path);
            driveInTree = FindChild(tree, computerInTree, driveRoot);
            if (driveInTree is null) return false;
            if (string.Equals(Path.TrimEndingDirectorySeparator(driveRoot),
                    Path.TrimEndingDirectorySeparator(path), StringComparison.OrdinalIgnoreCase))
                return SetSelected(tree, driveInTree);
            return DescendAndSelect(tree, driveInTree, ShellItemPath.ParentPathsFrom(driveRoot, path), path);
        }
        finally
        {
            Marshal.ReleaseComObject(computer);
            if (computerInTree is not null) Marshal.ReleaseComObject(computerInTree);
            if (driveInTree is not null) Marshal.ReleaseComObject(driveInTree);
        }
    }

    /// <summary>SelectItem の起点(root)そのものを選ぶ場合だけの経路。root は AppendRoot に渡した
    /// こちら側の IShellItem で、ツリー内部の実体とは COM の同一性が違うことがあるため、
    /// GetNextItem(FirstVisible) で実体を取り直してから選択する。</summary>
    private bool SelectVisibleRoot(INameSpaceTreeControl tree, IShellItem root)
    {
        var hr = tree.GetNextItem(null!, NextItem.FirstVisible, out var actualRoot);
        if (hr < 0 || actualRoot is null)
        {
            if (actualRoot is not null) Marshal.ReleaseComObject(actualRoot);
            if (hr < 0 && hr is not E_FAIL and not E_INVALIDARG)
                Check(hr, "名前空間ツリーのルートを取得できません。");
            return false;
        }
        try { return SameItem(root, actualRoot) && SetSelected(tree, actualRoot); }
        finally { Marshal.ReleaseComObject(actualRoot); }
    }

    /// <summary>startItem(その実パスへの祖先を辿った後の項目)から parentPaths を 1 段ずつ展開し、
    /// 最後に targetPath の子を選択する。startItem は FindChild 等で得たツリー内在の項目である想定
    /// (SetSelected を直接呼べる。SelectVisibleRoot の再取得は不要)。</summary>
    private bool DescendAndSelect(INameSpaceTreeControl tree, IShellItem startItem, string[] parentPaths, string targetPath)
    {
        IShellItem current = startItem;
        var releaseCurrent = false;
        try
        {
            foreach (var parentPath in parentPaths)
            {
                var child = FindChild(tree, current, parentPath);
                if (child is null) return false;
                if (releaseCurrent) Marshal.ReleaseComObject(current);
                current = child;
                releaseCurrent = true;

                Check(tree.GetItemState(current, ItemState.Expanded, out var state),
                    "名前空間ツリーの展開状態を取得できません。");
                if ((state & ItemState.Expanded) == 0)
                    Check(tree.SetItemState(current, ItemState.Expanded, ItemState.Expanded),
                        "名前空間ツリーの枝を展開できません。");
                var visibleHr = tree.EnsureItemVisible(current);
                if (visibleHr == E_INVALIDARG) return false;
                Check(visibleHr, "名前空間ツリーの枝を表示できません。");
            }

            var target = FindChild(tree, current, targetPath);
            if (target is null) return false;
            try { return SetSelected(tree, target); }
            finally { Marshal.ReleaseComObject(target); }
        }
        finally
        {
            if (releaseCurrent) Marshal.ReleaseComObject(current);
        }
    }

    private static IShellItem? FindChild(INameSpaceTreeControl tree, IShellItem parent, string path)
    {
        var expected = ShellItemPath.Create(path);
        try { return FindChildMatching(tree, parent, expected); }
        finally { Marshal.ReleaseComObject(expected); }
    }

    /// <summary>This PC のような、パスからは作れない期待値(既知フォルダー等)で子を探すための下請け。</summary>
    private static IShellItem? FindChildMatching(INameSpaceTreeControl tree, IShellItem parent, IShellItem expected)
    {
        IShellItem? item = null;
        try
        {
            var hr = tree.GetNextItem(parent, NextItem.Child, out item);
            while (hr >= 0 && item is not null)
            {
                var current = item;
                item = null;
                var keepCurrent = false;
                try
                {
                    if (SameItem(expected, current))
                    {
                        keepCurrent = true;
                        return current;
                    }
                    hr = tree.GetNextItem(current, NextItem.Next, out item);
                }
                finally
                {
                    if (!keepCurrent) Marshal.ReleaseComObject(current);
                }
            }
            if (hr < 0 && hr is not E_FAIL and not E_INVALIDARG)
                Check(hr, "名前空間ツリーの項目を列挙できません。");
            return null;
        }
        finally
        {
            if (item is not null) Marshal.ReleaseComObject(item);
        }
    }

    private static bool SameItem(IShellItem left, IShellItem right)
    {
        Check(left.Compare(right, SICHINT_CANONICAL, out var order),
            "Shell項目を比較できません。");
        if (order == 0) return true;
        // R-97: デスクトップ直下の項目はデスクトップを親に持つ識別子で、パスから作った項目と Compare では一致しない。
        // 同じ実フォルダかどうかは実パスで確かめる（実パスを持たない仮想項目はここで一致しない）
        return ShellItemPath.FileSystemPathOf(left) is { } leftPath
            && ShellItemPath.FileSystemPathOf(right) is { } rightPath
            && string.Equals(Path.TrimEndingDirectorySeparator(leftPath), Path.TrimEndingDirectorySeparator(rightPath),
                StringComparison.OrdinalIgnoreCase);
    }

    private bool SetSelected(INameSpaceTreeControl tree, IShellItem item)
    {
        // R-97-2: NSTC はコードからの選択でも OnItemClick を出す。確定扱いにすると現在位置の同期のたびに
        // 同じフォルダを開き直し、フォーカスもファイル一覧へ移ってしまう
        _selectingItem = true;
        try
        {
            Check(tree.SetItemState(item, ItemState.Selected, ItemState.Selected),
                "名前空間ツリーの項目を選択できません。");
        }
        finally { _selectingItem = false; }
        Check(tree.GetItemState(item, ItemState.Selected, out var state),
            "名前空間ツリーの選択状態を取得できません。");
        if ((state & ItemState.Selected) == 0)
        {
            return false;
        }
        var ensureHr = tree.EnsureItemVisible(item);
        if (ensureHr == E_INVALIDARG)
        {
            return false;
        }
        Check(ensureHr, "名前空間ツリーの選択項目を表示できません。");
        var selected = IsSelectedItem(tree, item);
        return selected;
    }

    private static bool IsSelectedItem(INameSpaceTreeControl tree, IShellItem expected)
    {
        var hr = tree.GetSelectedItems(out var pointer);
        if (hr < 0)
        {
            if (pointer != IntPtr.Zero) Marshal.Release(pointer);
            if (hr == E_INVALIDARG) return false;
            Check(hr, "名前空間ツリーの選択項目を取得できません。");
        }
        if (pointer == IntPtr.Zero) return false;

        IShellItemArray? items = null;
        try
        {
            items = (IShellItemArray)Marshal.GetObjectForIUnknown(pointer);
            Marshal.Release(pointer);
            pointer = IntPtr.Zero;
            Check(items.GetCount(out var count), "名前空間ツリーの選択数を取得できません。");
            for (uint index = 0; index < count; index++)
            {
                var itemHr = items.GetItemAt(index, out var selected);
                if (itemHr < 0)
                {
                    if (selected is not null) Marshal.ReleaseComObject(selected);
                    Check(itemHr, "名前空間ツリーの選択項目を取得できません。");
                }
                if (selected is null) continue;
                try
                {
                    if (SameItem(expected, selected)) return true;
                }
                finally { Marshal.ReleaseComObject(selected); }
            }
            return false;
        }
        finally
        {
            if (pointer != IntPtr.Zero) Marshal.Release(pointer);
            if (items is not null) Marshal.ReleaseComObject(items);
        }
    }

    private void SelectionFrom(IntPtr array)
    {
        if (!_acceptEvents) return;
        if (_pendingSelectionPath is not null)
        {
            ScheduleSelectionContinuation("selection-changed");
            return;
        }
        if (_ignoreSelectionEvents) return;
        PublishSelection(ShellItemPath.FileSystemPathsOf(array).FirstOrDefault(), hasSelection: array != IntPtr.Zero);
    }

    /// <summary>R-97: hasSelection は「ツリーで何かは選ばれているか」。path が無くても、パスを持たない
    /// 項目が選ばれていることはある(HasSelectionWithoutPath はその区別に使う)。</summary>
    private void PublishSelection(string? path, bool hasSelection)
    {
        _selectedPath = path;
        HasSelectionWithoutPath = hasSelection && path is null;
        SelectionChanged?.Invoke(this, new ShellTreeSelectionChangedEventArgs(path));
    }

    private static bool ExceptionBranchesChanged(CurrentPathShellItemFilter filter, string oldPath, string newPath)
    {
        foreach (var child in PathFromRoot(oldPath).Concat(PathFromRoot(newPath)))
        {
            if (NameSpaceTreePolicy.IsCurrentPathOrAncestor(child, oldPath)
                != NameSpaceTreePolicy.IsCurrentPathOrAncestor(child, newPath)
                && filter.RequiresException(child)) return true;
        }
        return false;

        static IEnumerable<string> PathFromRoot(string path) =>
            ShellItemPath.ParentPathsFromRoot(path).Append(path);
    }

    /// <summary>
    /// R-97: 作り直す前に開いていた枝の復元を、選択が済んでから一定時間で諦める。消えた枝や
    /// 表示対象から外れた枝は見つからないので、期限が無いと選択が完了せず、選択と EnsureItemVisible を
    /// やり直し続ける（スクロールが最下部に固定され、以後の移動にも追従しなくなる）。復元は見た目だけなので捨ててよい。
    /// </summary>
    private bool ExpansionRestoreExpired()
    {
        const long GiveUpMilliseconds = 3000;
        var now = Environment.TickCount64;
        if (_expansionRestoreDeadline == 0) _expansionRestoreDeadline = now + GiveUpMilliseconds;
        if (now < _expansionRestoreDeadline) return false;
        _pendingExpandedPaths.Clear();
        return true;
    }

    private bool RestorePendingExpansion(INameSpaceTreeControl tree)
    {
        foreach (var expandedPath in _pendingExpandedPaths.OrderBy(path => path.Length))
        {
            var item = FindVisibleItem(tree, expandedPath);
            if (item is null)
            {
                return false;
            }
            try
            {
                Check(tree.SetItemState(item, ItemState.Expanded, ItemState.Expanded),
                    "名前空間ツリーの展開状態を復元できません。");
                var visibleHr = tree.EnsureItemVisible(item);
                if (visibleHr != E_INVALIDARG)
                    Check(visibleHr, "名前空間ツリーの展開項目を表示できません。");
            }
            finally { Marshal.ReleaseComObject(item); }
        }
        _pendingExpandedPaths.Clear();
        return true;
    }

    private static HashSet<string> ExpandedPaths(INameSpaceTreeControl tree)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IShellItem? item = null;
        try
        {
            var hr = tree.GetNextItem(null!, NextItem.FirstVisible, out item);
            while (hr >= 0 && item is not null)
            {
                var current = item;
                item = null;
                try
                {
                    Check(tree.GetItemState(current, ItemState.Expanded, out var state),
                        "名前空間ツリーの展開状態を取得できません。");
                    if ((state & ItemState.Expanded) != 0 && ShellItemPath.FileSystemPathOf(current) is { } path)
                        paths.Add(path);
                    hr = tree.GetNextItem(current, NextItem.NextVisible, out item);
                }
                finally { Marshal.ReleaseComObject(current); }
            }
            if (hr < 0 && hr is not E_FAIL and not E_INVALIDARG)
                Check(hr, "名前空間ツリーの展開項目を列挙できません。");
            return paths;
        }
        finally
        {
            if (item is not null) Marshal.ReleaseComObject(item);
        }
    }

    private static IShellItem? FindVisibleItem(INameSpaceTreeControl tree, string path)
    {
        IShellItem? item = null;
        try
        {
            var hr = tree.GetNextItem(null!, NextItem.FirstVisible, out item);
            while (hr >= 0 && item is not null)
            {
                var current = item;
                item = null;
                var keepCurrent = false;
                try
                {
                    if (SamePath(ShellItemPath.FileSystemPathOf(current) ?? "", path))
                    {
                        keepCurrent = true;
                        return current;
                    }
                    hr = tree.GetNextItem(current, NextItem.NextVisible, out item);
                }
                finally
                {
                    if (!keepCurrent) Marshal.ReleaseComObject(current);
                }
            }
            if (hr < 0 && hr is not E_FAIL and not E_INVALIDARG)
                Check(hr, "名前空間ツリーの項目を列挙できません。");
            return null;
        }
        finally
        {
            if (item is not null) Marshal.ReleaseComObject(item);
        }
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);



    private void ClickFrom(IntPtr item, ShellTreeHitTest hitTest, ShellTreeClickType clickType)
    {
        if (_acceptEvents && !_selectingItem)

            ItemClicked?.Invoke(this, new ShellTreeClickEventArgs(ShellItemPath.FileSystemPathOf(item), hitTest, clickType));
    }

    private void RequestCommit()
    {
        if (!_acceptEvents) return;
        // R-97-2: NSTC はキーでの選択移動の OnSelectionChanged をダブルクリック時間ほど遅らせて出す。
        // 矢印の直後の Enter で一つ前のフォルダを確定しないよう、その場の選択を読み直す
        if (_pendingSelectionPath is null && _tree is not null
            && _tree.GetSelectedItems(out var selection) >= 0 && selection != IntPtr.Zero)
        {
            try { PublishSelection(ShellItemPath.FileSystemPathsOf(selection).FirstOrDefault(), hasSelection: true); }
            finally { Marshal.Release(selection); }
        }
        CommitRequested?.Invoke(this, EventArgs.Empty);
    }


    private void RequestTab()
    {
        if (_acceptEvents) TabPressed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>R-97-3: ReTAC の割り当てを優先するため、Enter/Tab 以外のキーを NSTC 既定の処理より先に通知する。
    /// Control.ModifierKeys はこの呼び出しと同じ UI スレッドで見るので、押下時の状態と一致する。</summary>
    private bool RequestKey(Keys keyCode)
    {
        if (!_acceptEvents) return false;
        var args = new ShellTreeKeyEventArgs(keyCode | Control.ModifierKeys);
        KeyPressed?.Invoke(this, args);
        return args.Handled;
    }

    private void ReplaceDropSources(IntPtr data) => _dropSources = ShellItemPath.FileSystemPathsOf(data);

    /// <summary>
    /// R-97-3 / §9: パスを持たない項目(仮想項目)は転送先にしない(Phase10.2 技術確認: 実機 OK)。
    /// OnDragPosition には手を出さない。NSTC 標準の約 1 秒の自動展開が、この判定と無関係に保たれる。
    /// </summary>
    private void RejectVirtualDropTarget(IntPtr over, ref uint effect)
    {
        if (!_acceptEvents) return;
        if (ShellItemPath.FileSystemPathOf(over) is null) effect = (uint)DragDropEffects.None;
    }

    private void DropFrom(IntPtr over, IntPtr data, uint keyState, ref uint effect)
    {
        var allowed = effect;
        try
        {
            if (data != IntPtr.Zero) ReplaceDropSources(data);
            var destination = ShellItemPath.FileSystemPathOf(over);
            if (_acceptEvents && destination is not null && _dropSources.Length > 0)
                FilesDropped?.Invoke(this, new ShellTreeDropEventArgs(_dropSources, destination, keyState, (DragDropEffects)allowed));
        }
        finally
        {
            effect = (uint)DragDropEffects.None; // Shell自身には即時転送させず、ReTAC側の確認経路へ渡す。
            _dropSources = [];
        }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class EventSink(NameSpaceTreeHost host) : INameSpaceTreeControlEvents, INameSpaceTreeControlDropHandler
    {
        private NameSpaceTreeHost? _host = host;
        internal void Detach() => _host = null;

        public int OnItemClick(IntPtr item, ShellTreeHitTest hitTest, ShellTreeClickType clickType)
        {
            try { _host?.ClickFrom(item, hitTest, clickType); }
            catch (Exception) { }
            return (hitTest & ShellTreeHitTest.Button) != 0 ? S_FALSE : S_OK;
        }

        public int OnSelectionChanged(IntPtr selection)
        {
            try { _host?.SelectionFrom(selection); }
            catch (Exception) { }
            return S_OK;
        }

        public int OnDragEnter(IntPtr over, IntPtr data, bool outsideSource, uint keyState, ref uint effect)
        {
            var hr = CacheDropSources(data);
            _host?.RejectVirtualDropTarget(over, ref effect);
            return hr;
        }

        public int OnDragOver(IntPtr over, IntPtr data, uint keyState, ref uint effect)
        {
            var hr = CacheDropSources(data);
            _host?.RejectVirtualDropTarget(over, ref effect);
            return hr;
        }

        public int OnDragPosition(IntPtr over, IntPtr data, int newPosition, int oldPosition) =>
            CacheDropSources(data);

        public int OnDrop(IntPtr over, IntPtr data, int position, uint keyState, ref uint effect)
        {
            try { _host?.DropFrom(over, data, keyState, ref effect); }
            catch (Exception) { ClearDropSources(); }
            finally { effect = (uint)DragDropEffects.None; }
            return S_OK;
        }

        public int OnDropPosition(IntPtr over, IntPtr data, int newPosition, int oldPosition) =>
            CacheDropSources(data);

        public int OnDragLeave(IntPtr over)
        {
            ClearDropSources();
            return S_OK;
        }

        public int OnPropertyItemCommit(IntPtr item) => S_OK;
        public int OnItemStateChanging(IntPtr item, ItemState mask, ItemState state) => S_OK;
        public int OnItemStateChanged(IntPtr item, ItemState mask, ItemState state) => S_OK;
        // R-97-2 / Step4-5: 矢印・Home・End・PageUp・PageDown は S_FALSE でツリー標準の処理へ渡し、
        // 現在位置（選択中の実フォルダ）は変えない。Enter だけが確定、Tab だけがファイルビューへ戻す合図で、
        // どちらもツリー既定の動作（ラベル編集の開始・既定のタブ移動）をさせないため S_OK で止める。
        public int OnKeyboardInput(uint message, nuint wParam, nint lParam)
        {
            try
            {
                if (message == WM_KEYDOWN)
                {
                    _host?.AbandonPendingSelection();
                    if ((int)wParam == VK_RETURN) { _host?.RequestCommit(); return S_OK; }
                    if ((int)wParam == VK_TAB) { _host?.RequestTab(); return S_OK; }
                    // R-97-3: ReTAC のコマンドとして処理したキーの WM_CHAR をツリーへ届けない。届くと頭文字検索で選択が動き、
                    // 一致しなければ警告音が鳴る（テンキーのドライブ移動など）。コマンドはここでダイアログを開くことがあり、
                    // その中で WM_CHAR が配られてしまうので、処理の後ではなく前にキューから外し、処理しなかったときだけ戻す
                    var hasChar = PeekMessage(out var charMessage, IntPtr.Zero, WM_CHAR, WM_CHAR, PM_REMOVE);
                    var handled = false;
                    try { handled = _host?.RequestKey((Keys)(int)wParam) == true; }
                    finally
                    {
                        if (hasChar && !handled)
                            PostMessage(charMessage.Hwnd, charMessage.Message, charMessage.WParam, charMessage.LParam);
                    }
                    if (handled) return S_OK;
                }
            }
            catch (Exception) { }
            return S_FALSE;
        }
        public int OnBeforeExpand(IntPtr item) => S_OK;
        public int OnAfterExpand(IntPtr item)
        {
            try
            {
                _host?.ScheduleSelectionContinuation(
                    $"after-expand path={ShellItemPath.FileSystemPathOf(item) ?? "<null>"}");
            }
            catch (Exception) { }
            return S_OK;
        }
        public int OnBeginLabelEdit(IntPtr item) => S_OK;
        public int OnEndLabelEdit(IntPtr item) => S_OK;
        public int OnGetToolTip(IntPtr item, IntPtr tip, int characterCount) => E_NOTIMPL;
        public int OnBeforeItemDelete(IntPtr item) => S_OK;
        public int OnItemAdded(IntPtr item, bool isRoot)
        {
            try
            {
                _host?.ScheduleSelectionContinuation(
                    $"item-added root={isRoot} path={ShellItemPath.FileSystemPathOf(item) ?? "<null>"}");
            }
            catch (Exception) { }
            return S_OK;
        }
        public int OnItemDeleted(IntPtr item, bool isRoot) => S_OK;
        public int OnBeforeContextMenu(IntPtr item, ref Guid iid, out IntPtr result) { result = IntPtr.Zero; return S_OK; }
        public int OnAfterContextMenu(IntPtr item, IntPtr contextMenu, ref Guid iid, out IntPtr result) { result = IntPtr.Zero; return S_OK; }
        public int OnBeforeStateImageChange(IntPtr item) => S_OK;
        // S_OK で -1 を返すと「アイコンなし」と受け取られ、ツリーにアイコンが出なくなる。E_NOTIMPL で NSTC 標準のシェルアイコンに任せる
        public int OnGetDefaultIconIndex(IntPtr item, out int defaultIcon, out int openIcon) { defaultIcon = -1; openIcon = -1; return unchecked((int)0x80004001); }

        private int CacheDropSources(IntPtr data)
        {
            try { _host?.ReplaceDropSources(data); }
            catch (Exception) { ClearDropSources(); }
            return S_OK;
        }

        private void ClearDropSources()
        {
            if (_host is { } host) host._dropSources = [];
        }
    }

    private const uint PM_REMOVE = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out NativeMessage message, IntPtr hwnd, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string? windowName);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hwnd);


    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);

}
