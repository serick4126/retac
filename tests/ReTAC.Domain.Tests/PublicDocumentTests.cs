using System.IO;

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
}
