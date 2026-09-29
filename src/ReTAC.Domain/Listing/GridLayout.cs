namespace ReTAC.Domain.Listing;

/// <summary>R-119 / R-120: 格子の並べ方。小アイコンはアイコンが左、中・大・特大はアイコンが上。</summary>
public enum GridArrangement { IconLeft, IconTop }

/// <summary>GridLayout の入力。文字の実測・dpi の換算は呼び出し側（描画層）が行い、結果の数値だけを渡す（R-66-3）。</summary>
public sealed record GridLayoutInput
{
    public required int EntryCount { get; init; }
    public required GridArrangement Arrangement { get; init; }
    public required int IconSize { get; init; }
    public required int LineHeight { get; init; }
    /// <summary>R-119: 名前の行数（IconTop）。IconLeft は 1。</summary>
    public required int NameLines { get; init; }
    /// <summary>名前の領域の幅。パネルより広ければ Compute が縮める。</summary>
    public required int TextWidth { get; init; }
    public required int PaddingX { get; init; }
    public required int PaddingY { get; init; }
    /// <summary>項目の間と外周の余白。ここは「項目の無い所」（投げ縄を始められる）。</summary>
    public required int Gap { get; init; }
    /// <summary>R-116: チェックボックスの 1 辺。0 なら無し（小アイコン）。</summary>
    public required int CheckBoxSize { get; init; }
    public required int ClientWidth { get; init; }
    public required int ClientHeight { get; init; }
    public required int VerticalBarWidth { get; init; }
    /// <summary>R-110-3: 自動スクロールの上下の端の幅。</summary>
    public required int EdgeBand { get; init; }
}

/// <summary>
/// R-119 / R-120 / INV-LAYOUT-GEOMETRY-SINGLE-SOURCE: 小〜特大アイコンの格子。行ごとに左から右へ並べ、縦にスクロールする。
/// 横にはスクロールしない（項目がパネルより広ければ縮める）。縦のスクロールの 1 段は格子の 1 行。
/// </summary>
public sealed record GridLayout : IFileViewLayout
{
    public required GridArrangement Arrangement { get; init; }
    public required int IconSize { get; init; }
    public required int TextWidth { get; init; }
    public required int NameHeight { get; init; }
    public required int PaddingX { get; init; }
    public required int PaddingY { get; init; }
    public required int Gap { get; init; }
    public required int CheckBoxSize { get; init; }
    public required int CellWidth { get; init; }
    public required int CellHeight { get; init; }
    public required int Columns { get; init; }
    public required int Rows { get; init; }
    public required bool VerticalBar { get; init; }
    public required int EdgeBand { get; init; }
    /// <summary>項目を描ける高さ（見出しは無い）。</summary>
    public required int ViewportHeight { get; init; }

    public int PitchX => CellWidth + Gap;
    public int PitchY => CellHeight + Gap;

    public static readonly GridLayout Empty = Compute(new GridLayoutInput
    {
        EntryCount = 0, Arrangement = GridArrangement.IconTop, IconSize = 1, LineHeight = 1, NameLines = 1, TextWidth = 1,
        PaddingX = 0, PaddingY = 0, Gap = 0, CheckBoxSize = 0, ClientWidth = 1, ClientHeight = 1, VerticalBarWidth = 0, EdgeBand = 1,
    });

    public static GridLayout Compute(GridLayoutInput input)
    {
        var layout = Fit(input, input.ClientWidth);
        // 縦のバーが要るなら、その幅を引いて列を数え直す。列が減ると行が増えるだけなので、要るという答えは変わらない
        // 項目が無ければ窓の大きさによらずバーを出さない（外周の余白だけで高さを超えても要らない）
        return input.EntryCount == 0 || layout.Gap + layout.Rows * layout.PitchY <= input.ClientHeight
            ? layout
            : Fit(input, Math.Max(0, input.ClientWidth - input.VerticalBarWidth)) with { VerticalBar = true };
    }

    private static GridLayout Fit(GridLayoutInput input, int width)
    {
        var gap = input.Gap;
        var lines = input.Arrangement == GridArrangement.IconLeft ? 1 : Math.Max(1, input.NameLines);
        var nameHeight = lines * Math.Max(1, input.LineHeight);
        // 名前の外側（余白・アイコン）の幅。IconLeft はアイコンの右にも横の余白を置く
        var outside = input.Arrangement == GridArrangement.IconLeft ? input.PaddingX * 3 + input.IconSize : input.PaddingX * 2;
        var maxCell = Math.Max(1, width - gap * 2);
        var text = Math.Max(0, Math.Min(input.TextWidth, maxCell - outside));
        var cellWidth = Math.Max(1, Math.Min(maxCell, outside + text));
        var cellHeight = input.Arrangement == GridArrangement.IconLeft
            ? Math.Max(input.IconSize, nameHeight) + input.PaddingY * 2
            : input.PaddingY * 3 + input.IconSize + nameHeight;
        var columns = Math.Max(1, (width - gap) / Math.Max(1, cellWidth + gap));
        var rows = input.EntryCount == 0 ? 0 : (input.EntryCount + columns - 1) / columns;
        return new GridLayout
        {
            Arrangement = input.Arrangement, IconSize = input.IconSize, TextWidth = text, NameHeight = nameHeight,
            PaddingX = input.PaddingX, PaddingY = input.PaddingY, Gap = gap, CheckBoxSize = input.CheckBoxSize,
            CellWidth = cellWidth, CellHeight = Math.Max(1, cellHeight), Columns = columns, Rows = rows,
            VerticalBar = false, EdgeBand = input.EdgeBand, ViewportHeight = input.ClientHeight,
        };
    }

    private int ColumnOf(int index) => index % Columns;
    private int RowOf(int index) => index / Columns;
    private int VisibleRows(int viewportHeight) => Math.Max(1, (viewportHeight - Gap) / Math.Max(1, PitchY));

    public (int X, int Y, int Width, int Height) ItemBounds(int index) =>
        (Gap + ColumnOf(index) * PitchX, Gap + RowOf(index) * PitchY, CellWidth, CellHeight);

    public (int X, int Y, int Width, int Height) IconBounds(int index)
    {
        var (x, y, w, h) = ItemBounds(index);
        return Arrangement == GridArrangement.IconLeft
            ? (x + PaddingX, y + (h - IconSize) / 2, IconSize, IconSize)
            : (x + (w - IconSize) / 2, y + PaddingY, IconSize, IconSize);
    }

    /// <summary>R-119: 名前の領域（IconTop は折り返しの行をすべて含む）。</summary>
    public (int X, int Y, int Width, int Height) NameBounds(int index)
    {
        var (x, y, w, h) = ItemBounds(index);
        return Arrangement == GridArrangement.IconLeft
            ? (x + PaddingX * 2 + IconSize, y, Math.Max(0, w - PaddingX * 3 - IconSize), h)
            : (x + PaddingX, y + PaddingY * 2 + IconSize, Math.Max(0, w - PaddingX * 2), NameHeight);
    }

    /// <summary>Q35: 格子は拡張子を揃えない。</summary>
    public (int X, int Y, int Width, int Height) ExtensionBounds(int index)
    {
        var (x, y, _, _) = ItemBounds(index);
        return (x, y, 0, 0);
    }

    public (int X, int Y, int Width, int Height)? CheckBoxBounds(int index)
    {
        if (CheckBoxSize <= 0) return null;
        var (x, y, _, _) = ItemBounds(index);
        return (x + PaddingY, y + PaddingY, CheckBoxSize, CheckBoxSize);
    }

    public (int Index, FileViewArea Area) HitTest(int x, int y, int entryCount)
    {
        if (x < Gap || y < Gap || entryCount == 0) return (-1, FileViewArea.None);
        var (column, inX) = (Math.DivRem(x - Gap, PitchX, out var rx), rx);
        var (row, inY) = (Math.DivRem(y - Gap, PitchY, out var ry), ry);
        if (column >= Columns || inX >= CellWidth || inY >= CellHeight) return (-1, FileViewArea.None);
        var index = row * Columns + column;
        if (index >= entryCount) return (-1, FileViewArea.None);
        if (CheckBoxBounds(index) is { } box && x >= box.X && x < box.X + box.Width && y >= box.Y && y < box.Y + box.Height)
            return (index, FileViewArea.CheckBox);
        // R-11-2 / B-07: 小アイコンは左の余白も行頭アイコン（一覧と同じ）
        if (Arrangement == GridArrangement.IconLeft && inX < PaddingX + IconSize) return (index, FileViewArea.MarkIcon);
        return (index, FileViewArea.Name);
    }

    public IReadOnlyList<int> IndexesIn(int x, int y, int width, int height, int entryCount)
    {
        if (width <= 0 || height <= 0 || entryCount == 0) return [];
        var firstRow = Math.Max(0, (y - Gap - CellHeight) / PitchY);
        var lastRow = Math.Min(Rows - 1, Math.Max(-1, (y + height - 1 - Gap) / PitchY));
        var result = new List<int>();
        for (var row = firstRow; row <= lastRow; row++)
            for (var column = 0; column < Columns; column++)
            {
                var index = row * Columns + column;
                if (index >= entryCount) break;
                var (bx, by, bw, bh) = ItemBounds(index);
                // R-120: 1px でも交われば囲んだことにする
                if (bx < x + width && x < bx + bw && by < y + height && y < by + bh) result.Add(index);
            }
        return result;
    }

    /// <summary>R-120 / V3: 左右は並び順の前後（行の端で隣の行へ）。上下は同じ列。下の行にその列が無ければ最後の項目。</summary>
    public int Arrow(int index, int dx, int dy, int entryCount)
    {
        if (entryCount == 0) return index;
        if (dx != 0) return Math.Clamp(index + dx, 0, entryCount - 1);
        var target = index + dy * Columns;
        if (target < 0) return index;
        if (target >= entryCount) return dy > 0 && RowOf(index) < RowOf(entryCount - 1) ? entryCount - 1 : index;
        return target;
    }

    public (int X, int Y) AutoScrollDirection(int x, int y, int viewportWidth, int viewportHeight) =>
        (0, y < EdgeBand ? -1 : y >= viewportHeight - EdgeBand ? 1 : 0);

    public (int X, int Y) ScrollOffset(ScrollPosition position) => (0, position.Y * PitchY);

    public ScrollPosition MaxScrollPosition(int entryCount, int viewportWidth, int viewportHeight) =>
        new(0, Math.Max(0, Rows - VisibleRows(viewportHeight)));

    public int PageItems(int viewportWidth, int viewportHeight) => Columns * VisibleRows(viewportHeight);

    public (int X, int Y) VisibleSteps(int viewportWidth, int viewportHeight) => (0, VisibleRows(viewportHeight));

    /// <summary>R-10: カーソルの行が見えていなければ、その行が端に来るまで動かす。</summary>
    public ScrollPosition Reveal(int index, ScrollPosition current, int viewportWidth, int viewportHeight)
    {
        if (index < 0) return current;
        var row = RowOf(index);
        var visible = VisibleRows(viewportHeight);
        if (row < current.Y) return current with { Y = row };
        if (row > current.Y + visible - 1) return current with { Y = row - visible + 1 };
        return current;
    }

    public int HeaderHeight => 0;
    public IReadOnlyList<HeaderCell> Header => [];
    public (bool Horizontal, bool Vertical) ScrollBars => (false, VerticalBar);
    public (int X, int Y, int Width, int Height)? CellBounds(int index, DetailsColumn column) => null;
    public int HeaderBorderAt(int x, int tolerance) => -1;
    public int HeaderCellAt(int x) => -1;
    public bool ArrowsScrollHorizontally => false;
}
