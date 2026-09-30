namespace ReTAC.Domain.Listing;

/// <summary>R-114: 列 1 つの入力（幅はすべてその dpi のピクセル）。Column が null なら名前の列（常に先頭）。</summary>
/// <param name="AutoWidth">自動の幅（中身の最長。名前の列は方式〔R-113〕を当てた後の幅）</param>
/// <param name="MinWidth">見出しの文字・ソートの印・余白が収まる幅（ドラッグでもこれより狭くしない）</param>
/// <param name="ManualWidth">見出しの境界のドラッグで決めた幅。null なら自動（V6）</param>
public readonly record struct DetailsColumnInput(DetailsColumn? Column, int AutoWidth, int MinWidth, int? ManualWidth);

public sealed record DetailsLayoutInput
{
    public int EntryCount { get; init; }
    public int RowHeight { get; init; }
    public int IconWidth { get; init; }
    public int ColumnPadding { get; init; }
    public int HeaderHeight { get; init; }
    /// <summary>横のスクロールの 1 段（一覧のフォントの数字 0 の幅の 4 文字分）。</summary>
    public int StepWidth { get; init; }
    /// <summary>表示する列だけを表示の順に。[0] は名前の列。</summary>
    public IReadOnlyList<DetailsColumnInput> Columns { get; init; } = [];
    public bool FitToWindow { get; init; }
    public int ClientWidth { get; init; }
    public int ClientHeight { get; init; }
    public int VerticalBarWidth { get; init; }
    public int HorizontalBarHeight { get; init; }
    /// <summary>名前のセルの左端から、揃えた拡張子（R-01-6）を描き始める位置。名前に拡張子を出さないなら 0。</summary>
    public int ExtensionOffset { get; init; }
}

/// <summary>
/// R-114 / V1 / V6: 詳細表示のレイアウト。行全体が項目（INV-DETAILS-ROW-HIT）。縦は 1 段 1 行、横は 1 段 StepWidth。
/// </summary>
public sealed record DetailsLayout : IFileViewLayout
{
    public required int RowHeight { get; init; }
    public required int IconWidth { get; init; }
    public required int ColumnPadding { get; init; }
    public required int HeaderHeight { get; init; }
    public required int StepWidth { get; init; }
    public required IReadOnlyList<HeaderCell> Header { get; init; }
    public required int EntryCount { get; init; }
    public required int ExtensionOffset { get; init; }
    public required int ViewportWidth { get; init; }
    public required int ViewportHeight { get; init; }
    public required (bool Horizontal, bool Vertical) ScrollBars { get; init; }

    public int TotalWidth => Header.Count == 0 ? 0 : Header[^1].X + Header[^1].Width;
    public bool ArrowsScrollHorizontally => true;
    private int NameWidth => Header.Count == 0 ? 0 : Header[0].Width;
    private int VisibleRows(int viewportHeight) => Math.Max(1, viewportHeight / RowHeight);

    /// <summary>
    /// 仕様の計算の順: ①縦スクロールバーが要るかを行数で決める ②その幅に列を合わせる ③合計がまだ広ければ横スクロールバー
    /// ④横スクロールバーで高さが減って縦が要るようになったら、①からもう一度だけ計算する（2 回目で確定し、行き来しない）。
    /// </summary>
    public static DetailsLayout Compute(DetailsLayoutInput input)
    {
        var rowsHeight = input.EntryCount * Math.Max(1, input.RowHeight);
        var itemsHeight = Math.Max(0, input.ClientHeight - input.HeaderHeight);
        var vertical = rowsHeight > itemsHeight;
        var (widths, horizontal) = Fit(input, vertical);
        if (horizontal && !vertical && rowsHeight > itemsHeight - input.HorizontalBarHeight)
        {
            vertical = true;
            (widths, horizontal) = Fit(input, vertical);
        }

        var header = new List<HeaderCell>(widths.Count);
        var x = 0;
        for (var i = 0; i < widths.Count; i++)
        {
            header.Add(new HeaderCell(input.Columns[i].Column, x, widths[i]));
            x += widths[i];
        }

        return new DetailsLayout
        {
            RowHeight = Math.Max(1, input.RowHeight),
            IconWidth = input.IconWidth,
            ColumnPadding = input.ColumnPadding,
            HeaderHeight = input.HeaderHeight,
            StepWidth = Math.Max(1, input.StepWidth),
            Header = header,
            EntryCount = input.EntryCount,
            ExtensionOffset = input.ExtensionOffset,
            ViewportWidth = Math.Max(0, input.ClientWidth - (vertical ? input.VerticalBarWidth : 0)),
            ViewportHeight = Math.Max(0, itemsHeight - (horizontal ? input.HorizontalBarHeight : 0)),
            ScrollBars = (horizontal, vertical),
        };

        static (IReadOnlyList<int> Widths, bool Horizontal) Fit(DetailsLayoutInput input, bool vertical)
        {
            var available = Math.Max(0, input.ClientWidth - (vertical ? input.VerticalBarWidth : 0));
            var widths = ResolveWidths(input.Columns, input.FitToWindow, available);
            return (widths, widths.Sum() > available);
        }
    }

    /// <summary>
    /// V6: 手動の幅があればそれ、無ければ自動。どちらも最小幅より狭くしない。
    /// 合わせる設定のときは表示のときだけ縮める（入力の手動の幅は変えない）: まず名前の列を最小幅まで、
    /// それでも入らなければ右の列から順に最小幅まで。最小幅まで縮めても入らなければそのまま（横スクロールが残る）。
    /// </summary>
    public static IReadOnlyList<int> ResolveWidths(IReadOnlyList<DetailsColumnInput> columns, bool fit, int available)
    {
        var widths = columns.Select(c => Math.Max(c.MinWidth, c.ManualWidth ?? c.AutoWidth)).ToArray();
        if (!fit) return widths;
        var excess = widths.Sum() - available;
        foreach (var i in new[] { 0 }.Concat(Enumerable.Range(1, Math.Max(0, widths.Length - 1)).Reverse()))
        {
            if (excess <= 0 || i >= widths.Length) break;
            var shrink = Math.Min(excess, widths[i] - columns[i].MinWidth);
            widths[i] -= shrink;
            excess -= shrink;
        }
        return widths;
    }

    public (int Index, FileViewArea Area) HitTest(int x, int y, int entryCount)
    {
        if (x < 0 || y < 0 || x >= TotalWidth) return (-1, FileViewArea.None);
        var row = y / RowHeight;
        if (row >= entryCount) return (-1, FileViewArea.None);
        // R-11-2 / B-07: 左の余白もアイコン。名前の列の中の余白は名前（Q31）。境界の 1 ピクセルは右の領域
        if (x < ColumnPadding + IconWidth) return (row, FileViewArea.MarkIcon);
        return (row, x < NameWidth ? FileViewArea.Name : FileViewArea.Other);
    }

    public (int X, int Y, int Width, int Height) ItemBounds(int index) => (0, index * RowHeight, TotalWidth, RowHeight);

    public (int X, int Y, int Width, int Height) IconBounds(int index) =>
        (ColumnPadding, index * RowHeight + (RowHeight - Math.Min(IconWidth, RowHeight)) / 2, IconWidth, Math.Min(IconWidth, RowHeight));

    public (int X, int Y, int Width, int Height) NameBounds(int index) =>
        (ColumnPadding + IconWidth, index * RowHeight, Math.Max(0, NameWidth - ColumnPadding * 2 - IconWidth), RowHeight);

    public (int X, int Y, int Width, int Height) ExtensionBounds(int index) => ExtensionOffset <= 0
        ? (NameWidth - ColumnPadding, index * RowHeight, 0, RowHeight)
        : (Math.Min(ExtensionOffset, NameWidth - ColumnPadding), index * RowHeight,
           Math.Max(0, NameWidth - ColumnPadding - ExtensionOffset), RowHeight);

    public IReadOnlyList<int> IndexesIn(int x, int y, int width, int height, int entryCount)
    {
        if (width <= 0 || height <= 0 || entryCount == 0 || x >= TotalWidth || x + width <= 0 || y + height <= 0) return [];
        var first = Math.Max(0, y / RowHeight);
        var last = Math.Min(entryCount - 1, (y + height - 1) / RowHeight);
        return first > last ? [] : Enumerable.Range(first, last - first + 1).ToList();
    }

    public int Arrow(int index, int dx, int dy, int entryCount) =>
        entryCount == 0 || dy == 0 ? index : Math.Clamp(index + dy, 0, entryCount - 1);

    public int PageItems(int viewportWidth, int viewportHeight) => VisibleRows(viewportHeight);

    public (int X, int Y) VisibleSteps(int viewportWidth, int viewportHeight) =>
        (Math.Max(1, viewportWidth / StepWidth), VisibleRows(viewportHeight));

    /// <summary>計画の既定: 縦だけ動かし、横は保つ（行はどの列も同じ項目なので、横を戻すと右の列を読めなくなる）。</summary>
    public ScrollPosition Reveal(int index, ScrollPosition current, int viewportWidth, int viewportHeight)
    {
        if (index < 0) return current;
        var rows = VisibleRows(viewportHeight);
        if (index < current.Y) return current with { Y = index };
        if (index > current.Y + rows - 1) return current with { Y = index - rows + 1 };
        return current;
    }

    /// <summary>横の最後の段が半端なら、内容の右端が表示の右端に来る位置で止める（Q28）。</summary>
    public (int X, int Y) ScrollOffset(ScrollPosition position) =>
        (Math.Min(position.X * StepWidth, Math.Max(0, TotalWidth - ViewportWidth)), position.Y * RowHeight);

    public ScrollPosition MaxScrollPosition(int entryCount, int viewportWidth, int viewportHeight)
    {
        var overflow = Math.Max(0, TotalWidth - viewportWidth);
        return new((overflow + StepWidth - 1) / StepWidth, Math.Max(0, entryCount - VisibleRows(viewportHeight)));
    }

    public int AutoScrollBand => RowHeight;

    /// <summary>R-110-3: 端（項目 1 行分の高さ）で縦横それぞれの向き。横は横スクロールバーがあるときだけ。</summary>
    public (int X, int Y) AutoScrollDirection(int x, int y, int viewportWidth, int viewportHeight) => (
        !ScrollBars.Horizontal ? 0 : x < RowHeight ? -1 : x >= viewportWidth - RowHeight ? 1 : 0,
        y < RowHeight ? -1 : y >= viewportHeight - RowHeight ? 1 : 0);

    public (int X, int Y, int Width, int Height)? CellBounds(int index, DetailsColumn column)
    {
        foreach (var cell in Header)
            if (cell.Column == column)
                return (cell.X + ColumnPadding, index * RowHeight, Math.Max(0, cell.Width - ColumnPadding * 2), RowHeight);
        return null;
    }

    public int HeaderBorderAt(int x, int tolerance) => HeaderBorderAt(Header, x, tolerance);

    public int HeaderCellAt(int x) => HeaderCellAt(Header, x);

    /// <returns>x がセルの右の境界から tolerance 以内なら、そのセルの添字。無ければ -1</returns>
    public static int HeaderBorderAt(IReadOnlyList<HeaderCell> header, int x, int tolerance)
    {
        for (var i = 0; i < header.Count; i++)
            if (Math.Abs(x - (header[i].X + header[i].Width)) <= tolerance) return i;
        return -1;
    }

    public static int HeaderCellAt(IReadOnlyList<HeaderCell> header, int x)
    {
        for (var i = 0; i < header.Count; i++)
            if (x >= header[i].X && x < header[i].X + header[i].Width) return i;
        return -1;
    }
}
