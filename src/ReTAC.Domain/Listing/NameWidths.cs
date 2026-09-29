namespace ReTAC.Domain.Listing;

/// <summary>R-113: 名前の列の方式から、名前の文字（本体 + 間 + 拡張子）に使ってよい幅の上限を求める。</summary>
public static class NameWidths
{
    /// <param name="zeroWidth">一覧のフォントの数字 0 の幅（Q9）</param>
    /// <param name="panelTextWidth">「自動」のときの上限。ファイル表示パネルの幅から、名前の文字の外側（余白・アイコン）を引いたもの</param>
    /// <returns>すべて表示なら null（上限なし。R-01-3 / R-01-4）</returns>
    public static int? TextCap(NameWidthSetting setting, int zeroWidth, int panelTextWidth) => setting.Mode switch
    {
        NameWidthMode.Auto => Math.Max(1, panelTextWidth),
        NameWidthMode.MaxChars => Math.Max(1, zeroWidth * setting.MaxChars),
        _ => null,
    };
}
