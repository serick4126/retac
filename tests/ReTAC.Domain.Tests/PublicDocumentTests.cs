using System.IO;
using System.Text.RegularExpressions;
using ReTAC.Domain.Entries;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

/// <summary>
/// 公開する文書で「卓駆」の名前を出すときは、著作権表示を添える（公開文書の約束）。
/// README を書き直したときに一度落ちたので、文書ごとに確かめる。
/// </summary>
public class PublicDocumentTests
{
    [Theory]
    [InlineData("README.md")]
    [InlineData("CHANGELOG.md")]
    [InlineData("EXTERNAL_TOOLS.md")]
    public void 卓駆の名前を出す文書には著作権表示がある(string file)
    {
        var text = File.ReadAllText(Path.Combine(SchemaManifest.RepoRoot(), file));
        if (text.Contains("卓駆")) Assert.Contains("Copyright (C) 1995-2009 COM Corp.", text);
    }

    private static string ExternalToolsText() =>
        File.ReadAllText(Path.Combine(SchemaManifest.RepoRoot(), "EXTERNAL_TOOLS.md"));

    [Fact]
    public void ExternalTools_ImportExampleParsesAsPublished()
    {
        var section = ExternalToolsText().Split("**インポートと変換の例**", StringSplitOptions.None)[1]
            .Split("**3.0.0", StringSplitOptions.None)[0];
        var command = Regex.Match(section, @"```text\r?\n([^\r\n]+)\r?\n```").Groups[1].Value;
        Assert.NotEmpty(command);
        var imported = PromptImport.Read(command);
        Assert.Null(imported.Error);
        Assert.Equal(@"C:\Program Files\WinRAR\WinRAR.exe", imported.Command!.Path);
        Assert.Equal(new[] { "a", "-afzip", "-m5", "backup.zip", @"C:\Data\*" },
            imported.Command.Arguments.Select(arg => arg.Text));
        Assert.All(imported.Command.Arguments, arg => Assert.Equal(PromptArgumentKind.Fixed, arg.Kind));
        var describedPaths = Regex.Matches(section, @"(?<!`)`([^`\r\n]+)`(?!`)").Select(match => match.Groups[1].Value)
            .Where(value => value.StartsWith("C:", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.Equal(new[] { imported.Command.Path }, describedPaths);
    }

    [Fact]
    public void ExternalTools_PathBaseExamplesMatchExpansion()
    {
        var explanation = ExternalToolsText().Split('\n').Single(line => line.StartsWith("拡張子なしのパスは、"));
        var paths = Regex.Matches(explanation, "`([^`]+)`").Select(match => match.Groups[1].Value)
            .Where(value => value.StartsWith("C:", StringComparison.OrdinalIgnoreCase) || value == ".gitignore").ToArray();
        Assert.Equal(4, paths.Length);
        Assert.Equal(@"C:\work\archive.tar.gz", paths[0]);
        Assert.Equal(".gitignore", paths[2]);
        foreach (var example in new[] { (Source: paths[0], Result: paths[1]),
            (Source: Path.Combine(@"C:\work", paths[2]), Result: paths[3]) })
        {
            var target = Entry.ForFile(example.Source, Path.GetFileName(example.Source), 0, 0, default);
            var context = new MacroContext([target], target, @"C:\work");
            Assert.Equal(example.Result, ScalarTemplate.Parse("${pathBase}").Expand(context, out _));
        }
    }
}
