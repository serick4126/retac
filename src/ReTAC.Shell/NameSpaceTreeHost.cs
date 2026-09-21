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
    public string[] Files { get; } = files;
    public string Destination { get; } = destination;
    public uint KeyState { get; } = keyState;
    public DragDropEffects AllowedEffect { get; } = allowedEffect;
}

public sealed class ShellTreeSynchronizationFailedEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
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
    private string? _pendingSelectionPath;
    private SynchronizationContext? _ownerContext;
    private bool _selectionContinuationPosted;
    private bool _selectionContinuationRunning;
    private bool _selectionSignalPending;
    private bool _ignoreSelectionEvents;
    private int _selectionVersion;
    private int _lifetime;
    private CancellationTokenSource? _selectionFallback;
    private string[] _dropSources = [];

    public event EventHandler<ShellTreeSelectionChangedEventArgs>? SelectionChanged;
    public event EventHandler<ShellTreeClickEventArgs>? ItemClicked;
    public event EventHandler<ShellTreeDropEventArgs>? FilesDropped;
    public event EventHandler<ShellTreeSynchronizationFailedEventArgs>? SynchronizationFailed;

    public string? SelectedPath => _selectedPath;

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
                      | TreeStyle.RootHasExpando | TreeStyle.ShowSelectionAlways | TreeStyle.NoEditLabels | TreeStyle.TabStop;
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
        var tree = RequireTree();
        VerifyOwner();
        var normalizedRoot = ShellItemPath.RootOf(rootPath);
        if (!string.Equals(normalizedRoot, ShellItemPath.RootOf(currentPath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("現在位置は指定したルート内にありません。", nameof(currentPath));

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

        IShellItem? newRoot = ShellItemPath.Create(normalizedRoot);
        var newFilter = new CurrentPathShellItemFilter(currentPath, visibility);
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
            _rootPath = normalizedRoot;
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
        if (!string.Equals(_rootPath, ShellItemPath.RootOf(path), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("選択先は現在のルート内にありません。", nameof(path));

        var filter = _rootFilter!;
        var previousPath = filter.UpdateCurrentPath(path);
        if (!SamePath(previousPath, path) && ExceptionBranchesChanged(filter, previousPath, path))
        {
            // NSTC には枝の再フィルター API がないため、例外枝が変わる場合だけ展開状態を退避してルートを入れ直す。
            var expandedPaths = ExpandedPaths(tree);
            SetRoot(_rootPath, path, filter.Visibility, expandedPaths);
            return;
        }

        BeginSelection(path);
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
        _selectedPath = null;
        _ignoreSelectionEvents = true;
    }

    private void ContinuePendingSelection()
    {
        if (_pendingSelectionPath is not { } path || _tree is not { } tree || _rootItem is null) return;
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
            completed = selected && RestorePendingExpansion(tree);
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

        _pendingSelectionPath = null;
        _selectionVersion++;
        _selectionContinuationPosted = false;
        _selectionSignalPending = false;
        CancelSelectionFallback();
        _ignoreSelectionEvents = false;
        PublishSelection(path);
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
        if (string.Equals(Path.TrimEndingDirectorySeparator(ShellItemPath.FileSystemPathOf(root) ?? ""),
                Path.TrimEndingDirectorySeparator(path), StringComparison.OrdinalIgnoreCase))
        {
            var hr = tree.GetNextItem(null!, NextItem.FirstVisible, out var actualRoot);
            if (hr < 0 || actualRoot is null)
            {
                if (actualRoot is not null) Marshal.ReleaseComObject(actualRoot);
                if (hr < 0 && hr is not E_FAIL and not E_INVALIDARG)
                    Check(hr, "名前空間ツリーのルートを取得できません。");
                return false;
            }
            try
            {
                var same = SameItem(root, actualRoot);
                var selected = same && SetSelected(tree, actualRoot);
                return selected;
            }
            finally { Marshal.ReleaseComObject(actualRoot); }
        }

        IShellItem current = root;
        var releaseCurrent = false;
        try
        {
            foreach (var parentPath in ShellItemPath.ParentPathsFromRoot(path))
            {
                var parent = FindChild(tree, current, parentPath);
                if (parent is null)
                {
                    return false;
                }
                if (releaseCurrent) Marshal.ReleaseComObject(current);
                current = parent;
                releaseCurrent = true;

                Check(tree.GetItemState(current, ItemState.Expanded, out var state),
                    "名前空間ツリーの展開状態を取得できません。");
                if ((state & ItemState.Expanded) == 0)
                    Check(tree.SetItemState(current, ItemState.Expanded, ItemState.Expanded),
                        "名前空間ツリーの枝を展開できません。");
                var visibleHr = tree.EnsureItemVisible(current);
                if (visibleHr == E_INVALIDARG)
                {
                    return false;
                }
                Check(visibleHr, "名前空間ツリーの枝を表示できません。");
            }

            var target = FindChild(tree, current, path);
            if (target is null)
            {
                return false;
            }
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
            Marshal.ReleaseComObject(expected);
        }
    }

    private static bool SameItem(IShellItem left, IShellItem right)
    {
        Check(left.Compare(right, SICHINT_CANONICAL, out var order),
            "Shell項目を比較できません。");
        return order == 0;
    }

    private bool SetSelected(INameSpaceTreeControl tree, IShellItem item)
    {
        Check(tree.SetItemState(item, ItemState.Selected, ItemState.Selected),
            "名前空間ツリーの項目を選択できません。");
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
        PublishSelection(ShellItemPath.FileSystemPathsOf(array).FirstOrDefault());
    }

    private void PublishSelection(string? path)
    {
        _selectedPath = path;
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
        if (_acceptEvents)
            ItemClicked?.Invoke(this, new ShellTreeClickEventArgs(ShellItemPath.FileSystemPathOf(item), hitTest, clickType));
    }

    private void ReplaceDropSources(IntPtr data) => _dropSources = ShellItemPath.FileSystemPathsOf(data);

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

        public int OnDragEnter(IntPtr over, IntPtr data, bool outsideSource, uint keyState, ref uint effect) =>
            CacheDropSources(data);

        public int OnDragOver(IntPtr over, IntPtr data, uint keyState, ref uint effect) =>
            CacheDropSources(data);

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
        public int OnKeyboardInput(uint message, nuint wParam, nint lParam) => S_OK;
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
        public int OnGetDefaultIconIndex(IntPtr item, out int defaultIcon, out int openIcon) { defaultIcon = -1; openIcon = -1; return S_OK; }

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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);

}
