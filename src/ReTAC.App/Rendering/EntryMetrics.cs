using ReTAC.Domain.Entries;
using ReTAC.Domain.Listing;

namespace ReTAC.App.Rendering;

/// <summary>
/// 全エントリの文字幅を計測し、ColumnLayout に渡す数値を作る。
/// 計測は必ず TextMeasure（計測専用の 1x1 Graphics）で行う（R-66-3）。
/// </summary>
public static class EntryMetrics
{
    public static ColumnLayout Layout(
        IReadOnlyList<Entry> entries,
        TextMeasure measure,
        int viewportHeight,
        int iconWidth,
        int gap,
        int rowPadding,
        int columnPadding)
    {
        var maxBase = 0;
        var maxExtension = 0;
        foreach (var entry in entries)
        {
            maxBase = Math.Max(maxBase, measure.Width(entry.BaseName));
            maxExtension = Math.Max(maxExtension, measure.Width(entry.Extension));
        }

        return ColumnLayout.Compute(
            entries.Count, maxBase, maxExtension, measure.LineHeight(),
            viewportHeight, iconWidth, gap, rowPadding, columnPadding);
    }
}
