using ReTAC.Domain.Entries;

namespace ReTAC.Domain.Selection;

/// <summary>
/// R-80: インクリメンタルサーチの一致。前方一致・大文字小文字を区別しない。
/// 親フォルダ項目は除く（名前が ".." なので、除かないと "." の入力で当たる。頭出しの規則とも揃える）。
/// マークには一切触れない（R-11）。
/// </summary>
public static class IncrementalMatch
{
    /// <returns>一致した添字（一覧の並び順）。<paramref name="text"/> が空なら空</returns>
    public static IReadOnlyList<int> Find(IReadOnlyList<Entry> entries, string text)
    {
        if (text.Length == 0) return [];
        var hits = new List<int>();
        for (var i = 0; i < entries.Count; i++)
            if (!entries[i].IsParent && entries[i].Name.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                hits.Add(i);
        return hits;
    }

    /// <summary>
    /// 入力が変わったときのカーソルの行き先。今の項目がまだ一致していれば動かさない
    /// （↓ で選んだ項目が、1 文字足しただけで先頭の一致へ引き戻されないように）。
    /// </summary>
    /// <returns>行き先の添字。一致が無ければ -1（カーソルは動かさない）</returns>
    public static int Pick(IReadOnlyList<int> matches, int cursor) =>
        matches.Count == 0 ? -1 : matches.Contains(cursor) ? cursor : matches[0];

    /// <summary>↓ / ↑。カーソルより後 / 前で最も近い一致。端まで行ったら反対の端へ回る。</summary>
    /// <returns>行き先の添字。一致が無ければ -1</returns>
    public static int Step(IReadOnlyList<int> matches, int cursor, bool forward)
    {
        if (matches.Count == 0) return -1;
        if (forward)
        {
            foreach (var index in matches)
                if (index > cursor) return index;
            return matches[0];
        }
        for (var i = matches.Count - 1; i >= 0; i--)
            if (matches[i] < cursor) return matches[i];
        return matches[^1];
    }
}
