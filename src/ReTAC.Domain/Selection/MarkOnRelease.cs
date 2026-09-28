namespace ReTAC.Domain.Selection;

/// <summary>
/// R-11-2 / INV-MARKS-EXPLICIT-ONLY: 行頭アイコンのクリック・Shift+クリックのマークは、押した時点ではなく、動かさずに離した時点で変える。
/// 押した時点ではカーソルだけ移す。押したまま動かして D&amp;D になったら（落としても Esc で取り消しても）変えない。
/// Shift は押した時点の値。アイコンの上でも Shift が先（範囲マーク）。
/// </summary>
public sealed class MarkOnRelease
{
    private enum Kind { None, Toggle, Range }
    private Kind _kind;
    private int _index, _anchor;

    public void Press(ListState state, int index, bool onIcon, bool shift)
    {
        _anchor = state.CursorIndex;
        _index = index;
        _kind = shift ? Kind.Range : onIcon ? Kind.Toggle : Kind.None;
        state.MoveCursor(index);
    }

    public void DragStarted() => _kind = Kind.None;

    /// <summary>
    /// 押している最中に一覧が入れ替わった（自動更新など）。保留していた `_index` / `_anchor` は
    /// もう別の項目を指しているので、離した時点で別のファイルをマークしてしまわないよう捨てる
    /// （R-11-2 / INV-MARKS-EXPLICIT-ONLY）。
    /// </summary>
    public void Cancel() => _kind = Kind.None;

    /// <returns>マークを変えたか</returns>
    public bool Release(ListState state)
    {
        var kind = _kind;
        _kind = Kind.None;
        switch (kind)
        {
            case Kind.Toggle: state.ToggleMark(_index); return true;
            case Kind.Range: state.MarkRange(_anchor, _index); return true;
            default: return false;
        }
    }
}
