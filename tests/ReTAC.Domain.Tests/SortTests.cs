using System.IO;
using ReTAC.Domain.Entries;
using ReTAC.Domain.Listing;

namespace ReTAC.Domain.Tests;

/// <summary>自然順比較とソート</summary>
public class SortTests
{
    [Fact]
    public void 自然順は数字を数値として比較する()
    {
        string[] input = ["003", "1", "02"];
        var sorted = input.Order(NameComparers.Natural).ToArray();
        Assert.Equal(["1", "02", "003"], sorted);
    }

    [Fact]
    public void 厳密比較では自然順と逆の並びになる()
    {
        string[] input = ["1", "02", "003"];
        var sorted = input.Order(NameComparers.Strict).ToArray();
        Assert.Equal(["003", "02", "1"], sorted);
    }

    [Fact]
    public void 自然順は連番を桁数によらず正しく並べる()
    {
        string[] input = ["file10.txt", "file9.txt", "file100.txt", "file1.txt"];
        var sorted = input.Order(NameComparers.Natural).ToArray();
        Assert.Equal(["file1.txt", "file9.txt", "file10.txt", "file100.txt"], sorted);
    }

    [Fact]
    public void 名前と拡張子の比較は大文字小文字を区別しない()
    {
        // R-05-3: 大文字が先に来る文字コード順にはならない
        Assert.True(NameComparers.Natural.Compare("apple", "BANANA") < 0);
        Assert.True(NameComparers.Strict.Compare("apple", "BANANA") < 0);

        string[] input = ["Beta.txt", "alpha.txt", "Gamma.txt"];
        Assert.Equal(["alpha.txt", "Beta.txt", "Gamma.txt"], input.Order(NameComparers.Natural).ToArray());
        Assert.Equal(["alpha.txt", "Beta.txt", "Gamma.txt"], input.Order(NameComparers.Strict).ToArray());
    }

    [Fact]
    public void 親フォルダ項目は常に先頭でソートの影響を受けない()
    {
        // R-04
        var entries = new[] { TestEntries.File("a.txt"), TestEntries.Parent(), TestEntries.Folder("z") };
        var sorted = FolderListing.Sort(entries, SortOrder.Default);
        Assert.Equal("..", sorted[0].Name);
    }

    [Fact]
    public void フォルダはファイルより常に前に並ぶ()
    {
        // R-05
        var entries = new[]
        {
            TestEntries.File("aaa.txt"),
            TestEntries.Folder("zzz"),
            TestEntries.File("bbb.txt"),
            TestEntries.Folder("mmm"),
        };
        var sorted = FolderListing.Sort(entries, SortOrder.Default);
        Assert.Equal(["mmm", "zzz", "aaa.txt", "bbb.txt"], TestEntries.Names(sorted));
    }

    [Fact]
    public void 降順でもフォルダ優先は保たれる()
    {
        var entries = new[]
        {
            TestEntries.File("aaa.txt"),
            TestEntries.Folder("zzz"),
            TestEntries.File("bbb.txt"),
            TestEntries.Folder("mmm"),
        };
        var order = SortOrder.Default with { Direction = SortDirection.Descending };
        var sorted = FolderListing.Sort(entries, order);
        Assert.Equal(["zzz", "mmm", "bbb.txt", "aaa.txt"], TestEntries.Names(sorted));
    }

    [Fact]
    public void 属性はソート順に影響しない()
    {
        // R-05-4: 卓駆はシステム属性を先頭に集めるが、これは再現しない
        var entries = new[]
        {
            TestEntries.File("bbb.txt"),
            TestEntries.File("aaa.txt", FileAttributes.System),
            TestEntries.File("ccc.txt", FileAttributes.Hidden),
        };
        var sorted = FolderListing.Sort(entries, SortOrder.Default);
        Assert.Equal(["aaa.txt", "bbb.txt", "ccc.txt"], TestEntries.Names(sorted));
    }

    [Fact]
    public void 拡張子ソートにも自然順が効く()
    {
        // R-05-2-3: 分割書庫 .r01 .r02 … .r10
        var entries = new[]
        {
            TestEntries.File("x.r10"),
            TestEntries.File("x.r2"),
            TestEntries.File("x.r1"),
        };
        var order = SortOrder.Default with { Key = SortKey.Extension };
        var sorted = FolderListing.Sort(entries, order);
        Assert.Equal(["x.r1", "x.r2", "x.r10"], TestEntries.Names(sorted));
    }

    [Fact]
    public void 日付ソートの同着は名前で決着する()
    {
        // R-05-2-3
        var same = new DateTime(2026, 5, 5);
        var entries = new[]
        {
            TestEntries.File("b2.txt", mtime: same),
            TestEntries.File("b10.txt", mtime: same),
            TestEntries.File("b1.txt", mtime: same),
        };
        var order = SortOrder.Default with { Key = SortKey.Date };
        var sorted = FolderListing.Sort(entries, order);
        Assert.Equal(["b1.txt", "b2.txt", "b10.txt"], TestEntries.Names(sorted));
    }

    [Fact]
    public void ソートしないを選ぶと列挙順を保つがフォルダ優先は残る()
    {
        var entries = new[]
        {
            TestEntries.File("zzz.txt"),
            TestEntries.Folder("b"),
            TestEntries.File("aaa.txt"),
            TestEntries.Folder("a"),
        };
        var order = SortOrder.Default with { Key = SortKey.None };
        var sorted = FolderListing.Sort(entries, order);
        Assert.Equal(["b", "a", "zzz.txt", "aaa.txt"], TestEntries.Names(sorted));
    }

    [Fact]
    public void 既定のソートは名前昇順の自然順である()
    {
        // R-05-2-4
        Assert.Equal(new SortOrder(SortKey.Name, SortDirection.Ascending, ComparisonMode.Natural), SortOrder.Default);
    }

    [Fact]
    public void 日本語はエクスプローラと同じ順に並ぶ()
    {
        // B-02: 2026-09-11 に shlwapi の StrCmpLogicalW で実測した順序。
        // 現行の UTF-16 コードポイント順とは、かなの前後と漢字の並びが違う
        string[] input =
        [
            "あいうえお.txt", "かきくけこ.txt", "カキクケコ.txt",
            "会い.txt", "合い.txt", "愛.txt", "相.txt", "藍.txt", "逢い.txt",
        ];

        string[] expected =
        [
            "あいうえお.txt", "カキクケコ.txt", "かきくけこ.txt",
            "愛.txt", "逢い.txt", "会い.txt", "合い.txt", "相.txt", "藍.txt",
        ];

        Assert.Equal(expected, input.Order(NameComparers.Natural).ToArray());
        Assert.Equal(expected, input.Order(NameComparers.Strict).ToArray());
    }

    [Fact]
    public void 大文字小文字だけが違う名前でも並びが揺れない()
    {
        // 比較関数が同値と見た組は序数で決着させる。これが無いと Order の結果が入力順に依存する
        string[] a = ["A.txt", "a.txt"];
        string[] b = ["a.txt", "A.txt"];
        Assert.Equal(a.Order(NameComparers.Natural).ToArray(), b.Order(NameComparers.Natural).ToArray());
        Assert.Equal(a.Order(NameComparers.Strict).ToArray(), b.Order(NameComparers.Strict).ToArray());
    }

    [Fact]
    public void 大文字小文字だけが違う同着は小文字が先に来る()
    {
        // B-02: StrCmpLogicalW は大文字小文字を区別せず同値を返すため、序数で決めると
        // 'A'(0x41) が 'a'(0x61) より前に来てしまい、実測した「名前の昇順」と食い違う。
        // StrCmpW（Windows 自身の大文字小文字順）に先に訊いて解決する
        string[] input = ["A.txt", "a.txt", "B.txt", "b.txt"];
        string[] expected = ["a.txt", "A.txt", "b.txt", "B.txt"];

        Assert.Equal(expected, input.Order(NameComparers.Natural).ToArray());
        Assert.Equal(expected, input.Order(NameComparers.Strict).ToArray());
        Assert.True(NameComparers.Natural.Compare("a.txt", "A.txt") < 0);
    }

    [Fact]
    public void 数字だけの同着は桁数の序数比較のまま変わらない()
    {
        // "02" と "2" は数値としては同値なので、大文字小文字の解決を挟んでも
        // 最終的には従来どおり序数比較（'0' < '2'）で "02" が先に来る
        string[] input = ["2", "02"];
        Assert.Equal(["02", "2"], input.Order(NameComparers.Natural).ToArray());
    }
}
