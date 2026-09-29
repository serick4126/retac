using ReTAC.Domain.Entries;
using ReTAC.Domain.Listing;

namespace ReTAC.App.Rendering;

/// <summary>
/// 全エントリの文字幅を計測し、ColumnLayout に渡す数値を作る。
/// 計測は必ず TextMeasure（計測専用の 1x1 Graphics）で行う（R-66-3）。
/// </summary>
public static class EntryMetrics
{
    /// <param name="maxTextWidth">R-113: 名前の文字の上限（NameWidths.TextCap）。すべて表示なら null</param>
    public static ColumnLayout Layout(IReadOnlyList<Entry> entries, TextMeasure measure,
        int clientWidth, int clientHeight, int horizontalBarHeight, int iconWidth, int gap, int rowPadding, int columnPadding,
        int? maxTextWidth, bool alignExtension = true, Func<Entry, bool>? hidesExtension = null)
    {
        var maxBase = 0;
        var maxExtension = 0;
        foreach (var entry in entries)
        {
            // R-01-6 / R-01-7: 実際に描く文字で測る。隠す項目の拡張子は最長に入れず、揃えないなら拡張子込みの名前で測る
            var hidden = hidesExtension?.Invoke(entry) ?? false;
            maxBase = Math.Max(maxBase, measure.Width(hidden || alignExtension ? entry.BaseName : entry.Name));
            if (alignExtension && !hidden) maxExtension = Math.Max(maxExtension, measure.Width(entry.Extension));
        }

        return ColumnLayout.ComputeFitted(entries.Count, maxBase, maxExtension, measure.LineHeight(),
            clientWidth, clientHeight, horizontalBarHeight, iconWidth, gap, rowPadding, columnPadding, maxTextWidth);
    }
}
