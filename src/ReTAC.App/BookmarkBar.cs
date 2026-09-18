using System.Drawing;
using System.Windows.Forms;
using ReTAC.Domain.Navigation;

namespace ReTAC.App;

/// <summary>
/// R-89: ブックマークバー。ブラウザのブックマークバーと同じ仕組みでツールバーを兼ねる。
/// フォーカスを取らない（ファイル操作のキーは今までどおりファイルリストだけが拾う）。
/// 入りきらない項目は ToolStrip の標準のオーバーフロー（»）に回す。
/// </summary>
public sealed class BookmarkBar : ToolStrip
{
    public BookmarkBar()
    {
        Dock = DockStyle.Top;
        GripStyle = ToolStripGripStyle.Hidden;
        CanOverflow = true;
        ShowItemToolTips = true;
        AutoSize = false;   // 高さはアドレスバーの行に揃える（SetRowHeight）
        ToolStripExtras.WidenOverflow(this);
    }

    /// <summary>上部の行（ドライブバー・アドレスバー）と同じ高さ・左右の余白にする。項目は縦の中央に並ぶ。</summary>
    public void SetRowHeight(int height, int sideMargin)
    {
        Height = height;
        Padding = new Padding(sideMargin, 0, sideMargin, 0);
    }

    private BookmarkItems? _items;
    /// <summary>左ボタンを押した位置。ここから動かしたらバーの項目のドラッグを始める。</summary>
    private Point? _dragOrigin;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _dragOrigin = e.Button == MouseButtons.Left ? e.Location : null;
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (e.Button != MouseButtons.Left || _dragOrigin is not { } origin) return;
        if (Math.Abs(e.X - origin.X) < SystemInformation.DragSize.Width
            && Math.Abs(e.Y - origin.Y) < SystemInformation.DragSize.Height) return;
        _dragOrigin = null;
        if (GetItemAt(origin) is not { Tag: Bookmark bookmark } item) return;
        (item as BarDropDownButton)?.CancelOpen();
        BookmarkDropZone.DragFrom(this, bookmark);
    }

    /// <summary>R-89: 項目の無い所の右クリック。項目の上は項目の MouseUp が受ける。</summary>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right && GetItemAt(e.Location) is null) _items?.Host.ShowContextMenu(null, PointToScreen(e.Location));
    }

    public void Rebuild(IReadOnlyList<Bookmark> bar, BookmarkBarStyle style, BookmarkItems items)
    {
        // ドロップの受け口は 1 度だけ付ける（作り直しのたびに付けると、1 回のドロップで何件も入る）
        if (_items is null) BookmarkDropZone.Attach(this, items.Host.Bookmarks.Bar, items.Host, vertical: false);
        _items = items;
        SuspendLayout();
        BookmarkItems.Clear(Items);
        var size = 16 * DeviceDpi / 96;
        ImageScalingSize = new Size(size, size);   // 拡大縮小でぼやけないよう、取ったアイコンの大きさに合わせる
        if (bar.Count == 0)
        {
            // R-89: 空のときの案内。押しても何も起きない
            var hint = new ToolStripLabel("フォルダやファイルをここへドラッグして追加") { ForeColor = SystemColors.GrayText };
            items.AttachContextMenu(hint);   // Tag が無いので空いた所と同じメニュー
            Items.Add(hint);
        }
        else
        {
            Items.AddRange(items.BarItems(bar, style));
        }
        ResumeLayout();
    }
}
