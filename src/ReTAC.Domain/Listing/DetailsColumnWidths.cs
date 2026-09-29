namespace ReTAC.Domain.Listing;

/// <summary>
/// R-114 / V6: 詳細表示の手動の列幅（見出しの境界のドラッグで決める。実行中に保存する値なので FileViews の外の最上位のキー）。
/// 論理列 ID → 96 dpi の論理ピクセル。キーが無い列は自動。
/// </summary>
public static class DetailsColumnWidths
{
    /// <summary>手で書いた極端な値で描画が壊れないための上限（論理ピクセル）。</summary>
    public const int Max = 8192;

    /// <summary>論理列 ID。名前の列は "Name"、ほかは DetailsColumn の名前（設定ファイルに載るので変えない）。</summary>
    public static string Key(DetailsColumn? column) => column?.ToString() ?? "Name";

    /// <summary>知らない列・0 以下・null を捨て（自動に戻る）、上限で止める。最小幅はフォントで決まるので、ここでは見ない（描くときに広げる）。</summary>
    public static Dictionary<string, int?> Normalize(IDictionary<string, int?>? widths)
    {
        var known = new[] { Key(null) }.Concat(Enum.GetNames<DetailsColumn>()).ToHashSet();
        return (widths ?? new Dictionary<string, int?>())
            .Where(p => known.Contains(p.Key) && p.Value is > 0)
            .ToDictionary(p => p.Key, p => (int?)Math.Min(p.Value!.Value, Max));
    }

    public static int ToPixels(int logical, int dpi) => (int)Math.Round(logical * dpi / 96.0, MidpointRounding.AwayFromZero);
    public static int ToLogical(int pixels, int dpi) => (int)Math.Round(pixels * 96.0 / dpi, MidpointRounding.AwayFromZero);
}
