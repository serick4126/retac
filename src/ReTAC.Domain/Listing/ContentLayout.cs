namespace ReTAC.Domain.Listing;

/// <summary>ContentLayout の入力。文字の実測・dpi の換算は呼び出し側（描画層）が行い、結果の数値だけを渡す（R-66-3）。</summary>
public sealed record ContentLayoutInput
{
    public required int EntryCount { get; init; }
    public required int IconSize { get; init; }
    public required int LineHeight { get; init; }
    public required int PaddingX { get; init; }
    public required int PaddingY { get; init; }
    /// <summary>行と行の間の隙間（区切り線を引く所。パネルの地）。ここは「項目の無い所」。</summary>
    public required int Gap { get; init; }
    public required int CheckBoxSize { get; init; }
    /// <summary>R-122: 右の欄に出す情報（選んだ情報の 2 つ目・3 つ目）それぞれの、見出しと値が収まる幅。上の段から順に 0〜2 個。</summary>
    public required IReadOnlyList<int> RightWidths { get; init; }
    /// <summary>R-122: 左の欄の最小幅（名前を 10 文字ぶん）。</summary>
    public required int MinLeftWidth { get; init; }
    public required int ClientWidth { get; init; }
    public required int ClientHeight { get; init; }
    public required int VerticalBarWidth { get; init; }
    /// <summary>R-110-3: 自動スクロールの上下の端の幅。</summary>
    public required int EdgeBand { get; init; }
}

/// <summary>
/// R-122 / INV-LAYOUT-GEOMETRY-SINGLE-SOURCE / INV-DETAILS-ROW-HIT: コンテンツ。1 行 1 項目で縦にスクロールし、行はパネルの幅に合わせる（横スクロールしない）。
/// 行の中は左からアイコン、左の欄（1 行目に名前、2 行目に情報の 1 つ目）、右の欄（情報の 2 つ目・3 つ目）で、矩形は重ならない。
/// 狭いときは右の欄の情報を下の段から省き、それでも左の欄が最小幅に届かなければ右の欄を無くす。アイコンとチェックボックスは縮めず、
/// それも収まらないほど狭ければ行がパネルからはみ出す（コントロールが切る）。縦のスクロールの 1 段は 1 行。
/// </summary>
public sealed record ContentLayout : IFileViewLayout
{
    public required int IconSize { get; init; }
    public required int LineHeight { get; init; }
    public required int PaddingX { get; init; }
    public required int PaddingY { get; init; }
    public required int Gap { get; init; }
    public required int CheckBoxSize { get; init; }
    public required int RowWidth { get; init; }
    public required int RowHeight { get; init; }
    public required int LeftWidth { get; init; }
    public required int RightWidth { get; init; }
    /// <summary>右の欄に出す情報の数（0〜2）。上の段から数える。</summary>
    public required int RightCount { get; init; }
    public required int Rows { get; init; }
    public required bool VerticalBar { get; init; }
    public required int EdgeBand { get; init; }

    public int Pitch => RowHeight + Gap;

    public static readonly ContentLayout Empty = Compute(new ContentLayoutInput
    {
        EntryCount = 0, IconSize = 1, LineHeight = 1, PaddingX = 0, PaddingY = 0, Gap = 0, CheckBoxSize = 0, RightWidths = [],
        MinLeftWidth = 0, ClientWidth = 1, ClientHeight = 1, VerticalBarWidth = 0, EdgeBand = 1,
    });

    public static ContentLayout Compute(ContentLayoutInput input)
    {
        var rowHeight = Math.Max(1, Math.Max(input.IconSize, input.LineHeight * 2) + input.PaddingY * 2);
        var pitch = rowHeight + input.Gap;
        // R-122: 隙間は行と行の間だけ。最後の行の後に隙間を数えると、ちょうど収まる高さでバーが出る
        var bar = input.EntryCount > 0 && (long)input.EntryCount * rowHeight + (input.EntryCount - 1L) * input.Gap > input.ClientHeight;
        var width = Math.Max(0, input.ClientWidth - (bar ? input.VerticalBarWidth : 0));
        var leftStart = input.PaddingX * 2 + input.IconSize;
        // R-122: 右の欄は下の段から省く。k は右に出す数。どれでも最小幅に届かなければ右の欄を無くす
        var (count, right, left) = (0, 0, width - leftStart - input.PaddingX);
        for (var k = Math.Min(2, input.RightWidths.Count); k > 0; k--)
        {
            var w = input.RightWidths.Take(k).Max();
            var rest = width - leftStart - input.PaddingX - w - input.PaddingX;
            if (rest >= input.MinLeftWidth) { (count, right, left) = (k, w, rest); break; }
        }
        return new ContentLayout
        {
            IconSize = input.IconSize, LineHeight = Math.Max(1, input.LineHeight), PaddingX = input.PaddingX, PaddingY = input.PaddingY,
            Gap = input.Gap, CheckBoxSize = input.CheckBoxSize,
            // アイコンは縮めない。パネルより狭ければ行がはみ出す
            RowWidth = Math.Max(width, leftStart + input.PaddingX), RowHeight = rowHeight,
            LeftWidth = Math.Max(0, left), RightWidth = right, RightCount = count,
            Rows = input.EntryCount, VerticalBar = bar, EdgeBand = input.EdgeBand,
        };
    }

    /// <summary>丸ごと見える行の数。n 行は n × 行の高さ + (n − 1) × 隙間 に収まればよい（最後の行の後の隙間は要らない）。</summary>
    private int VisibleRows(int viewportHeight) => Math.Max(1, (viewportHeight + Gap) / Math.Max(1, Pitch));
    private int TextTop(int index) => index * Pitch + (RowHeight - LineHeight * 2) / 2;
    private static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);
    private static bool Contains((int X, int Y, int Width, int Height) r, int x, int y) =>
        x >= r.X && x < r.X + r.Width && y >= r.Y && y < r.Y + r.Height;

    public (int X, int Y, int Width, int Height) ItemBounds(int index) => (0, index * Pitch, RowWidth, RowHeight);

    public (int X, int Y, int Width, int Height) IconBounds(int index) =>
        (PaddingX, index * Pitch + (RowHeight - IconSize) / 2, IconSize, IconSize);

    /// <summary>R-122: 左の欄の 1 行目（名前）。INV-DETAILS-ROW-HIT の「名前の列」はこの矩形（文字の右の空きも含む）とアイコン。</summary>
    public (int X, int Y, int Width, int Height) NameBounds(int index) => (PaddingX * 2 + IconSize, TextTop(index), LeftWidth, LineHeight);

    /// <summary>R-122: 左の欄の 2 行目（選んだ情報の 1 つ目）。</summary>
    public (int X, int Y, int Width, int Height) LeftInfoBounds(int index) =>
        (PaddingX * 2 + IconSize, TextTop(index) + LineHeight, LeftWidth, LineHeight);

    /// <summary>R-122: 右の欄の row 段目（0 は選んだ情報の 2 つ目、1 は 3 つ目）。省いた段・無い段は null。</summary>
    public (int X, int Y, int Width, int Height)? RightInfoBounds(int index, int row) =>
        row >= 0 && row < RightCount ? (RowWidth - PaddingX - RightWidth, TextTop(index) + row * LineHeight, RightWidth, LineHeight) : null;

    /// <summary>R-122: その行と次の行の間の隙間（区切り線を引く所）。行の塗りとは重ならない。最後の行の下には無い（行と行の「間」だけ）。</summary>
    public (int X, int Y, int Width, int Height)? SeparatorBounds(int index) =>
        index >= 0 && index < Rows - 1 ? (0, index * Pitch + RowHeight, RowWidth, Gap) : null;

    /// <summary>Q35: コンテンツは拡張子を揃えない。</summary>
    public (int X, int Y, int Width, int Height) ExtensionBounds(int index) => (0, index * Pitch, 0, 0);

    public (int X, int Y, int Width, int Height)? CheckBoxBounds(int index) =>
        CheckBoxSize > 0 ? (PaddingY, index * Pitch + PaddingY, CheckBoxSize, CheckBoxSize) : null;

    /// <summary>INV-DETAILS-ROW-HIT: 行全体が項目。チェックボックス、アイコンと名前の欄（Name）、それ以外（Other）。行の間の隙間と最後の行より下は項目の無い所。</summary>
    public (int Index, FileViewArea Area) HitTest(int x, int y, int entryCount)
    {
        if (entryCount == 0 || x < 0 || y < 0 || x >= RowWidth) return (-1, FileViewArea.None);
        var index = Math.DivRem(y, Pitch, out var inY);
        if (index >= entryCount || inY >= RowHeight) return (-1, FileViewArea.None);
        if (CheckBoxBounds(index) is { } box && Contains(box, x, y)) return (index, FileViewArea.CheckBox);
        if (Contains(IconBounds(index), x, y) || Contains(NameBounds(index), x, y)) return (index, FileViewArea.Name);
        return (index, FileViewArea.Other);
    }

    public IReadOnlyList<int> IndexesIn(int x, int y, int width, int height, int entryCount)
    {
        if (width <= 0 || height <= 0 || entryCount == 0 || x >= RowWidth || x + width <= 0) return [];
        var first = Math.Max(0, FloorDiv(y, Pitch));
        var last = Math.Min(entryCount - 1, FloorDiv(y + height - 1, Pitch));
        var result = new List<int>();
        for (var index = first; index <= last; index++)
        {
            var top = index * Pitch;
            // R-120: 1px でも交われば囲んだことにする（行の間の隙間だけに交わる矩形は対象外）
            if (top < y + height && y < top + RowHeight) result.Add(index);
        }
        return result;
    }

    /// <summary>R-122: ↑ ↓ は 1 行ずつ。← → は何もしない（横スクロールも無い）。</summary>
    public int Arrow(int index, int dx, int dy, int entryCount)
    {
        if (entryCount == 0 || dx != 0) return index;
        return Math.Clamp(index + dy, 0, entryCount - 1);
    }

    public int AutoScrollBand => EdgeBand;

    public (int X, int Y) AutoScrollDirection(int x, int y, int viewportWidth, int viewportHeight) =>
        (0, y < EdgeBand ? -1 : y >= viewportHeight - EdgeBand ? 1 : 0);

    public (int X, int Y) ScrollOffset(ScrollPosition position) => (0, position.Y * Pitch);

    public ScrollPosition MaxScrollPosition(int entryCount, int viewportWidth, int viewportHeight) =>
        new(0, Math.Max(0, entryCount - VisibleRows(viewportHeight)));

    public int PageItems(int viewportWidth, int viewportHeight) => VisibleRows(viewportHeight);

    public (int X, int Y) VisibleSteps(int viewportWidth, int viewportHeight) => (0, VisibleRows(viewportHeight));

    /// <summary>R-10: カーソルの行が見えていなければ、その行が端に来るまで動かす。</summary>
    public ScrollPosition Reveal(int index, ScrollPosition current, int viewportWidth, int viewportHeight)
    {
        if (index < 0) return current;
        var visible = VisibleRows(viewportHeight);
        if (index < current.Y) return current with { Y = index };
        if (index > current.Y + visible - 1) return current with { Y = index - visible + 1 };
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
