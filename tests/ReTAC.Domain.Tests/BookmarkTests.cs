using System.Text.Json;
using System.Text.Json.Serialization;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Navigation;

namespace ReTAC.Domain.Tests;

/// <summary>R-89 / R-92: ブックマークの型と、クイックアクセスの種類</summary>
public class BookmarkTests
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };
    private static readonly string Tool1 = new ToolTarget(1).Serialize();
    private static readonly string Tool2 = new ToolTarget(2).Serialize();

    [Fact]
    public void 種類の無い古いクイックアクセスはフォルダとして読む()
    {
        var entry = JsonSerializer.Deserialize<QuickAccessEntry>("""{"Title":"a","Path":"C:\\x"}""", Json)!;
        Assert.Equal(BookmarkKind.Folder, entry.Kind);
    }

    [Fact]
    public void クイックアクセスの重複は種類ごとに見る()
    {
        var list = new QuickAccessList();
        Assert.True(list.Add(new QuickAccessEntry("", @"C:\a", BookmarkKind.Folder)));
        Assert.True(list.Add(new QuickAccessEntry("", @"C:\a", BookmarkKind.File)));
        Assert.False(list.Add(new QuickAccessEntry("", @"c:\A\", BookmarkKind.Folder)));
        Assert.True(list.Add(new QuickAccessEntry("", "Refresh", BookmarkKind.Command)));
        Assert.False(list.Add(new QuickAccessEntry("別名", "Refresh", BookmarkKind.Command)));
    }

    [Fact]
    public void クイックアクセスから消えたツールとグループと読めないコマンドを落とす()
    {
        var list = new QuickAccessList();
        list.Add(new QuickAccessEntry("", @"C:\a"));
        list.Add(new QuickAccessEntry("", Tool1, BookmarkKind.Command));
        list.Add(new QuickAccessEntry("", Tool2, BookmarkKind.Command));
        list.Add(new QuickAccessEntry("g", "", BookmarkKind.Group));
        list.Add(new QuickAccessEntry("", "NoSuchCommand", BookmarkKind.Command));

        Assert.True(list.DropUnknownTools([1]));
        Assert.Equal([@"C:\a", Tool1], list.Items.Select(e => e.Path));
        Assert.False(list.DropUnknownTools([1]));
    }

    [Theory]
    [InlineData("", BookmarkKind.Group, "", false)]
    [InlineData("仕事", BookmarkKind.Group, "", true)]
    [InlineData("", BookmarkKind.Folder, "", false)]
    [InlineData("", BookmarkKind.Folder, @"C:\a", true)]
    [InlineData("", BookmarkKind.Command, "Refresh", false)]
    [InlineData("更新", BookmarkKind.Command, "NoSuchCommand", false)]
    [InlineData("更新", BookmarkKind.Command, "Refresh", true)]
    public void ブックマークの検証(string title, BookmarkKind kind, string target, bool valid) =>
        Assert.Equal(valid, BookmarkRules.Validate(new Bookmark(title, kind, target)) is null);

    [Fact]
    public void ブックマークから消えたツールを入れ子の中まで落とす()
    {
        var set = new BookmarkSet
        {
            Bar = [new("t", BookmarkKind.Command, Tool2), new("g", BookmarkKind.Group, Children: [new("t", BookmarkKind.Command, Tool1),
                   new("h", BookmarkKind.Group, Children: [new("t", BookmarkKind.Command, Tool2)])])],
            Other = [new("x", BookmarkKind.Command, "NoSuchCommand"), new("", BookmarkKind.Folder, @"C:\a")],
        };

        Assert.True(BookmarkRules.DropUnknownTools(set, [1]));
        var group = Assert.Single(set.Bar);
        Assert.Equal(Tool1, group.Children![0].Target);
        Assert.Empty(group.Children[1].Children!);
        Assert.Equal(@"C:\a", Assert.Single(set.Other).Target);
        Assert.False(BookmarkRules.DropUnknownTools(set, [1]));
    }

    [Fact]
    public void ブックマークの入れ子は保存して読み戻せる()
    {
        var set = new BookmarkSet { Bar = [new("a", BookmarkKind.Group, Children: [new("b", BookmarkKind.Group, Children: [new("", BookmarkKind.Folder, @"C:\c")])])] };
        var back = JsonSerializer.Deserialize<BookmarkSet>(JsonSerializer.Serialize(set, Json), Json)!;
        Assert.Equal(@"C:\c", back.Bar[0].Children![0].Children![0].Target);
        Assert.Equal(BookmarkKind.Group, back.Bar[0].Kind);
    }

    [Fact]
    public void 空の設定からは空のブックマークを読む()
    {
        var set = JsonSerializer.Deserialize<BookmarkSet>("{}", Json)!;
        Assert.Empty(set.Bar);
        Assert.Empty(set.Other);
    }

    [Fact]
    public void 取り除くのは参照が同じ項目だけ()
    {
        var first = new Bookmark("", BookmarkKind.Folder, @"C:\a");
        var second = new Bookmark("", BookmarkKind.Folder, @"C:\a");   // 値は同じ（重複を許す）
        var set = new BookmarkSet { Bar = [first], Other = [new("g", BookmarkKind.Group, Children: [second])] };

        Assert.True(BookmarkRules.Remove(set, second));
        Assert.Same(first, Assert.Single(set.Bar));
        Assert.Empty(set.Other[0].Children!);
        Assert.False(BookmarkRules.Remove(set, second));
    }

    [Fact]
    public void 位置は入れ子の中まで参照で探す()
    {
        var first = new Bookmark("", BookmarkKind.Folder, @"C:\a");
        var second = new Bookmark("", BookmarkKind.Folder, @"C:\a");   // 値は同じ（重複を許す）
        var group = new Bookmark("g", BookmarkKind.Group, Children: [new("", BookmarkKind.File, @"C:\x"), second]);
        var set = new BookmarkSet { Bar = [first], Other = [group] };

        var (list, index) = BookmarkRules.Locate(set, second)!.Value;
        Assert.Same(group.Children, list);
        Assert.Equal(1, index);
        Assert.Null(BookmarkRules.Locate(set, new Bookmark("", BookmarkKind.Folder, @"C:\a")));
    }

    [Theory]
    [InlineData("", @"C:\Work\Docs\", "Docs")]
    [InlineData("", @"C:\", @"C:\")]
    [InlineData("書類", @"C:\Work\Docs", "書類")]
    public void 表示名は題名か末尾の名前(string title, string target, string expected) =>
        Assert.Equal(expected, BookmarkRules.DisplayName(new Bookmark(title, BookmarkKind.Folder, target), _ => ""));
}
