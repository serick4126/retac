using System.IO;

namespace ReTAC.Domain.Tests;

/// <summary>
/// 用語の言い換え: コード・コメント・画面の文言と公開する文章は、ドメイン定義書の用語集の語で書く。
/// 案や下書きで普段の言い方が混ざり、どの段階でも捕まらなかったことがあるので（Phase 28.1）、自動で止める。
/// 一覧は docs の tools/check_domain_ids.py の TERMS と同じものを持つ。似たぶれを見つけたら両方に足す。
/// </summary>
public class TerminologyTests
{
    // 使わない語 → 用語集の語。このファイル自身に当たらないよう、使わない語は連結で作る
    private static readonly (string Wrong, string Right)[] Terms =
    [
        ("ファイル" + "一覧", "ファイルリスト"),
    ];

    private static IEnumerable<string> Targets()
    {
        var root = SchemaManifest.RepoRoot();
        foreach (var dir in new[] { "src", "tests" })
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, dir), "*.cs", SearchOption.AllDirectories))
            {
                var parts = file.Split(Path.DirectorySeparatorChar);
                if (!parts.Contains("bin") && !parts.Contains("obj")) yield return file;
            }
        foreach (var name in new[] { "CHANGELOG.md", "README.md", "EXTERNAL_TOOLS.md" })
            yield return Path.Combine(root, name);
    }

    [Fact]
    public void コードと公開する文章は用語集の語で書く()
    {
        var found = new List<string>();
        foreach (var file in Targets())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                foreach (var (wrong, right) in Terms)
                    if (lines[i].Contains(wrong))
                        found.Add($"{Path.GetRelativePath(SchemaManifest.RepoRoot(), file)}:{i + 1}: 「{wrong}」ではなく「{right}」");
        }
        Assert.Empty(found);
    }
}
