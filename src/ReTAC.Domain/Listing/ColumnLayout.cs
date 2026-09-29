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
    /// <param name="maxTextWidth">R-113: 名前の文字（本体 + 間 + 拡張子）の幅の上限。null なら上限なし。</param>
    public static ColumnLayout Compute(
        int entryCount,
        int maxBaseWidth,
        int maxExtensionWidth,
        int lineHeight,
        int viewportHeight,
        int iconWidth,
        int gap,
        int rowPadding,
        int columnPadding,
        int? maxTextWidth = null)
    {
        var rowHeight = Math.Max(1, lineHeight + rowPadding);
        // 高さが 0 でも 1 行分は確保して除算を守る
        var rowsPerColumn = Math.Max(1, viewportHeight / rowHeight);
        var textStart = columnPadding + iconWidth + gap;
        var naturalText = maxBaseWidth + gap + maxExtensionWidth;
        // R-113: 上限を超えたら本体の幅だけを縮め、拡張子は揃えた位置（R-01-6）に残す。
        // 拡張子だけで上限を超えるときは本体の幅を 0 にし、拡張子も列の中で「…」になる（描く側）
        var text = maxTextWidth is { } cap && naturalText > cap ? cap : naturalText;
        var baseWidth = text == naturalText ? maxBaseWidth : Math.Max(0, text - gap - maxExtensionWidth);
        var extensionOffset = Math.Min(textStart + baseWidth + gap, textStart + text);

        return new ColumnLayout
        {
            RowHeight = rowHeight,
            IconWidth = iconWidth,
            ColumnPadding = columnPadding,
            ExtensionOffset = extensionOffset,
            // B-07: pad + icon + gap + text + pad
            ColumnWidth = textStart + text + columnPadding,
            RowsPerColumn = rowsPerColumn,
            ColumnCount = entryCount == 0 ? 0 : (entryCount + rowsPerColumn - 1) / rowsPerColumn,
            HorizontalBar = false,
        };
    }

    /// <summary>
    /// 横スクロールバーが要るかを決め、要るならその高さを引いた行数で計算し直す。
    /// 行数が減ると列が増えるだけなので、要る・要らないは 1 回目の結果から変わらない（2 回で確定する）。
    /// </summary>
    public static ColumnLayout ComputeFitted(int entryCount, int maxBaseWidth, int maxExtensionWidth, int lineHeight,
        int clientWidth, int clientHeight, int horizontalBarHeight, int iconWidth, int gap, int rowPadding, int columnPadding,
        int? maxTextWidth)
    {
        var layout = Compute(entryCount, maxBaseWidth, maxExtensionWidth, lineHeight, clientHeight,
            iconWidth, gap, rowPadding, columnPadding, maxTextWidth);
        if (layout.TotalWidth <= clientWidth) return layout;
        return Compute(entryCount, maxBaseWidth, maxExtensionWidth, lineHeight, Math.Max(0, clientHeight - horizontalBarHeight),
            iconWidth, gap, rowPadding, columnPadding, maxTextWidth) with { HorizontalBar = true };
    }

    /// <summary>横スクロールバーを出すか（ComputeFitted が決める）。</summary>
    public bool HorizontalBar { get; init; }

    /// <summary>既存の呼び出し用。HitTest の添字だけ。</summary>
    public int IndexAt(int x, int y, int entryCount) => HitTest(x, y, entryCount).Index;

    public int HeaderHeight => 0;
    public IReadOnlyList<HeaderCell> Header => [];
    public (bool Horizontal, bool Vertical) ScrollBars => (HorizontalBar, false);
    public bool ArrowsScrollHorizontally => false;

    /// <summary>R-11-2 / B-07: 左の余白もアイコンの当たり判定に含める（卓駆も左端の余白でマークが切り替わる）。名前の右の空きは名前（Q26）。</summary>
    public (int Index, FileViewArea Area) HitTest(int x, int y, int entryCount)
    {
        if (x < 0 || y < 0) return (-1, FileViewArea.None);
        var row = y / RowHeight;
        if (row >= RowsPerColumn) return (-1, FileViewArea.None);
        var index = x / ColumnWidth * RowsPerColumn + row;
        if (index >= entryCount) return (-1, FileViewArea.None);
        return (index, x % ColumnWidth < ColumnPadding + IconWidth ? FileViewArea.MarkIcon : FileViewArea.Name);
    }

    public (int X, int Y, int Width, int Height) IconBounds(int index) =>
        (XOf(index) + ColumnPadding, YOf(index) + (RowHeight - Math.Min(IconWidth, RowHeight)) / 2, IconWidth, Math.Min(IconWidth, RowHeight));

    /// <summary>名前の領域。アイコンの右から列の右端の余白の手前まで（名前の右の空きを含む。Q26）。</summary>
    public (int X, int Y, int Width, int Height) NameBounds(int index) =>
        (XOf(index) + ColumnPadding + IconWidth, YOf(index), Math.Max(0, ColumnWidth - ColumnPadding * 2 - IconWidth), RowHeight);

    /// <summary>R-01-6: 拡張子を描く領域。列の先頭からの一定のオフセットから、列の右端の余白の手前まで。</summary>
    public (int X, int Y, int Width, int Height) ExtensionBounds(int index) =>
        (XOf(index) + ExtensionOffset, YOf(index), Math.Max(0, ColumnWidth - ColumnPadding - ExtensionOffset), RowHeight);

    public IReadOnlyList<int> IndexesIn(int x, int y, int width, int height, int entryCount)
    {
        if (width <= 0 || height <= 0 || entryCount == 0 || x + width <= 0 || y + height <= 0) return [];
        var firstColumn = Math.Max(0, x / ColumnWidth);
        var lastColumn = Math.Min(ColumnCount - 1, (x + width - 1) / ColumnWidth);
        var firstRow = Math.Max(0, y / RowHeight);
        var lastRow = Math.Min(RowsPerColumn - 1, (y + height - 1) / RowHeight);
        var result = new List<int>();
        for (var column = firstColumn; column <= lastColumn; column++)
            for (var row = firstRow; row <= lastRow; row++)
            {
                var index = column * RowsPerColumn + row;
                if (index < entryCount) result.Add(index);
            }
        return result;
    }

    /// <summary>V3 / R-01-5: 上下は 1 つずつ。左右は隣の列の同じ高さで、隣に項目が無ければ動かさない。</summary>
    public int Arrow(int index, int dx, int dy, int entryCount)
    {
        if (entryCount == 0) return index;
        if (dy != 0) return Math.Clamp(index + dy, 0, entryCount - 1);
        var target = index + dx * RowsPerColumn;
        return target >= 0 && target < entryCount ? target : index;
    }

    /// <summary>ponytail: 1 画面分 = 列数 × 行数。卓駆の実測と食い違うようなら 1 列分に変える。</summary>
    public int PageItems(int viewportWidth, int viewportHeight) => RowsPerColumn * Math.Max(1, viewportWidth / Math.Max(1, ColumnWidth));

    public (int X, int Y) VisibleSteps(int viewportWidth, int viewportHeight) => (Math.Max(1, viewportWidth / Math.Max(1, ColumnWidth)), 0);

    /// <summary>カーソルの列が見えていなければ、その列が端に来るまで動かす。見えていれば何もしない（R-10）。</summary>
    public ScrollPosition Reveal(int index, ScrollPosition current, int viewportWidth, int viewportHeight)
    {
        if (index < 0) return current;
        var visible = Math.Max(1, viewportWidth / Math.Max(1, ColumnWidth));
        var column = ColumnOf(index);
        if (column < current.X) return current with { X = column };
        if (column > current.X + visible - 1) return current with { X = column - visible + 1 };
        return current;
    }
}
