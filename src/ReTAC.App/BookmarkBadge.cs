using System.Drawing;
using System.Drawing.Drawing2D;
using ReTAC.Domain.Navigation;

namespace ReTAC.App;

/// <summary>
/// R-140-1: ドロップ時の動作が「常にコピー／移動」のブックマークのアイコンの右下に重ねる印（＋は緑、−は赤）。
/// 色は背景ではなくアイコンの上に乗るのでライトとダークで変えない。ハイコントラストだけ OS の色にして、形で見分ける。
/// </summary>
internal static class BookmarkBadge
{
    private static readonly Color Frame = Color.FromArgb(0x60, 0x60, 0x60), Plus = Color.FromArgb(0x00, 0x80, 0x00), Minus = Color.FromArgb(0xC8, 0x00, 0x00);

    /// <summary>
    /// 枠の一辺・線の太さ・線の左右の空き。16px で (10, 4, 0) を基準に比率で伸ばす（四捨五入は整数演算で 0.5 を切り上げ）。
    /// 16px では細い線（太さ 2・空き 2）だと＋が読めなかったので、線は内側いっぱい（空き 0）に太く引く。
    /// 線が内側（Box - 2）の中央に来るよう、Box - 2 と Stroke の偶奇をそろえる。そろわなければ Stroke を 1 減らす。
    /// </summary>
    internal static (int Box, int Stroke, int Inset) Metrics(int iconSize)
    {
        var box = (iconSize * 10 + 8) / 16;
        var stroke = (box * 4 + 5) / 10;
        if ((box - 2) % 2 != stroke % 2) stroke--;
        return (box, stroke, 0);
    }

    /// <summary>読み込んだ Bitmap へ直接描く（新しい Bitmap は作らない）。Standard は何もしない。</summary>
    internal static void Draw(Bitmap icon, BookmarkDropMode mode, bool highContrast)
    {
        if (mode is not (BookmarkDropMode.Copy or BookmarkDropMode.Move)) return;   // 定義外の値は Standard と同じ（何も描かない）
        var (box, stroke, inset) = Metrics(icon.Width);
        var frame = highContrast ? SystemColors.WindowText : Frame;
        var fill = highContrast ? SystemColors.Window : Color.White;
        var line = highContrast ? SystemColors.WindowText : mode == BookmarkDropMode.Copy ? Plus : Minus;
        var (x, y) = (icon.Width - box, icon.Height - box);
        var inner = box - 2;
        var length = inner - 2 * inset;
        var mid = (inner - stroke) / 2;
        using var g = Graphics.FromImage(icon);
        g.SmoothingMode = SmoothingMode.None;   // 線がぼけると 16px で＋が読めない
        using (var b = new SolidBrush(frame)) g.FillRectangle(b, x, y, box, box);
        using (var b = new SolidBrush(fill)) g.FillRectangle(b, x + 1, y + 1, inner, inner);
        using var l = new SolidBrush(line);
        g.FillRectangle(l, x + 1 + inset, y + 1 + mid, length, stroke);   // 横線（＋と−に共通）
        if (mode == BookmarkDropMode.Copy) g.FillRectangle(l, x + 1 + mid, y + 1 + inset, stroke, length);   // 縦線（＋だけ）
    }
}
