using ReTAC.Domain.Entries;

namespace ReTAC.Domain.Selection;

/// <summary>
/// B-09 / R-70: 再表示（移動・削除・自動更新のあと）でカーソルをどこに置くか。
/// カーソルの項目が残っていればそこ。消えていれば、元の一覧でその上にあった項目のうち
/// <b>再表示後も残っている最も近いもの</b>。どれも残っていなければ先頭。
///
/// 「元の添字 − 1」で決めないのは、マークをまとめて移動すると 1 つ上も消えていることが多く、
/// 添字では新しい一覧の無関係な項目を指すため。名前で上へ辿れば並びの意味が保たれる。
/// </summary>
public static class CursorRestore
{
    /// <summary>カーソルの項目を先頭に、上へ向かって名前を並べる。親フォルダ項目は含めない。</summary>
    public static IReadOnlyList<string> NamesFromCursorUpward(ListState state)
    {
        var names = new List<string>();
        for (var i = Math.Min(state.CursorIndex, state.Count - 1); i >= 0; i--)
            if (!state.Entries[i].IsParent) names.Add(state.Entries[i].Name);
        return names;
    }

    /// <returns>新しい一覧での添字。見つからなければ 0（親フォルダ項目。ドライブルートでは先頭の項目）</returns>
    public static int IndexAfterReload(IReadOnlyList<string> namesFromCursorUpward, IReadOnlyList<Entry> entries)
    {
        // 名前 → 添字を 1 回だけ作る。上へ辿る長さと一覧の件数の積にしないため
        var indexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < entries.Count; i++)
            if (!entries[i].IsParent) indexByName.TryAdd(entries[i].Name, i);

        foreach (var name in namesFromCursorUpward)
            if (indexByName.TryGetValue(name, out var index)) return index;
        return 0;
    }
}
