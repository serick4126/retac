using ReTAC.Domain.Selection;

namespace ReTAC.Domain.Tests;

/// <summary>R-80: インクリメンタルサーチの一致</summary>
public class IncrementalMatchTests
{
    private static readonly IReadOnlyList<Entries.Entry> Sample =
    [
        TestEntries.Parent(),               // 0
        TestEntries.Folder("Reports"),      // 1
        TestEntries.File("a.txt"),          // 2
        TestEntries.File("Report.txt"),     // 3
        TestEntries.File("export.txt"),     // 4
        TestEntries.File("報告書.docx"),     // 5
    ];

    [Fact]
    public void 前方一致で大文字小文字を区別せず一覧の順に返す()
    {
        Assert.Equal([1, 3], IncrementalMatch.Find(Sample, "rep"));
    }

    [Fact]
    public void 途中の一致は含めない()
    {
        Assert.Equal([], IncrementalMatch.Find(Sample, "port"));
    }

    [Fact]
    public void 拡張子まで含めて比べる()
    {
        Assert.Equal([2], IncrementalMatch.Find(Sample, "a.t"));
    }

    [Fact]
    public void 親フォルダ項目は対象にしない()
    {
        Assert.Equal([], IncrementalMatch.Find(Sample, "."));
    }

    [Fact]
    public void 日本語の名前も探せる()
    {
        Assert.Equal([5], IncrementalMatch.Find(Sample, "報告"));
    }

    [Fact]
    public void 空文字なら空()
    {
        Assert.Equal([], IncrementalMatch.Find(Sample, ""));
    }

    [Fact]
    public void カーソルが一致の上ならそのままで無ければ最初の一致()
    {
        Assert.Equal(3, IncrementalMatch.Pick([1, 3], cursor: 3));
        Assert.Equal(1, IncrementalMatch.Pick([1, 3], cursor: 2));
        Assert.Equal(-1, IncrementalMatch.Pick([], cursor: 2));
    }

    [Fact]
    public void 次と前はカーソルの位置から数えて端で回る()
    {
        int[] matches = [1, 3, 4];
        Assert.Equal(3, IncrementalMatch.Step(matches, cursor: 1, forward: true));
        Assert.Equal(3, IncrementalMatch.Step(matches, cursor: 2, forward: true));    // 一致の上に無くても数える
        Assert.Equal(1, IncrementalMatch.Step(matches, cursor: 4, forward: true));    // 末尾から先頭へ
        Assert.Equal(1, IncrementalMatch.Step(matches, cursor: 3, forward: false));
        Assert.Equal(4, IncrementalMatch.Step(matches, cursor: 1, forward: false));   // 先頭から末尾へ
        Assert.Equal(-1, IncrementalMatch.Step([], cursor: 0, forward: true));
    }
}
