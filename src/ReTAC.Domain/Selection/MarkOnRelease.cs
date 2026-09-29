using ReTAC.Domain.Listing;

namespace ReTAC.Domain.Selection;

/// <summary>
/// R-11-2 / INV-MARKS-EXPLICIT-ONLY: 行頭アイコンのクリック・Shift+クリックのマークは、押した時点ではなく、動かさずに離した時点で変える。
/// 押した時点ではカーソルだけ移す。押したまま動かして D&amp;D になったら（落としても Esc で取り消しても）変えない。
/// Shift は押した時点の値。アイコンの上でも Shift が先（範囲マーク）。
/// 名前以外（詳細表示の Q24）は押した時点ではカーソルも動かさず、離した時点で移す。動いたら捨てる（Moved）。
/// </summary>
public sealed class MarkOnRelease
{
    /// <summary>MoveOnRelease: 名前以外（INV-DETAILS-ROW-HIT）。離したらカーソルを移す。動いたら捨てる。</summary>
    private enum Kind { None, Toggle, Range, MoveOnRelease }
    private Kind _kind;
    private int _index, _anchor;

    /// <param name="area">押した所の種類（レイアウトの HitTest）。Ctrl はクリックでは見ない（Q32 / R-11-2）</param>
    public void Press(ListState state, int index, FileViewArea area, bool shift)
    {
        _anchor = state.CursorIndex;
        _index = index;
        _kind = shift ? Kind.Range : area switch
        {
            FileViewArea.MarkIcon => Kind.Toggle,
            FileViewArea.Other => Kind.MoveOnRelease,
            _ => Kind.None,
        };
        // R-11-2: Shift の押下ではカーソルを動かさない。押す前の位置が範囲の起点として残る必要があるため
        // （動かしてしまうと Release で MarkRange するときに起点を見失う）。カーソルは離した時点で動く。
        // 名前以外は押しただけでは動かさない（INV-DETAILS-ROW-HIT。動かしたら投げ縄になるため）
        if (!shift && area != FileViewArea.Other) state.MoveCursor(index);
    }

    public void DragStarted() => _kind = Kind.None;

    /// <summary>
    /// 閾値を超えて動いた。名前以外で押したときは、保留していたカーソルの移動と範囲マークを捨てる
    /// （Phase 15 の間の形。Phase 16 でここに投げ縄の開始が入る）。行頭アイコン・名前では D&amp;D になるので DragStarted と同じ。
    /// </summary>
    public void Moved() => _kind = Kind.None;

    /// <summary>
    /// 押している最中に一覧が入れ替わった（自動更新など）。保留していた `_index` / `_anchor` は
    /// もう別の項目を指しているので、離した時点で別のファイルをマークしてしまわないよう捨てる
    /// （R-11-2 / INV-MARKS-EXPLICIT-ONLY）。
    /// </summary>
    public void Cancel() => _kind = Kind.None;

    /// <returns>マークを変えたか。名前以外の保留でカーソルだけ動いたときは false（呼び出し側はカーソルの位置を比べる）</returns>
    public bool Release(ListState state)
    {
        var kind = _kind;
        _kind = Kind.None;
        switch (kind)
        {
            case Kind.Toggle: state.ToggleMark(_index); return true;
            // 範囲をマークしてから、カーソルを押した項目へ動かす（R-11-2: 押した時点では動かさなかった分）
            case Kind.Range: state.MarkRange(_anchor, _index); state.MoveCursor(_index); return true;
            case Kind.MoveOnRelease: state.MoveCursor(_index); return false;
            default: return false;
        }
    }
}
