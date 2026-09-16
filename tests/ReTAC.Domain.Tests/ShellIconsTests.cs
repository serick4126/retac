using ReTAC.Shell;

namespace ReTAC.Domain.Tests;

/// <summary>
/// アイコンキャッシュの上限処理（R-12）。パスでキャッシュする種別（.dll / .exe など）で
/// 上限を超えたとき、返した Bitmap が生きていることを確かめる。
/// </summary>
public class ShellIconsTests
{
    /// <summary>
    /// 上限を超えても、返したアイコンは破棄されていない。
    ///
    /// 以前は「キャッシュに入れてから上限判定」だったため、今追加したキーも
    /// DropPathKeyed の対象になり、返す Bitmap ごと Dispose していた。
    /// 呼び出し側が描くと GDI+ が "Parameter is not valid." を投げ、
    /// System32 を横スクロールするとファイルリストが消えた。
    ///
    /// SHGFI_USEFILEATTRIBUTES で解決するのでパスは実在しなくてよい。
    /// </summary>
    [Fact]
    public void 上限を超えても返したアイコンは生きている()
    {
        using var icons = new ShellIcons(16);

        // 上限（512）を確実に超える件数を、すべて別々のパスキーで要求する
        for (var i = 0; i < 600; i++)
        {
            var bitmap = icons.ForFile($@"C:\__retac_test__\icon-{i}.dll");
            if (bitmap is null) continue;   // シェルがアイコンを返さない環境では確かめようがない

            // 破棄済みなら Width を読むだけで ArgumentException になる
            Assert.Equal(16, bitmap.Width);
        }
    }

    /// <summary>拡張子で共有できるものは上限を超えても捨てられない（同じ実体が返る）。</summary>
    [Fact]
    public void 拡張子キーのアイコンは上限を超えても捨てられない()
    {
        using var icons = new ShellIcons(16);

        var first = icons.ForFile(@"C:\__retac_test__\a.txt");
        for (var i = 0; i < 600; i++) icons.ForFile($@"C:\__retac_test__\icon-{i}.dll");
        var again = icons.ForFile(@"C:\__retac_test__\b.txt");

        Assert.Same(first, again);
    }
}
