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
