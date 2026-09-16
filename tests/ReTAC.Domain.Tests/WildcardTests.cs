using ReTAC.Domain.Listing;

namespace ReTAC.Domain.Tests;

/// <summary>ワイルドカードで選択（0x8326 / R-22）。パターンは組み立てを使い回す（V-11）。</summary>
public class WildcardTests
{
    [Fact]
    public void 全部に一致するパターン()
    {
        Assert.True(Wildcard.IsMatch("readme", "*"));
        Assert.True(Wildcard.IsMatch("readme", "*.*"));   // 拡張子が無くても一致する（Windows の慣例）
        Assert.True(Wildcard.IsMatch("a.txt", "*.*"));
    }

    [Fact]
    public void 拡張子で絞る()
    {
        Assert.True(Wildcard.IsMatch("memo.txt", "*.txt"));
        Assert.True(Wildcard.IsMatch("MEMO.TXT", "*.txt"));   // 大文字小文字は区別しない
        Assert.False(Wildcard.IsMatch("memo.md", "*.txt"));
    }

    [Fact]
    public void 疑問符は一文字()
    {
        Assert.True(Wildcard.IsMatch("a1.log", "a?.log"));
        Assert.False(Wildcard.IsMatch("a12.log", "a?.log"));
    }

    [Fact]
    public void 正規表現の記号は文字として扱う()
    {
        Assert.True(Wildcard.IsMatch("a+b.txt", "a+b.txt"));
        Assert.False(Wildcard.IsMatch("aab.txt", "a+b.txt"));
    }

    [Fact]
    public void 同じパターンを繰り返し使っても結果が変わらない()
    {
        // 組み立てたものを使い回すようにしたので、2 度目以降も同じであることを確かめる（V-11）
        for (var i = 0; i < 3; i++)
        {
            Assert.True(Wildcard.IsMatch("memo.txt", "*.txt"));
            Assert.False(Wildcard.IsMatch("memo.md", "*.txt"));
        }
    }
}
