namespace ReTAC.Domain.Listing;

/// <summary>R-114 / Q23 / Q34: 詳細表示の見出しのクリック。ソートのキーは増やさない。</summary>
public static class HeaderSort
{
    /// <summary>見出しが示すキー。種類は拡張子順（Q23）。作成日時・属性は null（クリックしても何もしない）。</summary>
    public static SortKey? KeyOf(DetailsColumn? column) => column switch
    {
        null => SortKey.Name,
        DetailsColumn.Extension or DetailsColumn.Type => SortKey.Extension,
        DetailsColumn.Size => SortKey.Size,
        DetailsColumn.Modified => SortKey.Date,
        _ => null,
    };

    /// <summary>Q34: キーが違えばキーだけ変えて向きは保つ（「表示」メニューのキーの項目と同じ）。同じなら向きを反転する。比べ方は変えない。</summary>
    /// <returns>何もしない列なら null</returns>
    public static SortOrder? Click(SortOrder current, DetailsColumn? column)
    {
        if (KeyOf(column) is not { } key) return null;
        if (key != current.Key) return current with { Key = key };
        return current with
        {
            Direction = current.Direction == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending,
        };
    }

    /// <summary>今のキーを示す見出しに ▲▼。並べ替えないときはどこにも出さない。</summary>
    public static bool ShowsArrow(SortOrder current, DetailsColumn? column) =>
        current.Key != SortKey.None && KeyOf(column) == current.Key;
}
