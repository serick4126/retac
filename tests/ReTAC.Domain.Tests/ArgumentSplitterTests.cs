using ReTAC.Domain.Entries;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

/// <summary>R-130: 文字どおりの区切り・付ける文字</summary>
public class ArgumentSplitterTests
{
    [Theory]
    [InlineData("a b\tc", new[] { "a", "b", "c" })]
    [InlineData("a　b", new[] { "a　b" })]            // 全角空白では区切らない
    [InlineData("\"a b\" c", new[] { "a b", "c" })]
    [InlineData("\"\"", new[] { "" })]                // 空の引数
    [InlineData("a\"\"b", new[] { "ab" })]
    [InlineData("", new string[0])]
    [InlineData("  \t ", new string[0])]
    [InlineData("$$ ${file} $${file}", new[] { "$$", "${file}", "$${file}" })]   // マクロは解釈しない
    public void 引数欄と同じ規則で区切りマクロは文字どおり(string text, string[] expected)
    {
        Assert.Equal(expected, ArgumentSplitter.Split(text));
    }

    [Theory]
    [InlineData("\"x")]
    [InlineData("a \"b c")]
    public void 引用符が閉じていなければnull(string text)
    {
        Assert.Null(ArgumentSplitter.Split(text));
    }

    [Theory]
    [InlineData("-y")]
    [InlineData("a b")]
    [InlineData("\"a b\" \"\" c")]
    [InlineData("x\t\"y z\"w")]
    [InlineData("a　b c")]
    public void ドルを含まない文字列は引数欄と同じ結果になる(string text)
    {
        var context = new MacroContext([], null, @"C:\work");
        var expected = ArgumentExpander.Expand(ArgumentTemplate.Parse(text), context);
        Assert.Equal(expected, ArgumentSplitter.Split(text));
    }

    [Theory]
    [InlineData("-y", "-y")]
    [InlineData("a b", "\"a b\"")]
    [InlineData("a\tb", "\"a\tb\"")]
    [InlineData("  ", "\"  \"")]
    [InlineData("$${file}", "$${file}")]
    public void 一つの引数の形にする(string value, string expected)
    {
        Assert.Equal(expected, ArgumentSplitter.QuoteOne(value));
    }

    [Fact]
    public void 二重引用符を含む値は一つの引数の形にできない()
    {
        Assert.Null(ArgumentSplitter.QuoteOne("a\"b"));
    }

    [Fact]
    public void 一つの引数の形にした値を区切ると元の値に戻る()
    {
        foreach (var value in new[] { "-c:v", "a b", "a\tb", "  ", "$$", "${file}" })
            Assert.Equal([value], ArgumentSplitter.Split(ArgumentSplitter.QuoteOne(value)!));
    }

    [Theory]
    [InlineData("-ss ", "00:01:00", "", false, new[] { "-ss", "00:01:00" })]
    [InlineData("-o", @"E:\展開先", "", true, new[] { @"-oE:\展開先" })]
    [InlineData("--in -o", @"E:\a b", "", true, new[] { "--in", @"-oE:\a b" })]
    [InlineData("", @"E:\展開先", @"\", true, new[] { @"E:\展開先\" })]
    [InlineData("", @"E:\動画", @"\out.mp4", true, new[] { @"E:\動画\out.mp4" })]
    [InlineData("", @"C:\", @"\", true, new[] { @"C:\" })]
    [InlineData("", @"C:\", @"\out.mp4", true, new[] { @"C:\out.mp4" })]
    [InlineData("", @"E:\展開先\", @"\", true, new[] { @"E:\展開先\" })]
    [InlineData("", @"a\", @"\b", false, new[] { @"a\\b" })]            // テキストの項目は \ を 1 つにしない
    [InlineData("", "x", " -y", false, new[] { "x", "-y" })]
    [InlineData("", "v", "", false, new[] { "v" })]
    [InlineData("\"-o \"", "v", "", false, new[] { "-o v" })]            // 引用符の中の空白は区切りではない
    [InlineData("$${file} ", "v", "", false, new[] { "$${file}", "v" })]
    [InlineData("", "a b", "", false, new[] { "a b" })]                  // 値は区切らない
    public void 前後に付ける文字をつなぐ(string prefix, string value, string suffix, bool merge, string[] expected)
    {
        Assert.Equal(expected, ArgumentSplitter.Join(prefix, value, suffix, merge));
    }

    [Theory]
    [InlineData("\"x", "")]
    [InlineData("", "\"x")]
    public void 付ける文字の引用符が閉じていなければnull(string prefix, string suffix)
    {
        Assert.Null(ArgumentSplitter.Join(prefix, "v", suffix, false));
    }
}
