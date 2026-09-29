using ReTAC.Domain.Entries;
using ReTAC.Domain.Listing;

namespace ReTAC.App.Rendering;

/// <summary>R-01-6 / R-01-7: 名前の描き方。Hidden は本体だけ、Together は本体に拡張子を続ける、Aligned は拡張子を揃えた位置に置く。</summary>
public enum ExtensionStyle { Aligned, Together, Hidden }

/// <summary>
/// 全エントリの文字幅を計測し、ColumnLayout に渡す数値を作る。
/// 計測は必ず TextMeasure（計測専用の 1x1 Graphics）で行う（R-66-3）。
/// </summary>
public static class EntryMetrics
{
    /// <param name="maxTextWidth">R-113: 名前の文字の上限（NameWidths.TextCap）。すべて表示なら null</param>
    public static ColumnLayout Layout(IReadOnlyList<Entry> entries, TextMeasure measure,
        int clientWidth, int clientHeight, int horizontalBarHeight, int iconWidth, int gap, int rowPadding, int columnPadding,
        int? maxTextWidth, ExtensionStyle style = ExtensionStyle.Aligned)
    {
        var maxBase = 0;
        var maxExtension = 0;
        foreach (var entry in entries)
        {
            maxBase = Math.Max(maxBase, measure.Width(style == ExtensionStyle.Together ? entry.Name : entry.BaseName));
            if (style == ExtensionStyle.Aligned) maxExtension = Math.Max(maxExtension, measure.Width(entry.Extension));
        }

        return ColumnLayout.ComputeFitted(entries.Count, maxBase, maxExtension, measure.LineHeight(),
            clientWidth, clientHeight, horizontalBarHeight, iconWidth, gap, rowPadding, columnPadding, maxTextWidth);
    }
}
