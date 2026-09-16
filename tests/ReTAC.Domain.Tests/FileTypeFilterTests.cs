using System.IO;
using ReTAC.Domain.Entries;
using ReTAC.Domain.Listing;

namespace ReTAC.Domain.Tests;

/// <summary>表示するファイルタイプの設定（16.4 節の 7 項目）</summary>
public class FileTypeFilterTests
{
    private static Entry File(string name, FileAttributes attributes = FileAttributes.Normal) =>
        Entry.ForFile($@"C:\x\{name}", name, attributes, 0, DateTime.Now);

    [Theory]
    [InlineData("setup.exe", FileTypeKind.Program)]
    [InlineData("run.CMD", FileTypeKind.Program)]
    [InlineData("old.lzh", FileTypeKind.Archive)]
    [InlineData("data.bin", FileTypeKind.Other)]
    public void 拡張子で種類が決まる(string name, FileTypeKind expected)
    {
        Assert.Equal(expected, FileTypeFilter.Classify(File(name), hasAssociation: false));
    }

    [Fact]
    public void 関連付けの有無がその他と関連付けを分ける()
    {
        Assert.Equal(FileTypeKind.Associated, FileTypeFilter.Classify(File("memo.txt"), hasAssociation: true));
        Assert.Equal(FileTypeKind.Other, FileTypeFilter.Classify(File("memo.txt"), hasAssociation: false));
    }

    [Fact]
    public void 既定はすべて表示する()
    {
        var filter = new FileTypeFilter();

        Assert.True(filter.AcceptsEverything);
        Assert.True(filter.Accepts(File("secret.txt", FileAttributes.Hidden), true));
    }

    [Fact]
    public void 隠しファイルをOFFにすると隠し属性が消える()
    {
        var filter = new FileTypeFilter { HiddenFiles = false };

        Assert.False(filter.AcceptsEverything);
        Assert.False(filter.Accepts(File("secret.txt", FileAttributes.Hidden), true));
        Assert.True(filter.Accepts(File("memo.txt"), true));
    }

    [Fact]
    public void システムファイルをOFFにするとシステム属性が消える()
    {
        var filter = new FileTypeFilter { SystemFiles = false };

        Assert.False(filter.Accepts(File("pagefile.sys", FileAttributes.System), false));
    }

    [Fact]
    public void 種類ごとにONOFFできる()
    {
        var filter = new FileTypeFilter { Programs = false, Archives = false };

        Assert.False(filter.Accepts(File("setup.exe"), true));
        Assert.False(filter.Accepts(File("old.zip"), true));
        Assert.True(filter.Accepts(File("memo.txt"), true));
    }

    [Fact]
    public void 親フォルダ項目は常に表示する()
    {
        var filter = new FileTypeFilter { Folders = false };

        Assert.True(filter.Accepts(Entry.ForParent(@"C:\"), false));
    }
}
