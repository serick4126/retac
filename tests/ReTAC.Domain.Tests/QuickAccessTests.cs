using ReTAC.Domain.Navigation;

namespace ReTAC.Domain.Tests;

/// <summary>クイックアクセス（16.2 節）</summary>
public class QuickAccessTests
{
    /// <summary>並べ替え・表示の検証用に 2 件入れたリストを作る。既定の登録は無い（B-05）。</summary>
    private static QuickAccessList TwoEntries()
    {
        var list = new QuickAccessList();
        list.Add(new QuickAccessEntry("Screenshots", @"C:\Users\tester\Pictures\Screenshots"));
        list.Add(new QuickAccessEntry("Download", @"C:\Users\tester\Downloads"));
        return list;
    }

    [Fact]
    public void 並べ替えは端で止まる()
    {
        var list = TwoEntries();

        Assert.Equal(0, list.Move(0, -1));                       // 先頭より上へは動かない
        Assert.Equal("Screenshots", list.Items[0].Title);

        Assert.Equal(1, list.Move(0, 1));
        Assert.Equal(["Download", "Screenshots"], list.Items.Select(e => e.Title));
    }

    [Fact]
    public void タイトルを表示しない設定ならパスを出す()
    {
        var list = TwoEntries();

        Assert.Equal("Screenshots", list.LabelOf(list.Items[0]));

        list.ShowTitles = false;
        Assert.Equal(@"C:\Users\tester\Pictures\Screenshots", list.LabelOf(list.Items[0]));
    }

    [Fact]
    public void タイトルが空ならタイトル表示でもパスを出す()
    {
        var list = new QuickAccessList();
        list.Add(new QuickAccessEntry("", @"D:\work"));

        Assert.Equal(@"D:\work", list.LabelOf(list.Items[0]));
    }

    [Fact]
    public void 範囲外の位置を指定しても壊れない()
    {
        var list = TwoEntries();

        list.RemoveAt(9);
        list.Replace(-1, new QuickAccessEntry("x", @"C:\x"));

        Assert.Equal(2, list.Items.Count);
    }

    [Fact]
    public void 同じフォルダは二重に登録しない()
    {
        var list = new QuickAccessList();

        Assert.True(list.Add(new QuickAccessEntry("仕事", @"D:\work")));
        Assert.False(list.Add(new QuickAccessEntry("別名", @"D:\work")));
        Assert.Single(list.Items);
        Assert.Equal("仕事", list.Items[0].Title);
    }

    [Theory]
    [InlineData(@"d:\WORK")]
    [InlineData(@"D:\work\")]
    public void 大文字小文字と末尾の区切りは同じフォルダとみなす(string path)
    {
        var list = new QuickAccessList();
        list.Add(new QuickAccessEntry("仕事", @"D:\work"));

        Assert.False(list.Add(new QuickAccessEntry("別名", path)));
        Assert.Equal(0, list.IndexOfPath(path));
    }

    [Fact]
    public void 他の項目と同じフォルダへは差し替えない()
    {
        var list = TwoEntries();
        var first = list.Items[0];

        Assert.False(list.Replace(1, new QuickAccessEntry("x", first.Path)));
        Assert.NotEqual(first.Path, list.Items[1].Path);
    }

    [Fact]
    public void 自分自身と同じパスへの差し替えは通る()
    {
        var list = TwoEntries();
        var second = list.Items[1];

        Assert.True(list.Replace(1, second with { Title = "改名" }));
        Assert.Equal("改名", list.Items[1].Title);
    }

    [Fact]
    public void ReplaceAllは実体を差し替えず中身だけ入れ替える()
    {
        var list = TwoEntries();

        list.ReplaceAll([new QuickAccessEntry("新規", @"D:\new")]);

        Assert.Equal([@"D:\new"], list.Items.Select(e => e.Path));
    }

    [Fact]
    public void ReplaceAllもAddと同じ重複規則で弾く()
    {
        var list = new QuickAccessList();

        list.ReplaceAll([new QuickAccessEntry("仕事", @"D:\work"), new QuickAccessEntry("別名", @"D:\work")]);

        Assert.Equal(["仕事"], list.Items.Select(e => e.Title));
    }
}
