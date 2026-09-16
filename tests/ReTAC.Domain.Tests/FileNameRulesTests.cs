using ReTAC.Domain.FileOps;

namespace ReTAC.Domain.Tests;

/// <summary>名前の検査（R-48）</summary>
public class FileNameRulesTests
{
    [Fact]
    public void 普通の名前は通る()
    {
        Assert.Null(FileNameRules.Validate("報告書 2026.xlsx"));
        Assert.Null(FileNameRules.Validate("コンソール.txt"));   // 予約語の部分一致では弾かない
        Assert.Null(FileNameRules.Validate(".gitignore"));
    }

    [Fact]
    public void 空とパス区切りと禁止文字は弾く()
    {
        Assert.NotNull(FileNameRules.Validate(""));
        Assert.NotNull(FileNameRules.Validate(@"a\b.txt"));
        Assert.NotNull(FileNameRules.Validate("a:b.txt"));
        Assert.NotNull(FileNameRules.Validate("a?b.txt"));
    }

    [Fact]
    public void 末尾のドットと空白は弾く()
    {
        Assert.NotNull(FileNameRules.Validate("報告書."));
        Assert.NotNull(FileNameRules.Validate("報告書 "));
        Assert.NotNull(FileNameRules.Validate(".."));
    }

    [Fact]
    public void 予約語は拡張子が付いていても弾く()
    {
        Assert.NotNull(FileNameRules.Validate("CON"));
        Assert.NotNull(FileNameRules.Validate("con.txt"));
        Assert.NotNull(FileNameRules.Validate("LPT9.log"));
        Assert.NotNull(FileNameRules.Validate("NUL"));
    }
}
