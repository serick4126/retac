namespace ReTAC.Domain.Listing;

/// <summary>R-110-3: スクロール位置。軸ごとに「段」の数で持つ。1 段の量はレイアウトが決める（一覧は横に 1 列、縦のレイアウトは 1 行）。</summary>
public readonly record struct ScrollPosition(int X, int Y);

/// <summary>R-11-2 / INV-DETAILS-ROW-HIT / R-116: 押した所の種類。マウスの操作の表の行を決める。</summary>
public enum FileViewArea { None, MarkIcon, Name, Other, CheckBox }

/// <summary>R-114: 見出しのセル 1 つ（中身の座標の x と幅）。Column が null なら名前の列。</summary>
public readonly record struct HeaderCell(DetailsColumn? Column, int X, int Width);

/// <summary>
/// INV-LAYOUT-GEOMETRY-SINGLE-SOURCE: 項目・部品・見出しの矩形、当たり判定、キーでの移動、スクロールはレイアウトだけが計算する。
/// 描画・当たり判定・見出しの操作・ドロップの枠は同じ答えを使う。座標は中身の座標（見出しを除いた領域の左上が原点、スクロールのずれを足したもの）。
/// viewport は見出しとスクロールバーを除いた、項目を描ける領域の大きさ。
/// AutoScrollDirection の x・y も、見出しの分を引いた項目の領域の左上を原点にした座標（呼び出し側が y から HeaderHeight を引いて渡す）。
/// </summary>
public interface IFileViewLayout
{
    int IndexAt(int x, int y, int entryCount) => HitTest(x, y, entryCount).Index;
    (int X, int Y, int Width, int Height) ItemBounds(int index);
    /// <param name="y">見出しを引いた項目の領域での座標。viewportHeight も見出しを除いた高さ</param>
    /// <returns>軸ごとに -1 は前へ、1 は後ろへ、0 はスクロールしない。端はその軸の端から項目 1 行分の高さ。角なら両方</returns>
    (int X, int Y) AutoScrollDirection(int x, int y, int viewportWidth, int viewportHeight);
    /// <summary>Phase 16 §10: AutoScrollDirection が向きを返す端の帯の幅（ピクセル）。投げ縄の自動スクロールの速さは、この帯からの深さで決める。</summary>
    int AutoScrollBand { get; }
    /// <summary>スクロール位置のときに、中身をどれだけずらして見せるか（ピクセル。縦横）。</summary>
    (int X, int Y) ScrollOffset(ScrollPosition position);
    /// <summary>いちばん後ろのスクロール位置（軸ごと）。</summary>
    ScrollPosition MaxScrollPosition(int entryCount, int viewportWidth, int viewportHeight);

    /// <summary>見出しの高さ。見出しの無いビューは 0。</summary>
    int HeaderHeight { get; }
    /// <summary>見出しのセル。見出しの無いビューは空。</summary>
    IReadOnlyList<HeaderCell> Header { get; }
    /// <summary>要るスクロールバー。</summary>
    (bool Horizontal, bool Vertical) ScrollBars { get; }
    (int Index, FileViewArea Area) HitTest(int x, int y, int entryCount);
    /// <summary>行頭アイコンの矩形。</summary>
    (int X, int Y, int Width, int Height) IconBounds(int index);
    /// <summary>名前の矩形（アイコンより右）。</summary>
    (int X, int Y, int Width, int Height) NameBounds(int index);
    /// <summary>揃えた拡張子の矩形。出さないなら幅 0。</summary>
    (int X, int Y, int Width, int Height) ExtensionBounds(int index);
    /// <summary>R-116: マークのチェックボックスの矩形（項目の左上）。チェックボックスの無いレイアウトは null。描くかどうか（ホバー・設定）は描く側が決める。</summary>
    (int X, int Y, int Width, int Height)? CheckBoxBounds(int index) => null;
    /// <summary>
    /// 行のセルの矩形（左右の余白を除いた文字の領域）。列は論理列 ID で指す。名前の列・その列が無い・見出しの無いレイアウトは null。
    /// </summary>
    (int X, int Y, int Width, int Height)? CellBounds(int index, DetailsColumn column);
    /// <summary>x が見出しのセルの右の境界から tolerance 以内なら、そのセルの添字。無ければ（見出しの無いレイアウトも）-1。</summary>
    int HeaderBorderAt(int x, int tolerance);
    /// <summary>x にある見出しのセルの添字。無ければ（見出しの無いレイアウトも）-1。</summary>
    int HeaderCellAt(int x);
    /// <summary>矩形に交わる項目（投げ縄・描く範囲）。</summary>
    IReadOnlyList<int> IndexesIn(int x, int y, int width, int height, int entryCount);
    /// <summary>矢印キーでの移動先。動かないなら index。</summary>
    int Arrow(int index, int dx, int dy, int entryCount);
    /// <summary>左右の矢印キーが横スクロールになるか（詳細）。</summary>
    bool ArrowsScrollHorizontally { get; }
    /// <summary>PageUp / PageDown の 1 画面分の項目数。</summary>
    int PageItems(int viewportWidth, int viewportHeight);
    /// <summary>R-10: カーソルを見える所へ出すスクロール位置。</summary>
    ScrollPosition Reveal(int index, ScrollPosition current, int viewportWidth, int viewportHeight);
    /// <summary>スクロールバーの LargeChange（軸ごとの段数）。</summary>
    (int X, int Y) VisibleSteps(int viewportWidth, int viewportHeight);
    /// <summary>R-76: マウスホイール 1 ノッチで縦に進める段数。既定はホイールの行数どおり。1 段が大きいレイアウトは自分で決める。</summary>
    int WheelSteps(int notchLines, int viewportWidth, int viewportHeight) => notchLines;
}

/// <summary>R-110-1〜R-110-3: ドロップの処理が使う、スクロールのずれを縦横とも入れた計算。</summary>
public static class FileViewScroll
{
    /// <summary>R-110-3: 端で止めている間の 1 回分。軸ごとに 1 段進め、スクロールできる端で止める。</summary>
    public static ScrollPosition Next(IFileViewLayout layout, ScrollPosition position, (int X, int Y) direction,
        int entryCount, int viewportWidth, int viewportHeight)
    {
        var max = layout.MaxScrollPosition(entryCount, viewportWidth, viewportHeight);
        return new ScrollPosition(
            Math.Clamp(position.X + Math.Sign(direction.X), 0, max.X),
            Math.Clamp(position.Y + Math.Sign(direction.Y), 0, max.Y));
    }

    /// <summary>投げ縄の自動スクロールの間隔（ミリ秒）。端の帯に入った所（深さ 0）で遅い方、帯の幅の 2 倍の深さで速い方。</summary>
    public const int LassoSlowInterval = 200, LassoFastInterval = 30;

    /// <summary>
    /// Phase 16 §10: 投げ縄の自動スクロールの間隔。深さは帯の内側の境界から測り、帯の外・コントロールの外へ出た分も数える
    /// （マウスをつかんでいるので座標は範囲の外になる）。深さ 0 で 200ms、帯の幅の 2 倍以上で 30ms、その間は直線。
    /// 軸ごとに向きのある軸だけを見て、深いほうを取る。x・y・viewport は AutoScrollDirection と同じ座標。
    /// </summary>
    public static int LassoInterval(int band, (int X, int Y) direction, int x, int y, int viewportWidth, int viewportHeight)
    {
        var depth = Math.Max(
            direction.X < 0 ? band - x : direction.X > 0 ? x - (viewportWidth - band) : 0,
            direction.Y < 0 ? band - y : direction.Y > 0 ? y - (viewportHeight - band) : 0);
        var full = Math.Max(1, band * 2);
        var ratio = Math.Clamp(depth, 0, full);
        return LassoSlowInterval - (LassoSlowInterval - LassoFastInterval) * ratio / full;
    }

    /// <summary>R-110-1: 見えている範囲の点にある項目。縦横どちらのずれも足し、見出しの分を引く。見出しの上なら -1。</summary>
    public static int IndexAt(IFileViewLayout layout, ScrollPosition position, int x, int y, int entryCount)
    {
        if (y < layout.HeaderHeight) return -1;
        var offset = layout.ScrollOffset(position);
        return layout.IndexAt(x + offset.X, y - layout.HeaderHeight + offset.Y, entryCount);
    }

    /// <summary>R-110-2: 項目の矩形を、見えている範囲の座標で。縦横どちらのずれも引き、見出しの分を足す。</summary>
    public static (int X, int Y, int Width, int Height) VisibleBounds(IFileViewLayout layout, ScrollPosition position, int index) =>
        ToVisible(layout, position, layout.ItemBounds(index));

    /// <summary>中身の座標の矩形を、見えている範囲の座標へ（部品の矩形にも使う）。</summary>
    public static (int X, int Y, int Width, int Height) ToVisible(IFileViewLayout layout, ScrollPosition position,
        (int X, int Y, int Width, int Height) bounds)
    {
        var offset = layout.ScrollOffset(position);
        return (bounds.X - offset.X, bounds.Y - offset.Y + layout.HeaderHeight, bounds.Width, bounds.Height);
    }
}
