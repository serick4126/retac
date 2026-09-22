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
    public void IDの無い項目を読むと入れ子の中まで新しいIDが付く()
    {
        var back = JsonSerializer.Deserialize<BookmarkSet>("""{"Bar":[{"Title":"g","Kind":"Group","Children":[{"Title":"","Kind":"Folder","Target":"x"}]}]}""", Json)!;
        Assert.NotEqual("", back.Bar[0].Id);
        Assert.NotEqual("", back.Bar[0].Children![0].Id);
        Assert.NotEqual(back.Bar[0].Id, back.Bar[0].Children![0].Id);
    }

    [Fact]
    public void IDは保存して読み戻せて_名前変更と移動では変わらない()
    {
        Bookmark a = new("a", BookmarkKind.Group, Children: []), b = new("b", BookmarkKind.Folder, @"C:\b");
        var set = new BookmarkSet { Bar = [a, b] };
        Assert.Equal(a.Id, JsonSerializer.Deserialize<BookmarkSet>(JsonSerializer.Serialize(set, Json), Json)!.Bar[0].Id);
        Assert.Equal(a.Id, (a with { Title = "z" }).Id);
        Assert.True(BookmarkRules.Move(set, b, a.Children!, 0));
        Assert.Equal(b.Id, a.Children![0].Id);
    }

    [Fact]
    public void 空と重複のIDは振り直し_最初の1件は残す()
    {
        Bookmark first = new("a", BookmarkKind.Folder, @"C:\a") { Id = "x" };
        var set = new BookmarkSet
        {
            Bar = [first, new("g", BookmarkKind.Group, Children: [new("b", BookmarkKind.Folder, @"C:\b") { Id = "x" }]) { Id = "" }],
            Other = [new("c", BookmarkKind.Folder, @"C:\c") { Id = null! }],
        };

        Assert.True(BookmarkRules.EnsureIds(set));
        var ids = new[] { set.Bar[0].Id, set.Bar[1].Id, set.Bar[1].Children![0].Id, set.Other[0].Id };
        Assert.Equal("x", ids[0]);
        Assert.Same(first, set.Bar[0]);
        Assert.Equal(4, ids.Distinct().Count());
        Assert.DoesNotContain(ids, id => string.IsNullOrEmpty(id));
        Assert.False(BookmarkRules.EnsureIds(set));
    }

    [Fact]
    public void 展開状態は今あるグループのIDだけを残す()
    {
        Bookmark inner = new("h", BookmarkKind.Group) { Id = "h" };
        var set = new BookmarkSet
        {
            Bar = [new("g", BookmarkKind.Group, Children: [inner]) { Id = "g" }, new("f", BookmarkKind.Folder, @"C:\f") { Id = "f" }],
        };
        Assert.Equal(["g", "h"], BookmarkRules.ExistingGroupIds(set, ["gone", "h", "f", "g"]).Order());
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
    [InlineData(0, 0, false)]    // 先頭より手前
    [InlineData(10, 0, false)]   // 1 つめの前半
    [InlineData(25, 1, false)]   // 1 つめの後半
    [InlineData(32, 1, false)]   // グループの左の 1/3
    [InlineData(45, 1, true)]    // グループの中央の 1/3
    [InlineData(58, 2, false)]   // グループの右の 1/3
    [InlineData(99, 2, false)]   // 末尾より後ろ
    public void 落とす位置(int x, int index, bool onto)
    {
        (int, int, bool)[] slots = [(0, 30, false), (30, 60, true)];
        Assert.Equal(new DropSpot(index, onto), BookmarkDrop.Hit(slots, x));
    }

    [Theory]
    [InlineData(5, 0, false)]     // ファイルのボタンの左の 1/3 は前への挿入（登録）
    [InlineData(15, 0, true)]     // ファイルのボタンの中央 1/3 は「項目の上」
    [InlineData(25, 1, false)]    // 右の 1/3 は後ろへの挿入
    public void どの種類も中央は項目の上で両端と間だけが挿入になる(int x, int index, bool onto)
    {
        (int, int, bool)[] slots = [(0, 30, true), (30, 60, true)];   // バーは全項目を HasCenter で渡す
        Assert.Equal(new DropSpot(index, onto), BookmarkDrop.Hit(slots, x));
    }

    [Theory]
    [InlineData(BookmarkKind.Group, false, OntoAction.IntoGroup)]
    [InlineData(BookmarkKind.Group, true, OntoAction.IntoGroup)]
    [InlineData(BookmarkKind.Folder, false, OntoAction.Transfer)]   // R-93
    [InlineData(BookmarkKind.Folder, true, OntoAction.None)]        // 並べ替えはフォルダへ転送しない
    [InlineData(BookmarkKind.File, false, OntoAction.None)]         // R-93: ファイル・コマンドの上は落とせない
    [InlineData(BookmarkKind.Command, false, OntoAction.None)]
    [InlineData(BookmarkKind.File, true, OntoAction.None)]
    public void 項目の上に落としたときに起こすこと(BookmarkKind kind, bool reorder, OntoAction expected)
    {
        Assert.Equal(expected, BookmarkDrop.Onto(kind, reorder));
    }

    [Fact]
    public void 同じ並びの中で後ろへ移すと位置は詰めて数える()
    {
        Bookmark a = new("a", BookmarkKind.Group, Children: []), b = new("b", BookmarkKind.Group), c = new("c", BookmarkKind.Group);
        var set = new BookmarkSet { Bar = [a, b, c] };

        Assert.True(BookmarkRules.Move(set, a, set.Bar, 2));   // b と c の間へ
        Assert.Equal([b, a, c], set.Bar);
        Assert.True(BookmarkRules.Move(set, c, a.Children!, 0));   // グループの中へ
        Assert.Equal([b, a], set.Bar);
        Assert.Same(c, Assert.Single(a.Children!));
    }

    [Fact]
    public void グループを自分の中へは移せない()
    {
        var inner = new Bookmark("inner", BookmarkKind.Group, Children: []);
        var outer = new Bookmark("outer", BookmarkKind.Group, Children: [inner]);
        var set = new BookmarkSet { Bar = [outer] };

        Assert.False(BookmarkRules.Move(set, outer, outer.Children!, 0));
        Assert.False(BookmarkRules.Move(set, outer, inner.Children!, 0));
        Assert.Same(outer, Assert.Single(set.Bar));
    }

    [Theory]
    [InlineData("", @"C:\Work\Docs\", "Docs")]
    [InlineData("", @"C:\", @"C:\")]
    [InlineData("書類", @"C:\Work\Docs", "書類")]
    public void 表示名は題名か末尾の名前(string title, string target, string expected) =>
        Assert.Equal(expected, BookmarkRules.DisplayName(new Bookmark(title, BookmarkKind.Folder, target), _ => ""));
}
