using System.Drawing;
using ReTAC.Domain.Entries;

namespace ReTAC.App.Rendering;

/// <summary>
/// R-11-6 / R-31 / R-110-2: ファイルリストの行の地と文字の色。落とす先の枠は文字の色で描く。
/// 1 つの色（通常の文字色・カーソルの地・OS の選択色）に固定すると、ある行で地と同じ色になって枠が消える。
/// 文字の色はどの配色でもその行の地と見分けられるように決めてあるので、枠も地に埋もれない。
/// </summary>
internal static class RowColors
{
    public static (Color Background, Color Foreground) Of(Theme theme, AttributeColor attribute, bool isCursor, bool isMarked)
    {
        // R-11-6: カーソルとマークが重なる行は地をカーソル色にする
        var background = isCursor ? theme.CursorBackground : isMarked ? theme.MarkBackground : theme.Background;
        if (isCursor) return (background, theme.CursorForeground);
        // R-31: 既定では属性配色より選択の配色を優先する
        if (isMarked && !theme.SeparateMarkColorFromAttributes) return (background, theme.MarkForeground);
        var attributeColor = theme.ForAttribute(attribute);
        return (background, isMarked && attributeColor == theme.Foreground ? theme.MarkForeground : attributeColor);
    }

    /// <summary>
    /// R-110-2: 落とす先の枠の色。ふだんは文字の色。独自の配色では文字と地を同じ色にできるので、そのときは地の明るさの反対の黒か白にする
    /// （文字は読めなくても、落とす先は見えるように）。
    /// </summary>
    public static Color Frame(Color background, Color foreground) =>
        foreground.ToArgb() != background.ToArgb() ? foreground
        : background.GetBrightness() < 0.5f ? Color.White : Color.Black;
}
