namespace ReTAC.Domain.Navigation;

/// <summary>R-89: 落とす位置。Onto なら Index の項目の上、そうでなければ Index の前へ入れる。</summary>
public readonly record struct DropSpot(int Index, bool Onto);

/// <summary>項目の上（中央 1/3）に落としたときに起こすこと。</summary>
public enum OntoAction { None, IntoGroup, Transfer }

/// <summary>R-89: バー（横）とグループのメニュー（縦）の上で、ドラッグしている位置から落とす先を決める（§6.10）。</summary>
public static class BookmarkDrop
{
    /// <summary>
    /// §6.10 / §7.1: 項目の上（中央 1/3）に落としたとき。<b>どの種類も中央は「項目の上」</b>で、登録・並べ替えの挿入は両端と項目の間だけ
    /// （バーの上では HasCenter をすべて true にして Hit を呼ぶ）。グループは中へ入れる、フォルダはファイルなら転送（R-93）、
    /// ファイル・コマンドは落とせない（利用者の決定。9.3 のレビューで仕様書どおりにした）。
    /// </summary>
    /// <param name="reorder">ブックマークの並べ替え（ファイルのドラッグではない）</param>
    public static OntoAction Onto(BookmarkKind kind, bool reorder) => kind switch
    {
        BookmarkKind.Group => OntoAction.IntoGroup,
        BookmarkKind.Folder when !reorder => OntoAction.Transfer,
        _ => OntoAction.None,
    };

    /// <param name="slots">
    /// 並んでいる項目の始まりと終わり（横なら左右、縦なら上下）。前から順に並んでいること。
    /// HasCenter の項目は中央の 1/3 を「項目の上」とし、残りの両端を「間」とする
    /// </param>
    /// <param name="position">ドラッグしている位置（slots と同じ軸）</param>
    public static DropSpot Hit(IReadOnlyList<(int Start, int End, bool HasCenter)> slots, int position)
    {
        for (var i = 0; i < slots.Count; i++)
        {
            var (start, end, hasCenter) = slots[i];
            if (position >= end) continue;
            if (hasCenter)
            {
                var third = (end - start) / 3;
                if (position >= start + third && position < end - third) return new DropSpot(i, Onto: true);
            }
            // 前半なら前、後半なら後ろ（項目より手前の隙間も前）
            return new DropSpot(position < (start + end) / 2 ? i : i + 1, Onto: false);
        }
        return new DropSpot(slots.Count, Onto: false);
    }
}
