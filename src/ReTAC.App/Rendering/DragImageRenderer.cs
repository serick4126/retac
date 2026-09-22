using System.Drawing;
using System.Windows.Forms;

namespace ReTAC.App.Rendering;

/// <summary>
/// R-78 / Q83: ドラッグ中にカーソルへ付ける小さな画像(アイコン＋名前)を作る。
/// FileListView(ファイル一覧)とツリー(DriveTreeView)の両方が同じ見た目で使う。
/// 既定の大きなドラッグ画像は指す先を隠す(Phase10.2 の実機確認で NG)ため、必ず小さい画像を渡す側で使うこと。
/// </summary>
internal static class DragImageRenderer
{
    internal static Bitmap Render(Image? icon, int iconSize, string text, Font font, Color foreground, Color background, int gap)
    {
        var textSize = TextRenderer.MeasureText(text, font, Size.Empty, TextMeasure.Flags);
        var width = Math.Max(1, iconSize + gap + textSize.Width + gap);
        var height = Math.Max(1, Math.Max(iconSize, textSize.Height));

        var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(background);
        if (icon is not null) g.DrawImage(icon, new Rectangle(0, (height - iconSize) / 2, iconSize, iconSize));
        TextRenderer.DrawText(g, text, font, new Point(iconSize + gap, (height - textSize.Height) / 2), foreground, TextMeasure.Flags);
        return bitmap;
    }
}
