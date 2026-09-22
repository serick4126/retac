using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Navigation;
using ReTAC.Shell;
using Timer = System.Windows.Forms.Timer;

namespace ReTAC.App;

/// <summary>
/// R-98: 左パネルのブックマークビュー。「ブックマークバー」「その他のブックマーク」を固定のルートにして、BookmarkSet 全体を木で出す。
/// 実行・右クリック・転送・保存はバーと同じ処理（IBookmarkHost / MainForm）を通す。ファイル操作コマンドはここでは実行しない。
/// </summary>
public sealed class BookmarkTreeView : TreeView
{
    private const string PlaceholderText = "ここへドラッグして追加";
    /// <summary>空の固定ルートの下の案内の行。選べないが、落とせばそのルートへ入る。</summary>
    private static readonly object PlaceholderTag = new();

    private readonly BookmarkItems _items;
    private readonly ImageList _images = new() { ColorDepth = ColorDepth.Depth32Bit };
    /// <summary>グループの上で止まったら開く（バーと同じ）。</summary>
    private readonly Timer _hold = new() { Interval = 1000 };
    /// <summary>作り直しをまたいで持つ、このウィンドウで展開中のグループ ID（R-96-2: 状態はウィンドウごと）。</summary>
    private HashSet<string> _expanded;
    private bool _barExpanded = true, _otherExpanded = true;
    private bool _building, _dragged;
    private TreeNode? _holding, _highlight;
    private (TreeNode Node, int After)? _insert;
    private Drop? _drop;

    /// <summary>落とす先。Transfer があればそのフォルダへの転送、無ければ List の Index の前へ入れる。</summary>
    private sealed record Drop(List<Bookmark>? List, int Index, string? Transfer);

    public event EventHandler? FocusFileViewRequested;
    /// <summary>ReTAC のキー割り当て。MainForm は左パネルの 5 コマンドだけを実行して Handled にする（R-98 / Q63）。</summary>
    public event EventHandler<KeyEventArgs>? CommandKeyRequested;
    /// <summary>右クリック。Bookmark は項目、List は追加系で足す並び（固定ルート）。両方 null なら空いた所。</summary>
    public event Action<Bookmark?, List<Bookmark>?, Point>? ContextMenuRequested;
    public event EventHandler? ExpandedGroupsChanged;

    public BookmarkTreeView(BookmarkItems items, IEnumerable<string> expandedGroupIds)
    {
        _items = items;
        _expanded = [.. expandedGroupIds];
        HideSelection = false;
        LabelEdit = false;   // F2 で名前を変えない（Q45）
        ShowNodeToolTips = true;
        AllowDrop = true;
        BorderStyle = BorderStyle.None;
        _images.ImageSize = new Size(LogicalToDeviceUnits(16), LogicalToDeviceUnits(16));
        _images.Images.Add("", new Bitmap(_images.ImageSize.Width, _images.ImageSize.Height));   // 絵の無い項目
        ImageList = _images;
        _hold.Tick += (_, _) =>
        {
            _hold.Stop();
            _holding?.Expand();
        };
        Disposed += (_, _) =>
        {
            _hold.Dispose();
            _images.Dispose();   // TreeView は渡された ImageList を破棄しない。ウィンドウを閉じるたびに残る
        };
    }

    /// <summary>今の BookmarkSet から作り直す。展開状態・選択は ID で引き継ぐ（名前変更・移動の後も同じグループに付く）。</summary>
    public void Rebuild()
    {
        if (Nodes.Count == 2)
        {
            _expanded = [.. ExpandedGroupIds];
            (_barExpanded, _otherExpanded) = (Nodes[0].IsExpanded, Nodes[1].IsExpanded);
        }
        var selectedId = (SelectedNode?.Tag as Bookmark)?.Id;
        var topId = (TopNode?.Tag as Bookmark)?.Id;
        var set = _items.Host.Bookmarks;
        var paths = new List<(TreeNode Node, string Path)>();
        _building = true;
        BeginUpdate();
        try
        {
            Nodes.Clear();
            Nodes.Add(Root("ブックマークバー", set.Bar, paths));
            Nodes.Add(Root("その他のブックマーク", set.Other, paths));
            // 初回は 2 つの固定ルートを開き、利用者のグループは保存した状態（Q50）
            if (_barExpanded) Nodes[0].Expand();
            if (_otherExpanded) Nodes[1].Expand();
            var byId = All(Nodes).Where(n => n.Tag is Bookmark).ToDictionary(n => ((Bookmark)n.Tag!).Id);
            foreach (var id in _expanded) if (byId.GetValueOrDefault(id) is { Tag: Bookmark { Kind: BookmarkKind.Group } } node) node.Expand();
            SelectedNode = selectedId is not null ? byId.GetValueOrDefault(selectedId) : null;
            if (topId is not null && byId.GetValueOrDefault(topId) is { } top) TopNode = top;
        }
        finally
        {
            EndUpdate();
            _building = false;
        }
        LoadIcons(paths);
    }

    /// <summary>このウィンドウで展開中のグループ ID。今あるグループだけなので、消えたグループの状態は残らない。</summary>
    public IEnumerable<string> ExpandedGroupIds =>
        All(Nodes).Where(n => n is { IsExpanded: true, Tag: Bookmark { Kind: BookmarkKind.Group } }).Select(n => ((Bookmark)n.Tag!).Id);

    private static IEnumerable<TreeNode> All(TreeNodeCollection nodes) =>
        nodes.Cast<TreeNode>().SelectMany(n => All(n.Nodes).Prepend(n));

    private TreeNode Root(string text, List<Bookmark> list, List<(TreeNode, string)> paths)
    {
        var root = new TreeNode(text) { Tag = list };
        AddChildren(root, list, paths);
        if (list.Count == 0) root.Nodes.Add(new TreeNode(PlaceholderText) { Tag = PlaceholderTag, ForeColor = SystemColors.GrayText });
        return root;
    }

    private void AddChildren(TreeNode parent, List<Bookmark> list, List<(TreeNode, string)> paths)
    {
        foreach (var b in list)
        {
            var name = BookmarkRules.DisplayName(b, _items.Host.LabelOf);
            var node = new TreeNode(name) { Tag = b, ToolTipText = b.Kind is BookmarkKind.Folder or BookmarkKind.File ? b.Target : "" };
            SetImage(node, GlyphKey(b));
            if (_items.IconPath(b) is { } path) paths.Add((node, path));
            if (b.Children is { } children) AddChildren(node, children, paths);
            parent.Nodes.Add(node);
        }
    }

    // ---- アイコン（バーと同じ絵。シェルのアイコンは応答しないドライブで待たされるので裏で取る。N-05） ----

    private string GlyphKey(Bookmark b)
    {
        var key = b.Kind == BookmarkKind.Group ? "glyph:group" : b.Kind == BookmarkKind.Command ? "glyph:" + b.Target : "";
        if (key.Length == 0 || _images.Images.ContainsKey(key)) return key;
        if (_items.Glyph(b) is not { } glyph) return "";
        _images.Images.Add(key, glyph);
        return key;
    }

    private static void SetImage(TreeNode node, string key) => (node.ImageKey, node.SelectedImageKey) = (key, key);

    // ponytail: 取ったアイコンは作り直しをまたいで取っておき、消さない。ブックマークの件数までしか増えない
    private void LoadIcons(List<(TreeNode Node, string Path)> paths)
    {
        var missing = paths.Select(p => p.Path).Where(p => !_images.Images.ContainsKey("path:" + p)).Distinct().ToList();
        var size = _images.ImageSize.Width;
        void Apply()
        {
            foreach (var (node, path) in paths)
                if (node.TreeView == this && _images.Images.ContainsKey("path:" + path)) SetImage(node, "path:" + path);
        }
        if (missing.Count == 0) { Apply(); return; }
        if (!IsHandleCreated)
        {
            // ビューは選ぶまで窓を持たず、結果を UI スレッドへ戻せない。窓ができてから読む（作り直しで外れた行は Apply が飛ばす）
            EventHandler? later = null;
            later = (_, _) => { HandleCreated -= later; LoadIcons(paths); };
            HandleCreated += later;
            return;
        }
        Task.Run(() =>
        {
            using var icons = new ShellIcons(size);
            return missing.Select(p => (p, image: icons.ForPath(p) is { } b ? new Bitmap(b) : null)).ToList();
        }).ContinueWith(task =>
        {
            if (!task.IsCompletedSuccessfully || IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(() =>
                {
                    foreach (var (path, image) in task.Result)
                        if (image is not null && !_images.Images.ContainsKey("path:" + path)) _images.Images.Add("path:" + path, image);
                    Apply();
                });
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException) { }   // 読んでいる間に窓が閉じた
        });
    }

    // ---- 実行（Q14: Web ブラウザーと同じくシングルクリック） ----

    private void Run(TreeNode node)
    {
        switch (node.Tag)
        {
            case List<Bookmark>:
            case Bookmark { Kind: BookmarkKind.Group }:
                node.Toggle();
                break;
            case Bookmark { Kind: BookmarkKind.Folder } b:
                _items.Host.JumpTo(b.Target);   // フォーカスはビューに残す（Q52。MainForm が見る）
                break;
            case Bookmark { Kind: BookmarkKind.File } b:
                if (File.Exists(b.Target)) _items.Host.OpenFile(b.Target);
                else _items.Host.BookmarkMissing(b);
                break;
            case Bookmark { Kind: BookmarkKind.Command } b when CommandTarget.Parse(b.Target) is { } target:
                _items.Host.Execute(target);
                break;
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _dragged = false;
        base.OnMouseDown(e);
    }

    protected override void OnNodeMouseClick(TreeNodeMouseClickEventArgs e)
    {
        base.OnNodeMouseClick(e);
        // 展開ボタンは標準の開閉だけ。行の余白・字下げも実行しない。ドラッグを始めたら実行しない
        if (e.Button != MouseButtons.Left || _dragged || e.Node is not { } node || node.Tag == PlaceholderTag) return;
        if (HitTest(e.Location).Location is not (TreeViewHitTestLocations.Label or TreeViewHitTestLocations.Image)) return;
        SelectedNode = node;
        Run(node);
    }

    protected override void OnBeforeSelect(TreeViewCancelEventArgs e)
    {
        base.OnBeforeSelect(e);
        if (e.Node?.Tag != PlaceholderTag) return;
        // 案内の行は選ばせない。キーで来たときは進む向きの次の行へ送る（止めるだけだと ↓ でその先へ行けなくなる）
        e.Cancel = true;
        if (e.Action != TreeViewAction.ByKeyboard || SelectedNode is not { } from) return;
        var next = e.Node.Bounds.Top > from.Bounds.Top ? e.Node.NextVisibleNode : e.Node.PrevVisibleNode;
        if (next is not null) BeginInvoke(() => SelectedNode = next);
    }

    protected override void OnAfterExpand(TreeViewEventArgs e)
    {
        base.OnAfterExpand(e);
        if (!_building && e.Node?.Tag is Bookmark) ExpandedGroupsChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnAfterCollapse(TreeViewEventArgs e)
    {
        base.OnAfterCollapse(e);
        if (!_building && e.Node?.Tag is Bookmark) ExpandedGroupsChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- キー（Q21 / Q45 / Q56 / Q63） ----

    /// <summary>入力キー扱いにしないと、Tab はフォームのタブ移動に、Enter は既定のボタンに取られる。</summary>
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Tab or Keys.Enter || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyData)
        {
            case Keys.Tab or (Keys.Tab | Keys.Shift):
                FocusFileViewRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case Keys.Enter:
                if (SelectedNode is { } node) Run(node);
                e.Handled = true;
                break;
            default:
                // 左パネルの 5 コマンドのキーだけが頭文字検索より優先する。それ以外（英数字・Delete・F2 など）は TreeView 標準に任せる
                CommandKeyRequested?.Invoke(this, e);
                break;
        }
        if (e.Handled) e.SuppressKeyPress = true;   // 続く WM_CHAR で頭文字検索・警告音が起きないように
        else base.OnKeyDown(e);
    }

    private const int WM_LBUTTONDBLCLK = 0x0203, WM_CONTEXTMENU = 0x007B;

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_LBUTTONDBLCLK:
                // 1 回目のクリックで実行済み。標準のダブルクリックの開閉まで通すと、グループが開いてすぐ閉じる
                return;
            case WM_CONTEXTMENU:
                ShowMenu(m.LParam);
                return;
        }
        base.WndProc(ref m);
    }

    /// <summary>右クリック・アプリケーションキー・Shift+F10。キーからなら選択中の行の下に出す。</summary>
    private void ShowMenu(IntPtr lParam)
    {
        TreeNode? node;
        Point screen;
        if (lParam == -1)
        {
            node = SelectedNode;
            screen = PointToScreen(node is null ? Point.Empty : new Point(node.Bounds.Left, node.Bounds.Bottom));
        }
        else
        {
            screen = new Point((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));
            node = GetNodeAt(PointToClient(screen));
        }
        switch (node?.Tag)
        {
            case Bookmark b: ContextMenuRequested?.Invoke(b, null, screen); break;
            case List<Bookmark> root: ContextMenuRequested?.Invoke(null, root, screen); break;
            case var tag when tag == PlaceholderTag: ContextMenuRequested?.Invoke(null, (List<Bookmark>)node!.Parent!.Tag!, screen); break;
            default: ContextMenuRequested?.Invoke(null, null, screen); break;
        }
    }

    // ---- ドラッグ＆ドロップ（バーと同じ規則。R-89 / R-93 / §9） ----

    protected override void OnItemDrag(ItemDragEventArgs e)
    {
        base.OnItemDrag(e);
        if (e.Button != MouseButtons.Left || e.Item is not TreeNode { Tag: Bookmark b }) return;
        _dragged = true;
        BookmarkDropZone.DragBookmark(this, b);
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        BookmarkDropZone.Guarded(e, () => { DragOverCore(e); DropTargetHelper.Enter(this, e); }, Reset);
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        BookmarkDropZone.Guarded(e, () => { DragOverCore(e); DropTargetHelper.Over(e); }, Reset);
    }

    protected override void OnDragLeave(EventArgs e)
    {
        base.OnDragLeave(e);
        BookmarkDropZone.Guarded(null, () => { Reset(); DropTargetHelper.Leave(); }, Reset);
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        BookmarkDropZone.Guarded(e, () => { DropTargetHelper.Drop(e); DropCore(e); }, Reset);
    }

    private void DragOverCore(DragEventArgs e)
    {
        var client = PointToClient(new Point(e.X, e.Y));
        ScrollNearEdge(client);
        var dragging = BookmarkDropZone.Dragging;
        var reorder = dragging is not null && e.Data?.GetDataPresent(BookmarkDropZone.Format) == true;
        var files = !reorder && e.Data?.GetDataPresent(DataFormats.FileDrop) == true;
        var set = _items.Host.Bookmarks;
        var node = GetNodeAt(client);

        Drop? drop = null;
        TreeNode? onto = null;
        (TreeNode Node, int After)? insert = null;
        if (!reorder && !files) { }
        else if (node is null)
        {
            drop = new Drop(set.Other, set.Other.Count, null);   // 最後の行より下の空いた所は、最後のルートの末尾
        }
        else if (node.Tag is List<Bookmark> root)
        {
            (drop, onto) = (new Drop(root, root.Count, null), node);
        }
        else if (node.Tag == PlaceholderTag)
        {
            (drop, onto) = (new Drop((List<Bookmark>)node.Parent!.Tag!, 0, null), node.Parent);
        }
        else if (node.Tag is Bookmark b && !ReferenceEquals(b, dragging))
        {
            var spot = BookmarkDrop.Hit([(node.Bounds.Top, node.Bounds.Bottom, true)], client.Y);
            if (spot.Onto)
            {
                onto = node;
                drop = BookmarkDrop.Onto(b.Kind, reorder) switch
                {
                    OntoAction.IntoGroup when b.Children is { } children => new Drop(children, children.Count, null),
                    OntoAction.Transfer => new Drop(null, 0, b.Target),
                    _ => null,
                };
            }
            else if (spot.Index == 1 && node.IsExpanded && b.Children is { Count: > 0 } children)
            {
                drop = new Drop(children, 0, null);   // 開いたグループの下半分は、見た目どおり中の先頭へ
                insert = (node.Nodes[0], 0);
            }
            else if (BookmarkRules.Locate(set, b) is var (list, index))
            {
                drop = new Drop(list, index + spot.Index, null);
                insert = (node, spot.Index);
            }
        }

        string message;
        if (drop?.Transfer is { } folder)
        {
            // R-93 / §9: フォルダの中央はそのフォルダへの転送。判定はファイルリストへのドロップと同じ
            DropFeedback.Apply(e, folder, DropFeedback.FolderLabel(folder));
            message = e.Effect switch
            {
                DragDropEffects.Copy => $"{DropFeedback.FolderLabel(folder)} へコピー",
                DragDropEffects.Move => $"{DropFeedback.FolderLabel(folder)} へ移動",
                _ => "",
            };
        }
        else
        {
            e.Effect = drop is null ? DragDropEffects.None : BookmarkDropZone.Effect(reorder, e.AllowedEffect);
            var group = onto is null ? "" : $"「{onto.Text}」に";
            message = e.Effect == DragDropEffects.None ? "" : reorder ? (onto is null ? "並べ替え" : $"{group}移す") : $"{group}ブックマークに追加";
        }
        _drop = e.Effect == DragDropEffects.None ? null : drop;
        if (_drop is null) ClearMarks();
        else SetMarks(onto, onto is null ? insert : null);
        _items.Host.ShowStatus(message);

        if (!ReferenceEquals(onto, _holding))
        {
            _hold.Stop();
            _holding = onto is { IsExpanded: false, Nodes.Count: > 0 } ? onto : null;
            if (_holding is not null) _hold.Start();
        }
    }

    private void DropCore(DragEventArgs e)
    {
        var drop = _drop;
        Reset();
        if (drop is null) return;
        if (drop.Transfer is { } folder)
        {
            if (BookmarkDropZone.Dragging is null && e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            {
                var (ctrl, shift) = DropFeedback.Modifiers(e);   // 後に回すとキーは離されている
                _items.Host.TransferDropped(files, folder, e.AllowedEffect, ctrl, shift);
            }
            return;
        }
        var changed = BookmarkDropZone.Dragging is { } dragging
            ? BookmarkRules.Move(_items.Host.Bookmarks, dragging, drop.List!, drop.Index)   // グループを自分の中へは移せない
            : e.Data?.GetData(DataFormats.FileDrop) is string[] paths && BookmarkDropZone.InsertFiles(drop.List!, drop.Index, paths);
        if (changed) _items.Host.BookmarksChanged();
    }

    private void Reset()
    {
        _hold.Stop();
        _holding = null;
        _drop = null;
        ClearMarks();
        _items.Host.ShowStatus("");
    }

    /// <summary>
    /// 強調と挿入線を、前と違うときだけ付け替える。DragOver は止まっていても繰り返し来るので、
    /// 毎回消して付け直すと強調・線がちらつく（実機指摘）。
    /// </summary>
    private void SetMarks(TreeNode? highlight, (TreeNode Node, int After)? insert)
    {
        if (!IsHandleCreated) return;
        if (!ReferenceEquals(highlight, _highlight))
        {
            SendMessage(Handle, TVM_SELECTITEM, TVGN_DROPHILITE, highlight?.Handle ?? IntPtr.Zero);
            _highlight = highlight;
        }
        if (insert != _insert)
        {
            SendMessage(Handle, TVM_SETINSERTMARK, insert?.After ?? 0, insert?.Node.Handle ?? IntPtr.Zero);
            _insert = insert;
        }
    }

    private void ClearMarks() => SetMarks(null, null);

    /// <summary>ドラッグ中に上端・下端へ来たら 1 行ずつ送る（TreeView は OLE のドラッグ中に自分では送らない）。</summary>
    private void ScrollNearEdge(Point client)
    {
        var edge = ItemHeight;
        if (client.Y < edge) SendMessage(Handle, WM_VSCROLL, SB_LINEUP, IntPtr.Zero);
        else if (client.Y > ClientSize.Height - edge) SendMessage(Handle, WM_VSCROLL, SB_LINEDOWN, IntPtr.Zero);
    }

    private const int TVM_SELECTITEM = 0x110B, TVM_SETINSERTMARK = 0x111A, TVGN_DROPHILITE = 0x0008;
    private const int WM_VSCROLL = 0x0115, SB_LINEUP = 0, SB_LINEDOWN = 1;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, nint wParam, IntPtr lParam);
}
