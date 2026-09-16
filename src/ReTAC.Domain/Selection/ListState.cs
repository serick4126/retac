using ReTAC.Domain.Entries;
using ReTAC.Domain.Listing;

namespace ReTAC.Domain.Selection;

/// <summary>
/// カーソルとマークの状態。<b>カーソル移動はマークを変更しない（R-11）。</b>
/// マークが変化するのは R-11-2 が列挙する操作のみ。
/// </summary>
public sealed class ListState
{
    private readonly List<Entry> _entries;
    private readonly HashSet<int> _marks = [];

    public ListState(IEnumerable<Entry> entries)
    {
        _entries = entries.ToList();
    }

    public IReadOnlyList<Entry> Entries => _entries;
    public int Count => _entries.Count;
    public int CursorIndex { get; private set; }
    public IReadOnlySet<int> Marks => _marks;

    public Entry? Cursor => _entries.Count == 0 ? null : _entries[CursorIndex];

    /// <summary>R-11: マークを一切変更しない。</summary>
    public void MoveCursor(int index)
    {
        if (_entries.Count == 0) { CursorIndex = 0; return; }
        // ↑↓のラップアラウンドは OFF（14.1 節）。端で止める
        CursorIndex = Math.Clamp(index, 0, _entries.Count - 1);
    }

    public void MoveCursorBy(int delta) => MoveCursor(CursorIndex + delta);

    /// <summary>
    /// R-01-5: 隣の列の同じ高さへ（← →）。<b>そこに項目が無ければ動かさない。</b>
    /// ↑↓ や PageUp/PageDown と違って端で丸めない。丸めると左端で ← が先頭へ、
    /// 右端で → が末尾へ飛んでしまう（利用者の実機確認による）。
    /// </summary>
    public void MoveCursorToNeighborColumn(int delta)
    {
        var target = CursorIndex + delta;
        if (target >= 0 && target < _entries.Count) CursorIndex = target;
    }

    /// <summary>R-11-3: マークをトグルしてカーソルを次の行へ進める。</summary>
    public void ToggleMarkAndAdvance()
    {
        ToggleMark(CursorIndex);
        MoveCursorBy(1);
    }

    public void ToggleMark(int index)
    {
        if (!CanMark(index)) return;
        if (!_marks.Remove(index)) _marks.Add(index);
    }

    /// <summary>全選択／全解除（0x8307）。1 件でもマークがあれば全解除、なければ全選択。</summary>
    public void ToggleAllMarks()
    {
        if (_marks.Count > 0) { _marks.Clear(); return; }
        // 14.1 節「全選択時のフォルダはファイルと同じ扱い」ON のためフォルダも含める
        for (var i = 0; i < _entries.Count; i++)
            if (CanMark(i)) _marks.Add(i);
    }

    /// <summary>反転選択（0x8312）。</summary>
    public void InvertMarks()
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            if (!CanMark(i)) continue;
            if (!_marks.Remove(i)) _marks.Add(i);
        }
    }

    /// <summary>ワイルドカードで選択（0x8326）。一致するものをマークに<b>追加</b>する。</summary>
    public void MarkByWildcard(string pattern)
    {
        for (var i = 0; i < _entries.Count; i++)
            if (CanMark(i) && Wildcard.IsMatch(_entries[i].Name, pattern)) _marks.Add(i);
    }

    /// <summary>同じ拡張子で選択（0x8325）。カーソル位置と同じ拡張子をマークに追加する。</summary>
    public void MarkBySameExtension()
    {
        if (Cursor is not { } cursor || cursor.IsParent) return;
        for (var i = 0; i < _entries.Count; i++)
        {
            if (!CanMark(i)) continue;
            if (string.Equals(_entries[i].Extension, cursor.Extension, StringComparison.OrdinalIgnoreCase))
                _marks.Add(i);
        }
    }

    /// <summary>Shift+クリックによる範囲マーク（R-11-2）。</summary>
    public void MarkRange(int from, int to)
    {
        var (lo, hi) = from <= to ? (from, to) : (to, from);
        for (var i = lo; i <= hi; i++)
            if (CanMark(i)) _marks.Add(i);
    }

    /// <summary>R-11-4: フォルダを移動するとマークは解除される。</summary>
    public void ClearMarks() => _marks.Clear();

    /// <summary>R-10: マークが 1 件以上ならマーク集合、0 件ならカーソル位置 1 件。</summary>
    public IReadOnlyList<Entry> EffectiveTarget()
    {
        if (_marks.Count > 0)
            return _marks.Order().Select(i => _entries[i]).ToList();
        if (Cursor is { IsParent: false } cursor)
            return [cursor];
        return [];
    }

    /// <summary>
    /// Shift+英字による頭出し（卓駆）。インクリメンタルサーチではなく、
    /// その頭文字を持つ<b>次の</b>項目へ現在の並び順で下方向に進み、末尾まで来たら先頭へ回る。
    /// フォルダも対象。英字で始まらない名前（日本語など）には当たらない。
    /// </summary>
    /// <returns>移動先の添字。該当が無ければ -1</returns>
    public int IndexOfNextStartingWith(char letter)
    {
        var initial = char.ToUpperInvariant(letter);
        for (var step = 1; step <= _entries.Count; step++)
        {
            var index = (CursorIndex + step) % _entries.Count;
            if (!CanMark(index)) continue;                       // 親フォルダ項目は飛ばす
            var name = _entries[index].Name;
            if (name.Length > 0 && char.ToUpperInvariant(name[0]) == initial) return index;
        }
        return -1;
    }

    /// <summary>R-04: 親フォルダ項目は選択の対象にならない。</summary>
    private bool CanMark(int index) =>
        index >= 0 && index < _entries.Count && !_entries[index].IsParent;
}
