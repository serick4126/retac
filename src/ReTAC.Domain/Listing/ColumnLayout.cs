namespace ReTAC.Domain.Listing;

/// <summary>
/// R-01-3（ジャストフィット）・R-01-6（拡張子の固定オフセット）のレイアウト計算。
/// 列幅はウィンドウ幅から独立し、フォルダを開くたびに再計算する（N-04-3）。
/// R-66-3: すべて実測値と DPI から算出し、物理ピクセルを直書きしない。
/// 文字の実測は呼び出し側（描画層）が行い、ここには結果の数値だけを渡す。
/// </summary>
public sealed record ColumnLayout : IFileViewLayout
{
    public required int RowHeight { get; init; }
    public required int IconWidth { get; init; }
    /// <summary>B-07: 列の先頭と末尾に置く余白。卓駆と同じく、左端にアイコンが接しないようにする。</summary>
    public required int ColumnPadding { get; init; }
    /// <summary>列の先頭から拡張子を描き始めるまでの距離（R-01-6）。</summary>
    public required int ExtensionOffset { get; init; }
    public required int ColumnWidth { get; init; }
    public required int RowsPerColumn { get; init; }
    public required int ColumnCount { get; init; }

    public int TotalWidth => ColumnWidth * ColumnCount;

    public int ColumnOf(int index) => index / RowsPerColumn;
    public int RowOf(int index) => index % RowsPerColumn;

    public int XOf(int index) => ColumnOf(index) * ColumnWidth;
    public int YOf(int index) => RowOf(index) * RowHeight;

    /// <summary>指定座標（スクロール量を加算済み）にあるエントリの添字。該当なしなら -1。</summary>
    public int IndexAt(int x, int y, int entryCount)
    {
        if (x < 0 || y < 0) return -1;
        var row = y / RowHeight;
        if (row >= RowsPerColumn) return -1;
        var index = x / ColumnWidth * RowsPerColumn + row;
        return index < entryCount ? index : -1;
    }

    /// <summary>R-110-2: 項目の矩形（中身の座標）。落とす先の枠に使う。</summary>
    public (int X, int Y, int Width, int Height) ItemBounds(int index) => (XOf(index), YOf(index), ColumnWidth, RowHeight);

    /// <summary>R-110-3: 一覧は横スクロールだけなので、左右の端（項目 1 行分の高さ）だけで横の向きを返す。縦は常に 0。</summary>
    public (int X, int Y) AutoScrollDirection(int x, int y, int viewportWidth, int viewportHeight) =>
        (x < RowHeight ? -1 : x >= viewportWidth - RowHeight ? 1 : 0, 0);

    /// <summary>R-110-3: 一覧の 1 段は横に 1 列。左端は常に列の境界（列幅の途中で止めない）。</summary>
    public (int X, int Y) ScrollOffset(ScrollPosition position) => (position.X * ColumnWidth, 0);

    /// <summary>丸ごと収まる列の数だけ見せた状態が、いちばん後ろ。端数の列は右端で切れる。縦には動かない。</summary>
    public ScrollPosition MaxScrollPosition(int entryCount, int viewportWidth, int viewportHeight) =>
        new(Math.Max(0, ColumnCount - Math.Max(1, viewportWidth / Math.Max(1, ColumnWidth))), 0);

    public static readonly ColumnLayout Empty = new()
    {
        RowHeight = 1,
        IconWidth = 0,
        ColumnPadding = 0,
        ExtensionOffset = 0,
        ColumnWidth = 1,
        RowsPerColumn = 1,
        ColumnCount = 0,
    };

    /// <param name="maxBaseWidth">全エントリの基底名の実測幅の最大値。</param>
    /// <param name="maxExtensionWidth">全エントリの拡張子の実測幅の最大値。</param>
    /// <param name="lineHeight">フォントの実測行高。</param>
    /// <param name="columnPadding">B-07: 列の先頭と末尾の余白。</param>
    public static ColumnLayout Compute(
        int entryCount,
        int maxBaseWidth,
        int maxExtensionWidth,
        int lineHeight,
        int viewportHeight,
        int iconWidth,
        int gap,
        int rowPadding,
        int columnPadding)
    {
        var rowHeight = Math.Max(1, lineHeight + rowPadding);
        // 高さが 0 でも 1 行分は確保して除算を守る
        var rowsPerColumn = Math.Max(1, viewportHeight / rowHeight);
        // B-07: pad + icon + gap + base + gap
        var extensionOffset = columnPadding + iconWidth + gap + maxBaseWidth + gap;

        return new ColumnLayout
        {
            RowHeight = rowHeight,
            IconWidth = iconWidth,
            ColumnPadding = columnPadding,
            ExtensionOffset = extensionOffset,
            // B-07: … + ext + pad
            ColumnWidth = extensionOffset + maxExtensionWidth + columnPadding,
            RowsPerColumn = rowsPerColumn,
            ColumnCount = entryCount == 0 ? 0 : (entryCount + rowsPerColumn - 1) / rowsPerColumn,
        };
    }
}
