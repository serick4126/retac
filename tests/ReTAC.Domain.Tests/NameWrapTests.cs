using ReTAC.Domain.Listing;

namespace ReTAC.Domain.Tests;

/// <summary>R-119: 名前は行数まで折り返し、あふれたら拡張子を残して「…」で省略する。書記素の途中では切らない（R-113）。</summary>
public class NameWrapTests
{
    private static int Measure(string s) => new System.Globalization.StringInfo(s).LengthInTextElements * 10;

    [Fact]
    public void 収まれば1行()
    {
        var r = NameWrap.Lines("abc", ".txt", 100, 2, Measure);
        Assert.Equal(["abc.txt"], r.Lines);
        Assert.False(r.Truncated);
    }

    [Fact]
    public void 幅で折り返す()
    {
        var r = NameWrap.Lines("abcdefgh", ".txt", 50, 3, Measure);
        Assert.Equal(["abcde", "fgh.t", "xt"], r.Lines);
        Assert.False(r.Truncated);
    }

    [Fact]
    public void 行数を超えたら最後の行で拡張子を残して省略する()
    {
        var r = NameWrap.Lines("abcdefghijklmnop", ".txt", 60, 2, Measure);
        Assert.Equal(2, r.Lines.Count);
        Assert.Equal("abcdef", r.Lines[0]);
        Assert.Equal("g….txt", r.Lines[1]);
        Assert.True(r.Truncated);
    }

    [Fact]
    public void 隠した拡張子は省略しても出さない()
    {
        var r = NameWrap.Lines("abcdefghijklmnop", "", 60, 1, Measure);
        Assert.Equal(["abcde…"], r.Lines);
        Assert.True(r.Truncated);
    }

    [Fact]
    public void 拡張子が1行に入らないときは拡張子も含めて末尾を省略する()
    {
        var r = NameWrap.Lines("ab", ".verylongextension", 60, 1, Measure);
        Assert.Single(r.Lines);
        Assert.EndsWith("…", r.Lines[0]);
        Assert.True(Measure(r.Lines[0]) <= 60);
    }

    [Fact]
    public void 書記素の途中で切らない()
    {
        var family = "👨‍👩‍👧";   // 1 書記素
        var r = NameWrap.Lines("a" + family + family + "bcdef", "", 30, 3, Measure);
        // ZWJ でつないだ絵文字が行の境目で割れない（行頭が ZWJ で始まらず、つなげると元に戻る）
        Assert.All(r.Lines, line => Assert.False(line.StartsWith('‍')));
        Assert.Contains(r.Lines, line => line.Contains(family));
        Assert.Equal("a" + family + family + "bcdef", string.Concat(r.Lines));
    }

    [Fact]
    public void 行数の制限なしなら省略しない()
    {
        var r = NameWrap.Lines("abcdefghijklmnop", ".txt", 60, int.MaxValue, Measure);
        Assert.Equal("abcdefghijklmnop.txt", string.Concat(r.Lines));
        Assert.False(r.Truncated);
    }

    [Theory]
    [InlineData("abc", ".txt", 0)]          // 幅 0
    [InlineData("\U0001F468‍\U0001F469‍\U0001F467", "", 5)]   // 幅より広い絵文字 1 つ
    [InlineData("あ", "", 5)]               // 幅より広い日本語 1 文字
    [InlineData("あい", "", 5)]             // 2 文字目も入らない（どの行も幅を超える）
    public void 幅より広い書記素は省略した扱い(string body, string tail, int width)
    {
        var r = NameWrap.Lines(body, tail, width, 2, Measure);
        Assert.True(r.Lines.Count <= 2);
        Assert.True(r.Truncated);   // 描いた文字は領域からはみ出すので、ツールチップ・ステータスバーで全部を見せる
    }

    [Theory]
    [InlineData("abcdefgh", ".txt", 50, 3)]
    [InlineData("abcdefghijklmnop", ".txt", 60, 2)]
    [InlineData("abc", ".txt", 100, 2)]
    public void 省略していないなら各行は幅に収まる(string body, string tail, int width, int lines)
    {
        var r = NameWrap.Lines(body, tail, width, lines, Measure);
        if (!r.Truncated) Assert.All(r.Lines, line => Assert.True(Measure(line) <= width, line));
    }
}
