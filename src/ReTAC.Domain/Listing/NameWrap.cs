using System.Globalization;

namespace ReTAC.Domain.Listing;

/// <summary>
/// R-119: アイコン表示の名前の折り返し。行数まで書記素の境目で折り返し、あふれたら最後の行で本体の末尾を「…」にして拡張子を残す。
/// 拡張子（tail）は揃えず、本体に続けて描く（Q35）。隠した拡張子（R-01-7）は呼び出し側が tail を空にして渡す。
/// ponytail: 語の境目を見ずに書記素ごとに詰める。日本語の名前がほとんどなので、英語の語の途中の折り返しが気になるようなら空白を優先する形に広げる。
/// </summary>
public static class NameWrap
{
    public sealed record Result(IReadOnlyList<string> Lines, bool Truncated);

    private const string Ellipsis = "…";

    public static Result Lines(string body, string tail, int width, int maxLines, Func<string, int> measure)
    {
        var elements = Graphemes(body + tail);
        var lines = new List<string>();
        var starts = new List<int>();
        var start = 0;
        while (start < elements.Count && lines.Count < maxLines)
        {
            var end = Fit(elements, start, width, measure);
            starts.Add(start);
            lines.Add(string.Concat(elements.Skip(start).Take(end - start)));
            start = end;
        }
        // 幅より広い書記素 1 つを押し込んだ行があれば、描くとはみ出すので省略した扱い（R-113 のツールチップ・ステータスバー）
        if (start >= elements.Count) return new Result(lines, lines.Any(line => measure(line) > width));

        // あふれた: 最後の行を、そこから先の本体を「…」で省いて拡張子を残した形に置き換える
        lines[^1] = LastLine(elements, starts[^1], Graphemes(body).Count, tail, width, measure);
        return new Result(lines, true);
    }

    /// <summary>start から、幅に収まる所まで（最低 1 書記素。幅 0 でも先へ進むため）。</summary>
    private static int Fit(List<string> elements, int start, int width, Func<string, int> measure)
    {
        var end = start + 1;
        while (end < elements.Count && measure(string.Concat(elements.Skip(start).Take(end + 1 - start))) <= width) end++;
        return end;
    }

    private static string LastLine(List<string> elements, int lineStart, int bodyCount, string tail, int width, Func<string, int> measure)
    {
        var suffix = Ellipsis + tail;
        if (measure(suffix) > width)
        {
            // 拡張子そのものが入らない: 行の頭から詰めて、全体の末尾を「…」
            var all = elements.Skip(lineStart).ToList();
            for (var n = all.Count; n > 0; n--)
            {
                var text = string.Concat(all.Take(n)) + Ellipsis;
                if (measure(text) <= width) return text;
            }
            return Ellipsis;
        }
        var bodyPart = elements.Skip(lineStart).Take(Math.Max(0, bodyCount - lineStart)).ToList();
        for (var n = bodyPart.Count; n > 0; n--)
        {
            var text = string.Concat(bodyPart.Take(n)) + suffix;
            if (measure(text) <= width) return text;
        }
        return suffix;
    }

    private static List<string> Graphemes(string text)
    {
        var result = new List<string>();
        var e = StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext()) result.Add((string)e.Current);
        return result;
    }
}
