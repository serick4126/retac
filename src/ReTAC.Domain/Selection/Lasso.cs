using ReTAC.Domain.Listing;

namespace ReTAC.Domain.Selection;

/// <summary>
/// R-120 / INV-MARKS-EXPLICIT-ONLY: 投げ縄。矩形は中身の座標で持つ（自動スクロールで画面の外へ出た項目も含めるため）。
/// ドラッグの間はマークを変えず、Commit でまとめて確定する。追加か解除かは始めた時点の Ctrl で決め、途中では変えない。
/// マークを入れ替えない（追加は外さず、解除は付けない）。
/// </summary>
public sealed class Lasso
{
    private readonly int _startX, _startY;
    private int _x, _y;

    public Lasso(int x, int y, bool remove) => (_startX, _startY, _x, _y, Remove) = (x, y, x, y, remove);

    public bool Remove { get; }

    public void Move(int x, int y) => (_x, _y) = (x, y);

    /// <summary>始めた点と今の点を対角にした矩形。両端の点を含む。</summary>
    public (int X, int Y, int Width, int Height) Rect =>
        (Math.Min(_startX, _x), Math.Min(_startY, _y), Math.Abs(_x - _startX) + 1, Math.Abs(_y - _startY) + 1);

    public IReadOnlyList<int> Covered(IFileViewLayout layout, int entryCount)
    {
        var (x, y, w, h) = Rect;
        return layout.IndexesIn(x, y, w, h, entryCount);
    }

    /// <returns>マークを変えたか</returns>
    public bool Commit(ListState state, IFileViewLayout layout)
    {
        var before = state.Marks.ToHashSet();
        var covered = Covered(layout, state.Count);
        if (Remove) state.RemoveMarks(covered);
        else state.AddMarks(covered);
        return !before.SetEquals(state.Marks);
    }
}
