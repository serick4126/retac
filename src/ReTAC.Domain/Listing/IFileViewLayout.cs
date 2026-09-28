namespace ReTAC.Domain.Listing;

/// <summary>R-110-3: スクロール位置。軸ごとに「段」の数で持つ。1 段の量はレイアウトが決める（一覧は横に 1 列、縦のレイアウトは 1 行）。</summary>
public readonly record struct ScrollPosition(int X, int Y);

/// <summary>
/// R-110-1〜R-110-3: ドロップの処理がレイアウトに聞くこと。ドロップの処理の中で行の高さ・列の幅を自分で計算しない。
/// Phase 15 で表示モードごとのレイアウトを作るとき、これを実装すればドロップの処理は変えずに済む。
/// IndexAt と ItemBounds は中身の座標（スクロールのずれを足した座標）、AutoScrollDirection は見えている範囲の座標。
/// </summary>
public interface IFileViewLayout
{
    int IndexAt(int x, int y, int entryCount);
    (int X, int Y, int Width, int Height) ItemBounds(int index);
    /// <returns>軸ごとに -1 は前へ、1 は後ろへ、0 はスクロールしない。端はその軸の端から項目 1 行分の高さ。角なら両方</returns>
    (int X, int Y) AutoScrollDirection(int x, int y, int viewportWidth, int viewportHeight);
    /// <summary>スクロール位置のときに、中身をどれだけずらして見せるか（ピクセル。縦横）。</summary>
    (int X, int Y) ScrollOffset(ScrollPosition position);
    /// <summary>いちばん後ろのスクロール位置（軸ごと）。</summary>
    ScrollPosition MaxScrollPosition(int entryCount, int viewportWidth, int viewportHeight);
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

    /// <summary>R-110-1: 見えている範囲の点にある項目。縦横どちらのずれも足す。</summary>
    public static int IndexAt(IFileViewLayout layout, ScrollPosition position, int x, int y, int entryCount)
    {
        var offset = layout.ScrollOffset(position);
        return layout.IndexAt(x + offset.X, y + offset.Y, entryCount);
    }

    /// <summary>R-110-2: 項目の矩形を、見えている範囲の座標で。縦横どちらのずれも引く。</summary>
    public static (int X, int Y, int Width, int Height) VisibleBounds(IFileViewLayout layout, ScrollPosition position, int index)
    {
        var (x, y, width, height) = layout.ItemBounds(index);
        var offset = layout.ScrollOffset(position);
        return (x - offset.X, y - offset.Y, width, height);
    }
}
