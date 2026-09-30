using ReTAC.Domain.Navigation;

namespace ReTAC.Domain.Tests;

/// <summary>B-01: 入力欄から受けたパス・名前の端の落とし方</summary>
public class InputTextTests
{
    [Fact]
    public void 末尾の全角空白は残す()
    {
        // U+3000 はファイル名に使える。string.Trim() はこれを落としてしまう
        Assert.Equal("フォルダ　　", InputText.TrimEdge("フォルダ　　"));
        Assert.Equal("a　", InputText.TrimEdge("a　"));
    }

    [Fact]
    public void 末尾の半角空白とタブは落とす()
    {
        // Windows はファイル名の末尾に ASCII 空白を許さない
        Assert.Equal("フォルダ", InputText.TrimEdge("フォルダ  "));
        Assert.Equal("フォルダ", InputText.TrimEdge("フォルダ\t"));
    }

    [Fact]
    public void 先頭の半角空白も落とす()
    {
        Assert.Equal("C:\\x", InputText.TrimEdge("  C:\\x"));
    }

    [Fact]
    public void 先頭の全角空白は残す()
    {
        Assert.Equal("　名前", InputText.TrimEdge("　名前"));
    }

    [Fact]
    public void 空文字と空白だけの入力は空になる()
    {
        Assert.Equal("", InputText.TrimEdge(""));
        Assert.Equal("", InputText.TrimEdge("   "));
        // 全角空白だけなら残る。それが名前として妥当かは呼び出し側が判断する
        Assert.Equal("　", InputText.TrimEdge("　"));
    }

    // ---- R-127: Windows の「パスのコピー」の形（二重引用符で囲む）を、そのまま貼り付けて使う ----

    [Theory]
    [InlineData("\"C:\\a\\b\"", @"C:\a\b")]
    [InlineData("  \"C:\\a\\b\"  ", @"C:\a\b")]          // 外側の空白
    [InlineData("\" C:\\a\\b \"", @"C:\a\b")]            // 内側の空白
    [InlineData("\"C:\\a\\b", @"C:\a\b")]                // 片方だけ
    [InlineData("C:\\a\\b\"", @"C:\a\b")]
    [InlineData("\"C:\\Program Files\\x\"", @"C:\Program Files\x")]
    [InlineData(@"C:\a\b", @"C:\a\b")]                   // 引用符が無ければそのまま
    [InlineData("\"\"", "")]
    [InlineData("\"", "")]
    [InlineData("", "")]
    public void 端の二重引用符を1つずつ外す(string input, string expected) =>
        Assert.Equal(expected, InputText.Unquote(input));

    [Fact]
    public void 二重に囲んだものは1組だけ外す() =>
        Assert.Equal("\"C:\\a\"", InputText.Unquote("\"\"C:\\a\"\""));

    [Fact]
    public void 途中の引用符と_全角の引用符と_一重引用符は外さない()
    {
        Assert.Equal("C:\\a\" \"C:\\b", InputText.Unquote("\"C:\\a\" \"C:\\b\""));
        Assert.Equal("“C:\\a”", InputText.Unquote("“C:\\a”"));
        Assert.Equal("'C:\\a'", InputText.Unquote("'C:\\a'"));
    }

    [Fact]
    public void 引用符の内側の全角空白は落とさない() =>
        Assert.Equal("C:\\名前\u3000", InputText.Unquote("\"C:\\名前\u3000\""));
}
