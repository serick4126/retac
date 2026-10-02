using ReTAC.Domain.Entries;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

public class MacroExpansionTests
{
    [Fact]
    public void Macro_TargetFamiliesAndCounts()
    {
        var context = ScalarTemplateTests.Context();
        Assert.Equal(new[] { @"C:\work\v1.2|C:\work\v1.2", @"C:\work\a.txt|C:\work\a", @"C:\work\b.txt|C:\work\b" },
            ArgumentExpander.Expand(ArgumentTemplate.Parse("${path}|${pathBase}"), context));
        Assert.Equal(new[] { @"C:\work\a.txt|a|.txt", @"C:\work\b.txt|b|.txt" },
            ArgumentExpander.Expand(ArgumentTemplate.Parse("${file}|${fileBasenameNoExtension}|${fileExtname}"), context));
        Assert.Equal(new[] { "work|b" }, ArgumentExpander.Expand(ArgumentTemplate.Parse("${cwdBasename}|${cursorFileBasenameNoExtension}"), context));
        foreach (var source in new[] { "${pathBase}:${file}", "${path}:${fileExtname}", "${pathBase}:${fileBasenameNoExtension}" })
            Assert.False(ArgumentTemplate.Parse(source).IsValid);
        var parent = Entry.ForParent(@"C:\");
        var noFiles = new MacroContext([context.Targets[0], parent], parent, @"C:\work");
        Assert.Empty(ArgumentExpander.Expand(ArgumentTemplate.Parse("${file}"), noFiles)!);
        Assert.Equal(new[] { "" }, ArgumentExpander.Expand(ArgumentTemplate.Parse("\"${file}\""), noFiles));
        Assert.Null(ArgumentExpander.Expand(ArgumentTemplate.Parse("\"${file}!\""), noFiles));
        Assert.Equal(new[] { @"C:\work\v1.2", @"C:\work" }, ArgumentExpander.Expand(ArgumentTemplate.Parse("${pathBase}"), noFiles));
        Assert.Empty(ArgumentExpander.Expand(ArgumentTemplate.Parse("${pathBase}"), new([], null, @"C:\work"))!);
    }

    [Theory]
    [InlineData("a.txt", "a")]
    [InlineData("archive.tar.gz", "archive.tar")]
    [InlineData("README", "README")]
    [InlineData(".gitignore", "")]
    [InlineData("a.", "a")]
    public void Macro_NamesAndPaths(string name, string stem)
    {
        var file = Entry.ForFile(@"C:\work\" + name, name, 0, 0, default);
        var context = new MacroContext([file], file, @"C:\work");
        Assert.Equal(new[] { @"C:\work\" + stem, @"C:\work\" + stem, stem },
            ArgumentExpander.Expand(ArgumentTemplate.Parse("\"${pathBase}\" \"${cursorPathBase}\" \"${cursorFileBasenameNoExtension}\""), context));
    }

    [Theory]
    [InlineData("C:\\", "C:")]
    [InlineData("C:\\work\\", "work")]
    [InlineData("\\\\server\\share\\", "share")]
    [InlineData("C:\\work\\v1.2", "v1.2")]
    public void Macro_NamesAndRoots(string folder, string expected)
    {
        Assert.Equal(new[] { expected }, ArgumentExpander.Expand(ArgumentTemplate.Parse("${cwdBasename}"), new([], null, folder)));
        var entry = Entry.ForFolder(folder, expected, FileAttributes.Directory, default);
        Assert.Equal(new[] { folder, folder }, ArgumentExpander.Expand(ArgumentTemplate.Parse("${pathBase} ${cursorPathBase}"), new([entry], entry, folder)));
    }

    [Theory]
    [InlineData("archive.tar.gz", "ARCHIV~1.GZ", "ARCHIV~1")]
    [InlineData(".gitignore", "GITIGN~1", "")]
    [InlineData("README", "README~1", "README~1")]
    public void PathBase_FormsOriginalBeforeRemovingExtension(string name, string formed, string expected)
    {
        var calls = new List<string>();
        var file = Entry.ForFile(@"C:\long name\" + name, name, 0, 0, default);
        var context = new MacroContext([file], file, @"C:\work", PathForm: p => { calls.Add(p); return @"C:\LONGNA~1\" + formed; });
        Assert.Equal(new[] { @"C:\LONGNA~1\" + expected }, ArgumentExpander.Expand(ArgumentTemplate.Parse("${pathBase}"), context));
        Assert.Equal(new[] { file.FullPath }, calls);
    }

    [Fact]
    public void Names_DoNotUsePathForm()
    {
        var context = ScalarTemplateTests.Context() with { PathForm = _ => throw new Exception() };
        Assert.Equal(new[] { "a|.txt|b|work", "b|.txt|b|work" },
            ArgumentExpander.Expand(ArgumentTemplate.Parse("${fileBasenameNoExtension}|${fileExtname}|${cursorFileBasenameNoExtension}|${cwdBasename}"), context));
    }

    [Fact]
    public void CatalogExactlyMatchesRegisteredNames()
    {
        Assert.Equal(ArgumentTemplate.Names.Keys.Order(), MacroCatalog.Entries.Where(e => e.Group == MacroCatalog.MacroGroup)
            .Select(e => e.Insert![2..^1]).Order());
    }
}
