using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Navigation;

namespace ReTAC.App;

/// <summary>
/// R-89 §6.11: ブックマークの管理。左にツリー、右に操作。変更はその場で BookmarkSet に入れ、キャンセルを持たない
/// （クイックアクセスの設定と同じ。保存とバーの作り直しは閉じた後に呼び出し側が行う。V-13）。
/// </summary>
public sealed class BookmarkDialog : Form
{
    private readonly AppSettings _settings;
    private readonly QuickAccessList _quickAccess;
    private readonly string _currentFolder;
    private readonly TreeView _tree = new() { HideSelection = false, ShowNodeToolTips = true, AllowDrop = true };
    private readonly ComboBox _style = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _fixMissing = new() { Text = "見つからない項目は、自動で取り除く(&R)", AutoSize = true };
    /// <summary>作り直しても開いたままにするグループ（参照で覚える）。</summary>
    private readonly HashSet<Bookmark> _expanded = new(ReferenceEqualityComparer.Instance);
    private TreeNode? _dropHighlight;

    /// <param name="quickAccess">「クイックアクセスにも追加」の先。「自動で取り除く」もこれと共通（QuickAccessFixMissing）</param>
    public BookmarkDialog(AppSettings settings, QuickAccessList quickAccess, string currentFolder)
    {
        _settings = settings;
        _quickAccess = quickAccess;
        _currentFolder = currentFolder;

        Text = "ブックマークの管理";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(600, 482);

        _tree.SetBounds(12, 12, 440, 372);
        _tree.DoubleClick += (_, _) => Edit();
        _tree.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete) Remove();
            else if (e.KeyCode == Keys.F2) Edit();
            else return;
            e.Handled = true;
        };
        _tree.AfterExpand += (_, e) => { if (e.Node?.Tag is Bookmark b) _expanded.Add(b); };
        _tree.AfterCollapse += (_, e) => { if (e.Node?.Tag is Bookmark b) _expanded.Remove(b); };
        _tree.ItemDrag += (_, e) =>
        {
            // 最上位の 2 つはドラッグの元にしない（INV-BOOKMARK-FIXED-ROOTS）
            if (e.Item is TreeNode { Tag: Bookmark } node) _tree.DoDragDrop(node, DragDropEffects.Move);
        };
        _tree.DragOver += (_, e) => e.Effect = DropAt(e, drop: false) ? DragDropEffects.Move : DragDropEffects.None;
        _tree.DragDrop += (_, e) => DropAt(e, drop: true);
        _tree.DragLeave += (_, _) => ClearDropMarks();

        var buttons = new (string Text, Action Do)[]
        {
            ("フォルダを追加(&A)...", AddFolder),
            ("ファイルを追加(&I)...", AddFile),
            ("コマンドを追加(&O)...", AddCommand),
            ("グループを追加(&G)...", AddGroup),
            ("編集(&E)...", Edit),
            ("削除(&D)", Remove),
            ("↑(&U)", () => MoveSelected(-1)),
            ("↓(&W)", () => MoveSelected(1)),
            ("クイックアクセスにも追加(&Q)", AddToQuickAccess),
        };
        var y = 12;
        foreach (var (text, action) in buttons)
        {
            var button = new Button { Text = text, Bounds = new Rectangle(464, y, 124, 30) };
            button.Click += (_, _) => action();
            Controls.Add(button);
            y += 36;
        }

        var styleLabel = new Label { Text = "バーの表示(&S):", AutoSize = true, Location = new Point(14, 400) };
        _style.SetBounds(120, 396, 180, 23);
        _style.Items.AddRange(["アイコンと名前", "アイコンだけ", "名前だけ"]);   // BookmarkBarStyle の順
        _style.SelectedIndex = (int)settings.BookmarkBarStyle;
        _fixMissing.Location = new Point(16, 428);
        _fixMissing.Checked = quickAccess.FixMissingAutomatically;
        var close = new Button { Text = "閉じる", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(464, 440, 124, 30) };
        CancelButton = close;
        Controls.AddRange([_tree, styleLabel, _style, _fixMissing, close]);
        FormClosing += (_, _) =>
        {
            _settings.BookmarkBarStyle = (BookmarkBarStyle)_style.SelectedIndex;
            _quickAccess.FixMissingAutomatically = _fixMissing.Checked;
        };

        Reload(null);

        AutoScaleDimensions = new SizeF(96F, 96F);   // B-16
        AutoScaleMode = AutoScaleMode.Dpi;   // R-66
    }

    private BookmarkSet Set => _settings.Bookmarks;

    private string Label(CommandTarget target) => CommandLabels.Of(target, _settings.ExternalTools);

    /// <summary>ツリーを作り直し、select を選ぶ（null なら最初の置き場）。</summary>
    private void Reload(Bookmark? select)
    {
        _tree.BeginUpdate();
        _tree.Nodes.Clear();
        TreeNode? selected = null;
        foreach (var (title, list) in new[] { ("ブックマークバー", Set.Bar), ("その他のブックマーク", Set.Other) })
        {
            // 最上位は置き場そのもの（Tag は並び）。削除・改名・移動できない（INV-BOOKMARK-FIXED-ROOTS）
            var root = new TreeNode(title) { Tag = list };
            AddNodes(root.Nodes, list, select, ref selected);
            _tree.Nodes.Add(root);
            root.Expand();
        }
        _tree.EndUpdate();
        _tree.SelectedNode = selected ?? _tree.Nodes[0];
        _tree.SelectedNode.EnsureVisible();
    }

    private void AddNodes(TreeNodeCollection nodes, List<Bookmark> items, Bookmark? select, ref TreeNode? selected)
    {
        foreach (var b in items)
        {
            var node = new TreeNode(BookmarkRules.DisplayName(b, Label)) { Tag = b };
            if (b.Kind is BookmarkKind.Folder or BookmarkKind.File) node.ToolTipText = b.Target;
            if (ReferenceEquals(b, select)) selected = node;
            if (b.Children is { } children) AddNodes(node.Nodes, children, select, ref selected);
            nodes.Add(node);
            if (_expanded.Contains(b)) node.Expand();
        }
    }

    private Bookmark? SelectedBookmark => _tree.SelectedNode?.Tag as Bookmark;

    /// <summary>
    /// 足す場所。置き場やグループを選んでいればその末尾、それ以外なら選んだ項目の後ろ。
    /// </summary>
    private void Insert(Bookmark added)
    {
        switch (_tree.SelectedNode?.Tag)
        {
            case List<Bookmark> root:
                root.Add(added);
                break;
            case Bookmark { Children: { } children } group:
                children.Add(added);
                _expanded.Add(group);
                break;
            case Bookmark b when BookmarkRules.Locate(Set, b) is var (list, index):
                list.Insert(index + 1, added);
                break;
            default:
                Set.Bar.Add(added);
                break;
        }
        Reload(added);
    }

    private void AddFolder()
    {
        if (FolderBrowser.Select(this, null, _currentFolder) is { } folder) Insert(new Bookmark("", BookmarkKind.Folder, folder));
    }

    private void AddFile()
    {
        using var open = new OpenFileDialog { InitialDirectory = _currentFolder, CheckFileExists = true };
        if (open.ShowDialog(this) == DialogResult.OK) Insert(new Bookmark("", BookmarkKind.File, open.FileName));
    }

    private void AddCommand()
    {
        // 名前はコマンドの名前で埋める（コマンドのブックマークは名前が要る。BookmarkRules.Validate）
        if (CommandPickerDialog.Pick(this, _settings.ExternalTools) is { } target)
            Insert(new Bookmark(Label(target), BookmarkKind.Command, target.Serialize()));
    }

    private void AddGroup()
    {
        using var dialog = new BookmarkEntryDialog(new Bookmark("", BookmarkKind.Group, Children: []), _currentFolder,
                                                   _settings.ExternalTools, Label, isEdit: false);
        if (dialog.ShowDialog(this) == DialogResult.OK) Insert(dialog.Bookmark);
    }

    private void Edit()
    {
        if (SelectedBookmark is not { } b) return;
        using var dialog = new BookmarkEntryDialog(b, _currentFolder, _settings.ExternalTools, Label);
        if (dialog.ShowDialog(this) != DialogResult.OK || BookmarkRules.Locate(Set, b) is not var (list, index)) return;
        var edited = dialog.Bookmark;
        list[index] = edited;
        if (_expanded.Remove(b)) _expanded.Add(edited);
        Reload(edited);
    }

    private void Remove()
    {
        if (SelectedBookmark is not { } b) return;
        // 中身ごと消えるグループだけは確かめる（ブックマークの編集は元に戻せない）
        if (b.Children is { Count: > 0 } children && MessageBox.Show(this,
                $"グループ「{b.Title}」と中の {children.Count} 件を削除します。", "ReTAC",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        if (BookmarkRules.Locate(Set, b) is not var (list, index)) return;
        list.RemoveAt(index);
        // 次は同じ位置の項目、無ければ前の項目、それも無ければ親
        Reload(index < list.Count ? list[index] : index > 0 ? list[index - 1] : _tree.SelectedNode?.Parent?.Tag as Bookmark);
    }

    private void MoveSelected(int delta)
    {
        if (SelectedBookmark is not { } b || BookmarkRules.Locate(Set, b) is not var (list, index)) return;
        var to = index + delta;
        if (to < 0 || to >= list.Count) return;
        // Move の index は移す前の並びで数える（後ろへは 1 つ先の前へ入れる）
        BookmarkRules.Move(Set, b, list, delta > 0 ? to + 1 : to);
        Reload(b);
    }

    private void AddToQuickAccess()
    {
        if (SelectedBookmark is not { Kind: not BookmarkKind.Group } b) return;
        if (!_quickAccess.Add(new QuickAccessEntry(b.Title, b.Target, b.Kind)))
            MessageBox.Show(this, "クイックアクセスに登録済みです。", "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>
    /// ツリーの中のドラッグ（グループをまたぐ・バーとその他の間を含む）。置き場とグループの中央は「中へ」、
    /// それ以外は上半分なら前・下半分なら後ろ（バーと同じ BookmarkDrop.Hit で決める）。
    /// </summary>
    /// <returns>落とせるなら true。drop が true なら実際に移す</returns>
    private bool DropAt(DragEventArgs e, bool drop)
    {
        ClearDropMarks();
        if (e.Data?.GetData(typeof(TreeNode)) is not TreeNode { Tag: Bookmark dragged }) return false;
        var point = _tree.PointToClient(new Point(e.X, e.Y));
        if (_tree.GetNodeAt(point) is not { } target || ReferenceEquals(target.Tag, dragged)) return false;

        List<Bookmark> dest;
        int index;
        var isRoot = target.Tag is List<Bookmark>;
        var spot = isRoot ? new DropSpot(0, Onto: true)
            : BookmarkDrop.Hit([(target.Bounds.Top, target.Bounds.Bottom, target.Tag is Bookmark { Children: not null })], point.Y);
        if (spot.Onto)
        {
            dest = target.Tag as List<Bookmark> ?? ((Bookmark)target.Tag!).Children!;
            index = dest.Count;
        }
        else if (BookmarkRules.Locate(Set, (Bookmark)target.Tag!) is var (list, at))
        {
            dest = list;
            index = at + spot.Index;
        }
        else return false;

        if (!drop)
        {
            // 位置を見せる。中へはその項目を強調し、間は挿入線（TreeView の標準の印）
            if (spot.Onto) { _dropHighlight = target; SendMessage(_tree.Handle, TVM_SELECTITEM, TVGN_DROPHILITE, target.Handle); }
            else SendMessage(_tree.Handle, TVM_SETINSERTMARK, spot.Index, target.Handle);
            return true;
        }
        if (!BookmarkRules.Move(Set, dragged, dest, index)) return false;   // グループを自分の中へは移せない
        if (spot.Onto && target.Tag is Bookmark group) _expanded.Add(group);
        Reload(dragged);
        return true;
    }

    private void ClearDropMarks()
    {
        SendMessage(_tree.Handle, TVM_SETINSERTMARK, 0, IntPtr.Zero);
        if (_dropHighlight is null) return;
        SendMessage(_tree.Handle, TVM_SELECTITEM, TVGN_DROPHILITE, IntPtr.Zero);
        _dropHighlight = null;
    }

    private const int TVM_SELECTITEM = 0x110B;
    private const int TVM_SETINSERTMARK = 0x111A;
    private const int TVGN_DROPHILITE = 0x0008;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, nint wParam, IntPtr lParam);
}
