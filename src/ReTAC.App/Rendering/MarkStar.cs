using System.Drawing;
using System.Drawing.Drawing2D;

namespace ReTAC.App.Rendering;

/// <summary>
/// マーク済みを示す赤い★（R-11-5 / R-34-3）。
///
/// <b>★はアイコンに差し替えるのではなく、アイコンの上に重ねる</b>（利用者による実機確認）。
/// フォルダ／ファイルのアイコンはそのまま残り、その上に乗る。
/// ファイルリストの行とステータスバーの ②③ 区画は「同じ作法」と決めてあるので、
/// 形の定義もここ 1 か所に置く（別々に持っていて片方だけ直すと食い違う。V-12）。
/// </summary>
public static class MarkStar
{
    /// <summary>アイコンの輪郭が残る大きさ。85% だとアイコンが完全に隠れる（卓駆と比較して決めた）。</summary>
    private const float Scale = 0.7f;

    /// <summary>内側の頂点の比率。星の細さを決める。</summary>
    private const float InnerRatio = 0.42f;

    /// <param name="iconRect">★を重ねる先のアイコンの矩形。</param>
    public static void Draw(Graphics g, Rectangle iconRect, Color color)
    {
        var previous = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var brush = new SolidBrush(color))
            g.FillPolygon(brush, Points(iconRect));
        g.SmoothingMode = previous;
    }

    private static PointF[] Points(Rectangle rect)
    {
        var cx = rect.X + rect.Width / 2f;
        var cy = rect.Y + rect.Height / 2f;
        var outer = rect.Width * Scale / 2f;
        var inner = outer * InnerRatio;

        var points = new PointF[10];
        for (var i = 0; i < 10; i++)
        {
            var radius = i % 2 == 0 ? outer : inner;
            // 真上から始めて 36 度ずつ回る
            var angle = -Math.PI / 2 + i * Math.PI / 5;
            points[i] = new PointF(cx + (float)(radius * Math.Cos(angle)), cy + (float)(radius * Math.Sin(angle)));
        }
        return points;
    }
}
