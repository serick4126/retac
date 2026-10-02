using ReTAC.Domain.Entries;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

public class ScalarTemplateTests
{
    internal static MacroContext Context()
    {
        var folder = Entry.ForFolder(@"C:\work\v1.2", "v1.2", FileAttributes.Directory, default);
        var a = Entry.ForFile(@"C:\work\a.txt", "a.txt", 0, 0, default);
        var b = Entry.ForFile(@"C:\work\b.txt", "b.txt", 0, 0, default);
        return new([folder, a, b], b, @"C:\work");
    }

    [Fact]
    public void Scalar_FirstTargetPerMacro()
    {
        var scalar = ScalarTemplate.Parse(" ${pathBase}|${fileBasenameNoExtension}|${file}|${cursorFileBasenameNoExtension} \"");
        Assert.True(scalar.IsValid);
        Assert.Equal(" C:\\work\\v1.2|a|C:\\work\\a.txt|b \"", scalar.Expand(Context(), out _));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(" \t\"a b\" ", " \t\"a b\" ")]
    [InlineData("$$$${prompt}", "$${prompt}")]
    [InlineData("${cwd}!", "C:\\work")]
    public void Scalar_WhitespaceQuotesAndDollarEscape(string source, string expected)
    {
        var scalar = ScalarTemplate.Parse(source);
        Assert.True(scalar.IsValid);
        Assert.Equal(expected, scalar.Expand(Context(), out var empty));
        Assert.Null(empty);
    }

    [Fact]
    public void Scalar_AllNonPromptMacros()
    {
        foreach (var name in ArgumentTemplate.Names.Keys.Where(n => n != "prompt"))
        {
            var template = ScalarTemplate.Parse("${" + name + "}");
            Assert.True(template.IsValid, name);
            Assert.NotNull(template.Expand(Context(), out _));
        }
    }

    [Fact]
    public void Scalar_EmptyAndRequired()
    {
        var context = new MacroContext([], null, @"C:\work");
        Assert.Equal("x", ScalarTemplate.Parse("x${file}").Expand(context, out _));
        Assert.Null(ScalarTemplate.Parse("x${file}!").Expand(context, out var empty));
        Assert.Equal(MacroName.File, empty);
        Assert.Equal("", ScalarTemplate.Parse("${path}").Expand(context, out _));
    }

    [Theory]
    [InlineData("${missing}")]
    [InlineData("${file")]
    [InlineData("${File}")]
    [InlineData("${file:x}")]
    [InlineData("${prompt}")]
    [InlineData("${prompt:x}{x}")]
    public void Scalar_InvalidSyntax(string source) => Assert.False(ScalarTemplate.Parse(source).IsValid);

    [Fact]
    public void Scalar_DoesNotReexpand()
    {
        var file = Entry.ForFile(@"C:\${missing}.txt", "${missing}.txt", 0, 0, default);
        var context = new MacroContext([file], file, @"C:\work", PathForm: _ => throw new Exception());
        Assert.Equal(@"C:\${missing}.txt", ScalarTemplate.Parse("${file}").Expand(context, out _));
        Assert.Equal("${missing}", ScalarTemplate.Parse("${fileBasenameNoExtension}").Expand(context, out _));
    }
}
