namespace ReTAC.Domain.Listing;

public enum SortKey { Name, Extension, Size, Date, None }

public enum SortDirection { Ascending, Descending }

/// <summary>R-05-2: ソートキーから独立した第 3 の軸。</summary>
public enum ComparisonMode { Strict, Natural }

/// <summary>R-05-2: ソートキー × ソート方向 × 比較方式 の 3 軸。</summary>
public sealed record SortOrder(SortKey Key, SortDirection Direction, ComparisonMode Mode)
{
    /// <summary>R-05-2-4 / 16.5 節: 名前・昇順・自然順。</summary>
    public static readonly SortOrder Default = new(SortKey.Name, SortDirection.Ascending, ComparisonMode.Natural);
}
