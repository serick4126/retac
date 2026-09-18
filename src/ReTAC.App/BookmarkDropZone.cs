using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ReTAC.Domain.Navigation;
using Timer = System.Windows.Forms.Timer;

namespace ReTAC.App;

/// <summary>
/// R-89 §6.10: バーとグループのメニューの上のドラッグ。バーの項目の並べ替えと、ファイル・フォルダの追加を受ける。
/// ToolStrip.AllowItemReorder は Alt を押しながらでないと働かず、挿入位置も出ないので自前で扱う。
/// </summary>
internal sealed class BookmarkDropZone
{
    /// <summary>バーの項目のドラッグの形式。中身には何も載せない（他のアプリへ渡さない。ReTAC の外へは落とせない）。</summary>
    private const string Format = "ReTAC.Bookmark";

    /// <summary>ドラッグ中のバーの項目。窓をまたいでも同じプロセスの中なので、ここで受け渡す。</summary>
    private static Bookmark? s_dragging;

    private readonly ToolStrip _strip;
    private readonly List<Bookmark> _list;
    private readonly IBookmarkHost _host;
    private readonly bool _vertical;
    /// <summary>グループの上で止まったら開く（§8 の「ホールドで展開」）。</summary>
    private readonly Timer _hold = new() { Interval = 1000 };
    private ToolStripDropDownItem? _holding;
    private DropSpot? _spot;

    private BookmarkDropZone(ToolStrip strip, List<Bookmark> list, IBookmarkHost host, bool vertical)
    {
        _strip = strip;
        _list = list;
        _host = host;
        _vertical = vertical;
    }

    /// <param name="list">この並びへ入れる（バーなら BookmarkSet.Bar、グループのメニューならその Children）</param>
    /// <param name="vertical">メニュー（縦）なら true</param>
    public static void Attach(ToolStrip strip, List<Bookmark> list, IBookmarkHost host, bool vertical)
    {
        var zone = new BookmarkDropZone(strip, list, host, vertical);
        strip.AllowDrop = true;
        strip.DragEnter += zone.OnDragOver;
        strip.DragOver += zone.OnDragOver;
        strip.DragLeave += (_, _) => zone.Reset();
        strip.DragDrop += zone.OnDragDrop;
        strip.Paint += zone.OnPaint;
        zone._hold.Tick += (_, _) =>
        {
            zone._hold.Stop();
            if (zone._holding is { IsDisposed: false } item) item.ShowDropDown();
        };
        strip.Disposed += (_, _) => zone._hold.Dispose();
    }

    /// <summary>バーの項目のドラッグを始める。終わるまで戻らない。</summary>
    public static void DragFrom(ToolStrip strip, Bookmark bookmark)
    {
        s_dragging = bookmark;
        try { strip.DoDragDrop(new DataObject(Format, ""), DragDropEffects.Move); }
        finally { s_dragging = null; }
    }

    /// <summary>並んでいるブックマークの項目。バーで入りきらない項目は » の中なので、見えている先頭の分だけ。</summary>
    private List<ToolStripItem> Slots() =>
        _strip.Items.Cast<ToolStripItem>().Where(i => i.Tag is Bookmark && i.Placement == ToolStripItemPlacement.Main).ToList();

    private DropSpot Hit(List<ToolStripItem> slots, Point client) =>
        BookmarkDrop.Hit(slots.Select(i => _vertical
            ? (i.Bounds.Top, i.Bounds.Bottom, IsContainer(i))
            : (i.Bounds.Left, i.Bounds.Right, IsContainer(i))).ToList(), _vertical ? client.Y : client.X);

    private static bool IsContainer(ToolStripItem item) => item.Tag is Bookmark { Kind: BookmarkKind.Folder or BookmarkKind.Group };

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var slots = Slots();
        var spot = Hit(slots, _strip.PointToClient(new Point(e.X, e.Y)));
        var onto = spot.Onto ? slots[spot.Index] : null;
        var reorder = s_dragging is not null && e.Data?.GetDataPresent(Format) == true;
        // R3: 外からは FileDrop だけ。URL・テキスト・仮想ファイルは受けない
        var files = !reorder && e.Data?.GetDataPresent(DataFormats.FileDrop) == true;

        string message;
        if (!reorder && !files || onto?.Tag is Bookmark { Kind: BookmarkKind.Folder })
        {
            // フォルダの上は 9.2 では受けない（9.3 で転送にする）
            e.Effect = DragDropEffects.None;
            message = "";
        }
        else if (onto?.Tag is Bookmark { Children: null } || reorder && ReferenceEquals(onto?.Tag, s_dragging))
        {
            e.Effect = DragDropEffects.None;
            message = "";
        }
        else
        {
            // ファイルは Move にしない。移動と受け取った元（エクスプローラー）が、元のファイルを消すことがある
            e.Effect = reorder ? DragDropEffects.Move
                : e.AllowedEffect.HasFlag(DragDropEffects.Link) ? DragDropEffects.Link : DragDropEffects.Copy;
            var group = onto is null ? "" : $"「{onto.Text?.Replace("&&", "&")}」に";
            message = reorder ? (onto is null ? "並べ替え" : $"{group}移す") : $"{group}ブックマークに追加";
        }
        _host.ShowStatus(message);

        if (!ReferenceEquals(onto, _holding))
        {
            _hold.Stop();
            _holding = onto as ToolStripDropDownItem;
            if (_holding is { Tag: Bookmark { Kind: BookmarkKind.Group } }) _hold.Start();
        }
        var shown = e.Effect == DragDropEffects.None ? (DropSpot?)null : spot;
        if (shown != _spot)
        {
            _spot = shown;
            _strip.Invalidate();
        }
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        var spot = _spot;
        var slots = Slots();
        Reset();
        if (spot is not { } s) return;

        var onto = s.Onto ? slots[s.Index].Tag as Bookmark : null;
        var dest = onto?.Children ?? _list;
        var index = onto is null ? s.Index : dest.Count;   // グループの上なら、その末尾へ
        var changed = false;
        if (s_dragging is { } dragging)
        {
            changed = BookmarkRules.Move(_host.Bookmarks, dragging, dest, index);
        }
        else if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
        {
            // Q5: ドロップ元の順に全件。重複は畳まない。見つからないパスは飛ばす
            foreach (var path in paths)
            {
                BookmarkKind? kind = Directory.Exists(path) ? BookmarkKind.Folder : File.Exists(path) ? BookmarkKind.File : null;
                if (kind is not { } k) continue;
                dest.Insert(index++, new Bookmark("", k, path));
                changed = true;
            }
        }
        if (changed) _host.BookmarksChanged();
    }

    private void Reset()
    {
        _hold.Stop();
        _holding = null;
        _host.ShowStatus("");
        if (_spot is null) return;
        _spot = null;
        _strip.Invalidate();
    }

    /// <summary>挿入位置の線（項目の間）か、項目の枠（グループの上）を描く。ToolStrip は標準では何も出さない。</summary>
    private void OnPaint(object? sender, PaintEventArgs e)
    {
        if (_spot is not { } spot) return;
        var slots = Slots();
        var width = Math.Max(2, 2 * _strip.DeviceDpi / 96);
        using var brush = new SolidBrush(SystemColors.Highlight);
        if (spot.Onto)
        {
            using var pen = new Pen(SystemColors.Highlight, width);
            var bounds = slots[spot.Index].Bounds;
            e.Graphics.DrawRectangle(pen, bounds.X + 1, bounds.Y + 1, bounds.Width - 2, bounds.Height - 2);
            return;
        }
        // 線は、入れる位置の後ろの項目の手前（末尾なら最後の項目の後ろ）
        int At(Rectangle r, bool before) => _vertical ? (before ? r.Top : r.Bottom) : (before ? r.Left : r.Right);
        var edge = spot.Index < slots.Count ? At(slots[spot.Index].Bounds, before: true)
            : slots.Count > 0 ? At(slots[^1].Bounds, before: false)
            : _vertical ? _strip.Padding.Top : _strip.Padding.Left;
        var line = _vertical
            ? new Rectangle(4, edge - width / 2, _strip.ClientSize.Width - 8, width)
            : new Rectangle(edge - width / 2, 3, width, _strip.ClientSize.Height - 6);
        e.Graphics.FillRectangle(brush, line);
    }
}

/// <summary>
/// バーのフォルダ・グループのボタン。押したときではなく離したときに開く（押した時点で開くと、ドラッグを始められない）。
/// 開いているときに押したら、今までどおり閉じる。
/// </summary>
internal sealed class BarDropDownButton : ToolStripDropDownButton
{
    private bool _openOnUp;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && !DropDown.Visible) { _openOnUp = true; return; }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && _openOnUp)
        {
            _openOnUp = false;
            ShowDropDown();
            return;
        }
        base.OnMouseUp(e);
    }

    /// <summary>ドラッグが始まったので、離しても開かない。</summary>
    public void CancelOpen() => _openOnUp = false;
}
