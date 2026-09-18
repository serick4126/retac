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
    }

    public void Rebuild(IReadOnlyList<Bookmark> bar, BookmarkBarStyle style, BookmarkItems items)
    {
        SuspendLayout();
        BookmarkItems.Clear(Items);
        var size = 16 * DeviceDpi / 96;
        ImageScalingSize = new Size(size, size);   // 拡大縮小でぼやけないよう、取ったアイコンの大きさに合わせる
        if (bar.Count == 0)
        {
            // R-89: 空のときの案内。押しても何も起きない
            Items.Add(new ToolStripLabel("フォルダやファイルをここへドラッグして追加") { ForeColor = SystemColors.GrayText });
        }
        else
        {
            Items.AddRange(items.BarItems(bar, style));
        }
        ResumeLayout();
    }
}
