using ReTAC.Domain.Entries;

namespace ReTAC.Domain.Listing;

/// <summary>R-04 / R-05: 親フォルダ項目を先頭に固定し、フォルダ優先で並べる。</summary>
public static class FolderListing
{
    /// <param name="entries">親フォルダ項目を含んでいてもよい。含まれていれば先頭へ移す。</param>
    public static IReadOnlyList<Entry> Sort(IEnumerable<Entry> entries, SortOrder order)
    {
        var all = entries.ToList();
        var result = new List<Entry>(all.Count);

        // R-04: 親フォルダ項目はソートの影響を受けず常に先頭
        result.AddRange(all.Where(e => e.IsParent));

        var comparer = Comparer(order);
        // R-05: フォルダ優先。R-05-4: 属性はグループ化に使わない
        result.AddRange(SortGroup(all.Where(e => e.Kind == EntryKind.Folder), comparer, order.Key));
        result.AddRange(SortGroup(all.Where(e => e.Kind == EntryKind.File), comparer, order.Key));
        return result;
    }

    private static IEnumerable<Entry> SortGroup(IEnumerable<Entry> group, IComparer<Entry> comparer, SortKey key) =>
        key == SortKey.None ? group : group.OrderBy(e => e, comparer);

    private static IComparer<Entry> Comparer(SortOrder order)
    {
        var names = NameComparers.For(order.Mode);
        var sign = order.Direction == SortDirection.Ascending ? 1 : -1;

        return Comparer<Entry>.Create((a, b) =>
        {
            var c = order.Key switch
            {
                SortKey.Name => names.Compare(a.Name, b.Name),
                // R-05-2-3: 拡張子にも自然順が効く（.r01 .r02 … .r10）
                SortKey.Extension => names.Compare(a.Extension, b.Extension),
                SortKey.Size => a.Size.CompareTo(b.Size),
                SortKey.Date => a.LastWriteTime.CompareTo(b.LastWriteTime),
                _ => 0,
            };
            // R-05-2-3: 同着は名前で決着させる。ここにも比較方式が効く
            if (c == 0 && order.Key != SortKey.Name) c = names.Compare(a.Name, b.Name);
            return sign * c;
        });
    }
}
