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

/// <summary>R-97: Windows Shellの名前空間ツリーを、作成したSTA上で所有する。</summary>
public sealed class NameSpaceTreeHost : IDisposable
{
    private const int S_OK = 0;
    private const int S_FALSE = 1;
    private const int E_NOTIMPL = unchecked((int)0x80004001);
    private const int E_INVALIDARG = unchecked((int)0x80070057);
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    private INameSpaceTreeControl? _tree;
    private EventSink? _sink;
    private IShellItem? _rootItem;
    private uint _adviseCookie;
    private IntPtr _treeHwnd;
    private int _ownerThreadId;
    private bool _acceptEvents;
    private string? _rootPath;
    private string? _selectedPath;
    private string[] _dropSources = [];

    public event EventHandler<ShellTreeSelectionChangedEventArgs>? SelectionChanged;
    public event EventHandler<ShellTreeClickEventArgs>? ItemClicked;
    public event EventHandler<ShellTreeDropEventArgs>? FilesDropped;

    public string? SelectedPath => _selectedPath;

    public void Create(IntPtr parentHwnd, Rectangle bounds)
    {
        if (parentHwnd == IntPtr.Zero) throw new ArgumentException("親ウィンドウが必要です。", nameof(parentHwnd));
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("名前空間ツリーはSTAスレッドで作成してください。");

        Dispose();
        _ownerThreadId = Environment.CurrentManagedThreadId;
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
        _rootPath = null;
        _selectedPath = null;

        var enumFlags = EnumFlags.Folders;
        if (visibility.ShowHidden) enumFlags |= EnumFlags.IncludeHidden;
        if (visibility.ShowSystem) enumFlags |= EnumFlags.IncludeSuperHidden;

        IShellItem? newRoot = ShellItemPath.Create(normalizedRoot);
        try
        {
            Check(tree.AppendRoot(newRoot, enumFlags, RootStyle.Expanded, IntPtr.Zero), "名前空間ツリーへルートを追加できません。");
            try { SelectItem(tree, currentPath); }
            catch
            {
                tree.RemoveAllRoots();
                _selectedPath = null;
                throw;
            }

            _rootItem = newRoot;
            newRoot = null;
            _rootPath = normalizedRoot;
            _selectedPath = currentPath;
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

        SelectItem(tree, path);
        _selectedPath = path;
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
        }
        else
        {
            _sink?.Detach();
            Release(ref _rootItem);
        }
        _sink = null;
        _tree = null;
        _rootPath = null;
        _selectedPath = null;
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

    private static void SelectItem(INameSpaceTreeControl tree, string path)
    {
        var item = ShellItemPath.Create(path);
        try
        {
            Check(tree.SetItemState(item, ItemState.Selected, ItemState.Selected), "名前空間ツリーの項目を選択できません。");
            Check(tree.EnsureItemVisible(item), "名前空間ツリーの選択項目を表示できません。");
        }
        finally { Marshal.ReleaseComObject(item); }
    }

    private void SelectionFrom(IntPtr array)
    {
        if (!_acceptEvents) return;
        _selectedPath = ShellItemPath.FileSystemPathsOf(array).FirstOrDefault();
        SelectionChanged?.Invoke(this, new ShellTreeSelectionChangedEventArgs(_selectedPath));
    }

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
        public int OnAfterExpand(IntPtr item) => S_OK;
        public int OnBeginLabelEdit(IntPtr item) => S_OK;
        public int OnEndLabelEdit(IntPtr item) => S_OK;
        public int OnGetToolTip(IntPtr item, IntPtr tip, int characterCount) => E_NOTIMPL;
        public int OnBeforeItemDelete(IntPtr item) => S_OK;
        public int OnItemAdded(IntPtr item, bool isRoot) => S_OK;
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
