using ReTAC.Domain.Listing;

namespace ReTAC.Domain.Selection;

/// <summary>
/// R-120: 投げ縄の入力の状態。押した所が項目の無い所（詳細表示では名前以外も）で、ドラッグの閾値を超えたら始める。
/// 追加か解除かは押した時点の Ctrl で決め、Move・Release は修飾キーを受け取らない（途中で押し直しても変わらない）。
/// ハンドルを持たないので、FileListView のマウス・キーの配線の判断をここでテストする。
/// </summary>
public sealed class LassoGesture
{
    private (int ClientX, int ClientY, int ContentX, int ContentY, bool Ctrl)? _pending;

    public Lasso? Active { get; private set; }

    /// <param name="startsLasso">押した所から投げ縄を始められるか（HitTest が項目の無い所、詳細表示なら名前以外）</param>
    public void Press(int clientX, int clientY, int contentX, int contentY, bool startsLasso, bool ctrl)
    {
        Cancel();
        if (startsLasso) _pending = (clientX, clientY, contentX, contentY, ctrl);
    }

    /// <returns>投げ縄の間なら true（呼び出し側は D&amp;D・ツールチップに進まない）</returns>
    public bool Move(int clientX, int clientY, int contentX, int contentY, int dragWidth, int dragHeight)
    {
        if (Active is { } lasso) { lasso.Move(contentX, contentY); return true; }
        if (_pending is not { } p) return false;
        if (Math.Abs(clientX - p.ClientX) < dragWidth && Math.Abs(clientY - p.ClientY) < dragHeight) return false;
        Active = new Lasso(p.ContentX, p.ContentY, p.Ctrl);
        Active.Move(contentX, contentY);
        _pending = null;
        return true;
    }

    /// <summary>自動スクロールで中身がずれた。最後のマウスの位置から求め直した中身の座標で矩形を伸ばす。</summary>
    public void Scrolled(int contentX, int contentY) => Active?.Move(contentX, contentY);

    /// <summary>R-124: 一覧が入れ替わって中身がずれた。押しただけの保留も、始めた投げ縄も、同じだけ動かす。</summary>
    public void Shift(int dx, int dy)
    {
        if (_pending is { } p) _pending = p with { ContentX = p.ContentX + dx, ContentY = p.ContentY + dy };
        Active?.Shift(dx, dy);
    }

    /// <returns>マークを変えたか。投げ縄を始めていなければ何もしない</returns>
    public bool Release(ListState state, IFileViewLayout layout)
    {
        var lasso = Active;
        Cancel();
        return lasso?.Commit(state, layout) ?? false;
    }

    public void Cancel()
    {
        _pending = null;
        Active = null;
    }
}
