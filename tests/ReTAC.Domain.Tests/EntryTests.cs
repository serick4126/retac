using System.IO;
using ReTAC.Domain.Entries;
using ReTAC.Domain.Formatting;
using ReTAC.Domain.Listing;

namespace ReTAC.Domain.Tests;

/// <summary>拡張子の分離と属性色</summary>
public class EntryTests
{
    [Theory]
    [InlineData("memo.txt", "memo", ".txt")]
    [InlineData("archive.tar.gz", "archive.tar", ".gz")]      // R-09: 最後のピリオド以降
    [InlineData(".gitignore", "", ".gitignore")]              // R-09: 基底名が空
    [InlineData("README", "README", "")]                      // ピリオドなし
    [InlineData("trailing.", "trailing", ".")]
    public void ファイルは最後のピリオドで基底名と拡張子に分かれる(string name, string expectedBase, string expectedExt)
    {
        var entry = TestEntries.File(name);
        Assert.Equal(expectedBase, entry.BaseName);
        Assert.Equal(expectedExt, entry.Extension);
        Assert.Equal(name, entry.Name);
    }

    [Theory]
    [InlineData("node.js")]
    [InlineData("v1.2.3")]
    [InlineData("plain")]
    public void フォルダは拡張子を分離しない(string name)
    {
        // R-07: フォルダ名は末尾にピリオドがあってもそのまま連結して表示する
        var entry = TestEntries.Folder(name);
        Assert.Equal(name, entry.BaseName);
        Assert.Equal("", entry.Extension);
    }

    [Fact]
    public void 親フォルダ項目は拡張子を持たずマーク対象外である()
    {
        var parent = TestEntries.Parent();
        Assert.True(parent.IsParent);
        Assert.Equal("..", parent.Name);
        Assert.Equal("", parent.Extension);
    }

    /// <summary>R-06: システム ＞ 書込禁止 ＞ 隠し ＞ 圧縮 ＞ 通常。属性 16 通りを網羅する。</summary>
    [Fact]
    public void 属性色の優先順位は16通りすべてでR06と一致する()
    {
        FileAttributes[] bits =
        [
            FileAttributes.System,
            FileAttributes.ReadOnly,
            FileAttributes.Hidden,
            FileAttributes.Compressed,
        ];
        AttributeColor[] expectedByBit =
        [
            AttributeColor.System,
            AttributeColor.ReadOnly,
            AttributeColor.Hidden,
            AttributeColor.Compressed,
        ];

        for (var combo = 0; combo < 16; combo++)
        {
            var attributes = default(FileAttributes);
            for (var b = 0; b < 4; b++)
                if ((combo & (1 << b)) != 0) attributes |= bits[b];

            // 立っているビットのうち最も優先度の高いもの（bits の並び順が優先順位）
            var expected = AttributeColor.Normal;
            for (var b = 0; b < 4; b++)
            {
                if ((combo & (1 << b)) == 0) continue;
                expected = expectedByBit[b];
                break;
            }

            Assert.Equal(expected, AttributeColorRule.Classify(attributes));
        }
    }

    [Fact]
    public void 暗号化は圧縮と同格で通常より優先される()
    {
        Assert.Equal(AttributeColor.Encrypted, AttributeColorRule.Classify(FileAttributes.Encrypted));
        // 上位属性があればそちらが勝つ
        Assert.Equal(AttributeColor.System, AttributeColorRule.Classify(FileAttributes.Encrypted | FileAttributes.System));
    }

    [Fact]
    public void 列挙は作成日時を持つ()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = Path.Combine(folder, "a.txt");
            File.WriteAllText(path, "x");
            var created = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Local);
            File.SetCreationTime(path, created);

            var entry = FolderEnumerator.Enumerate(folder, SortOrder.Default).Single(e => e.Name == "a.txt");

            Assert.Equal(created, entry.CreationTime);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Theory]
    [InlineData(FileAttributes.Normal, "")]
    [InlineData(FileAttributes.Archive, "A")]
    [InlineData(FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive, "RHSA")]
    [InlineData(FileAttributes.Directory | FileAttributes.Compressed, "C")]
    [InlineData(FileAttributes.Encrypted | FileAttributes.ReadOnly, "RE")]
    public void 属性は立っている文字だけをRHSACEの順に詰める(FileAttributes attributes, string expected) =>
        Assert.Equal(expected, Display.Attributes(attributes));
}
