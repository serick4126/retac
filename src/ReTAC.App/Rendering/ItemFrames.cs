using System.Drawing;
using System.Drawing.Drawing2D;

namespace ReTAC.App.Rendering;

/// <summary>
/// R-116: マークのチェックボックスとカーソルの枠。色はその項目の文字の色（R-110-2 の落とす先の枠と同じ規則。RowColors.Frame）。
/// チェックボックスは、項目の地の色で不透明な下地を描いてから枠とチェックを描く（下がサムネイルでも見えるように）。
/// </summary>
public static class ItemFrames
{
    public static (Color Fill, Color Stroke) CheckBoxColors(Color background, Color foreground) =>
        (Color.FromArgb(255, background), RowColors.Frame(background, foreground));

    public static void DrawCheckBox(Graphics g, Rectangle box, Color background, Color foreground, bool isChecked, int stroke)
    {
        var (fill, line) = CheckBoxColors(background, foreground);
        using (var brush = new SolidBrush(fill)) g.FillRectangle(brush, box);
        using var pen = new Pen(line, stroke) { Alignment = PenAlignment.Inset };
        g.DrawRectangle(pen, InsideStroke(box));   // DrawRectangle は右・下を 1px 外へ描くので、下地の内に収める
        if (!isChecked) return;
        var s = box.Width;
        var smoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var check = new Pen(line, Math.Max(stroke, s / 8f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLines(check, [
            new PointF(box.X + s * 0.22f, box.Y + s * 0.52f),
            new PointF(box.X + s * 0.42f, box.Y + s * 0.72f),
            new PointF(box.X + s * 0.78f, box.Y + s * 0.30f),
        ]);
        g.SmoothingMode = smoothing;
    }

    public static Color CursorFrameColor(Color background, Color foreground) => RowColors.Frame(background, foreground);

    public static void DrawCursorFrame(Graphics g, Rectangle item, Color background, Color foreground, int width)
    {
        using var pen = new Pen(CursorFrameColor(background, foreground), width) { Alignment = PenAlignment.Inset };
        g.DrawRectangle(pen, InsideStroke(item));
    }

    private static Rectangle InsideStroke(Rectangle r) => new(r.X, r.Y, Math.Max(0, r.Width - 1), Math.Max(0, r.Height - 1));

    /// <summary>R-120: 投げ縄の枠は背景に対する文字の色（RowColors.Frame）、塗りは同じ色の不透明度 25%。</summary>
    public static (Color Stroke, Color Fill) LassoColors(Theme theme)
    {
        var stroke = RowColors.Frame(theme.Background, theme.Foreground);
        return (stroke, Color.FromArgb(64, stroke));
    }
}
