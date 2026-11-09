using System.Text.Json;
using System.Text.Json.Serialization;
using ReTAC.App;
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
    [InlineData("", BookmarkKind.Command, "Refresh", true)]
    [InlineData("更新", BookmarkKind.Command, "NoSuchCommand", false)]
    [InlineData("更新", BookmarkKind.Command, "Refresh", true)]
    public void ブックマークの検証(string title, BookmarkKind kind, string target, bool valid) =>
        Assert.Equal(valid, BookmarkRules.Validate(new Bookmark(title, kind, target)) is null);

    [Theory]
    // IconOnly なし: アイコンがあれば全体どおり、無ければ「アイコンだけ」だけ名前へ落ちる
    [InlineData(false, BookmarkBarStyle.IconAndText, false, BookmarkBarStyle.IconAndText)]
    [InlineData(false, BookmarkBarStyle.IconAndText, true, BookmarkBarStyle.IconAndText)]
    [InlineData(false, BookmarkBarStyle.IconOnly, false, BookmarkBarStyle.TextOnly)]
    [InlineData(false, BookmarkBarStyle.IconOnly, true, BookmarkBarStyle.IconOnly)]
    [InlineData(false, BookmarkBarStyle.TextOnly, false, BookmarkBarStyle.TextOnly)]
    [InlineData(false, BookmarkBarStyle.TextOnly, true, BookmarkBarStyle.TextOnly)]
    // IconOnly あり: アイコンがあれば全体が何であれ「アイコンだけ」（項目の指定が優先）。アイコンが無ければ全体どおり
    [InlineData(true, BookmarkBarStyle.IconAndText, false, BookmarkBarStyle.IconAndText)]
    [InlineData(true, BookmarkBarStyle.IconAndText, true, BookmarkBarStyle.IconOnly)]
    [InlineData(true, BookmarkBarStyle.IconOnly, false, BookmarkBarStyle.TextOnly)]
    [InlineData(true, BookmarkBarStyle.IconOnly, true, BookmarkBarStyle.IconOnly)]
    [InlineData(true, BookmarkBarStyle.TextOnly, false, BookmarkBarStyle.TextOnly)]
    [InlineData(true, BookmarkBarStyle.TextOnly, true, BookmarkBarStyle.IconOnly)]
    public void バーのボタンの表示の形(bool iconOnly, BookmarkBarStyle global, bool hasIcon, BookmarkBarStyle expected)
    {
        var b = new Bookmark("x", BookmarkKind.Command, "Refresh") { IconOnly = iconOnly };
        Assert.Equal(expected, BookmarkRules.BarStyleOf(b, global, hasIcon));
    }

    [Fact]
    public void IconOnlyはwithで保たれ_JSONの往復でも残り_欄が無ければfalseで埋まる()
    {
        var b = new Bookmark("a", BookmarkKind.Folder, @"C:\a") { IconOnly = true };
        Assert.True((b with { Title = "z" }).IconOnly);

        var set = new BookmarkSet { Bar = [b] };
        var back = JsonSerializer.Deserialize<BookmarkSet>(JsonSerializer.Serialize(set, Json), Json)!;
        Assert.True(back.Bar[0].IconOnly);

        var noField = JsonSerializer.Deserialize<BookmarkSet>("""{"Bar":[{"Title":"x","Kind":"Folder","Target":"C:\\a"}]}""", Json)!;
        Assert.False(noField.Bar[0].IconOnly);
    }

    [Fact]
    public void Bookmark_DropModeJson()
    {
        var noField = JsonSerializer.Deserialize<BookmarkSet>("""{"Bar":[{"Title":"x","Kind":"Folder","Target":"C:\\a"}]}""", Json)!;
        Assert.Equal(BookmarkDropMode.Standard, noField.Bar[0].DropMode);

        var b = new Bookmark("a", BookmarkKind.Folder, @"C:\a") { DropMode = BookmarkDropMode.Copy };
        Assert.Equal(BookmarkDropMode.Copy, (b with { Title = "z" }).DropMode);
        var back = JsonSerializer.Deserialize<BookmarkSet>(JsonSerializer.Serialize(new BookmarkSet { Bar = [b] }, Json), Json)!;
        Assert.Equal(BookmarkDropMode.Copy, back.Bar[0].DropMode);
    }

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

    [Theory]
    [InlineData(true, 1, true)]
    [InlineData(true, 0, false)]   // 案内の文だけ（R-107）
    [InlineData(false, 1, false)]  // 非表示（R-107）
    [InlineData(false, 0, false)]
    public void バーへフォーカスしてよいのは表示中かつ項目があるときだけ(bool shown, int barCount, bool expected) =>
        Assert.Equal(expected, BookmarkRules.CanFocusBar(shown, barCount));

    [Theory]
    // → は段数を問わず、開けるものがあれば呑み込まない（標準どおり開く）
    [InlineData(1, true, true, false)]
    [InlineData(2, true, true, false)]
    // → は開けるものが無ければ、段数を問わず呑み込む（バーの次のボタンへ漏らさない）
    [InlineData(1, false, true, true)]
    [InlineData(2, false, true, true)]
    // ← は 1 段目だけ呑み込む（バーの前のボタンへ漏らさない）。has-submenu は ← の判定に関係しない
    [InlineData(1, true, false, true)]
    [InlineData(1, false, false, true)]
    // ← は 2 段目以降なら呑み込まない（標準どおり 1 段閉じて戻る）
    [InlineData(2, true, false, false)]
    [InlineData(2, false, false, false)]
    public void ドロップダウン内の矢印キーは段数と開けるかどうかで呑み込むかが決まる(int level, bool hasSubmenu, bool forward, bool expected) =>
        Assert.Equal(expected, BookmarkRules.SwallowArrowKey(level, hasSubmenu, forward));

    [Theory]
    // 開けないボタン（ファイル・コマンド）は ↑↓ とも何もしない
    [InlineData(false, true, BookmarkRules.BarVerticalKeyAction.None)]
    [InlineData(false, false, BookmarkRules.BarVerticalKeyAction.None)]
    // 開けるボタン（フォルダ・グループ・»）は ↓ で先頭、↑ で末尾を選んで開く
    [InlineData(true, true, BookmarkRules.BarVerticalKeyAction.OpenSelectFirst)]
    [InlineData(true, false, BookmarkRules.BarVerticalKeyAction.OpenSelectLast)]
    public void バーのボタンの上下キーは開けるかどうかと方向で決まる(bool canOpen, bool down, BookmarkRules.BarVerticalKeyAction expected) =>
        Assert.Equal(expected, BookmarkRules.BarVerticalKey(canOpen, down));

    [Theory]
    // 1 段目: ↑ は先頭のときだけ、↓ は末尾のときだけバーへ戻る
    [InlineData(1, true, false, false, true)]
    [InlineData(1, false, false, false, false)]
    [InlineData(1, false, true, true, true)]
    [InlineData(1, false, false, true, false)]
    // 先頭かつ末尾（項目が 1 件だけ）でも、方向に応じて戻る
    [InlineData(1, true, true, false, true)]
    [InlineData(1, true, true, true, true)]
    // 2 段目以降は対象外（標準の折り返しのまま。先頭・末尾でも戻らない）
    [InlineData(2, true, false, false, false)]
    [InlineData(2, false, true, true, false)]
    public void 一段目の端だけバーのボタンへ戻る(int level, bool isFirst, bool isLast, bool down, bool expected) =>
        Assert.Equal(expected, BookmarkRules.ReturnsToBarButton(level, isFirst, isLast, down));

    [Theory]
    // 途中は並び順で隣へ
    [InlineData(1, 3, true, 2)]
    [InlineData(1, 3, false, 0)]
    [InlineData(0, 3, true, 1)]
    [InlineData(2, 3, false, 1)]
    // 先頭で ↑・末尾で ↓ は「»」のボタンへ戻る（null）
    [InlineData(0, 3, false, null)]
    [InlineData(2, 3, true, null)]
    // 1 件だけなら ↑↓ とも戻る
    [InlineData(0, 1, true, null)]
    [InlineData(0, 1, false, null)]
    // 一覧に見つからないときも戻る
    [InlineData(-1, 3, true, null)]
    public void オーバーフローの一覧の上下は並び順で移り端でボタンへ戻る(int index, int count, bool down, int? expected) =>
        Assert.Equal(expected, BookmarkRules.OverflowListStep(index, count, down));

    // ---- R-147: フォルダ・ファイルのブックマークは設定ファイルの形の式を持つ ----

    private const string Work = @"C:\work";

    private static string? P32Env(string name) => name.ToUpperInvariant() switch
    {
        "P32_INNER" => "in",
        "P32_BASE" => @"D:\base",
        "P32_REL" => @"rel\x",
        _ => null,
    };

    private static Bookmark Folder(string target) => new("", BookmarkKind.Folder, target);

    [Fact]
    public void 登録_変数を使わない絶対パスは正規の形で保存する()
    {
        var (target, error) = BookmarkPath.FromField(Folder(""), @"C:\x\y", Work, P32Env);
        Assert.Null(error);
        Assert.Equal(@"C:\x\y", target);
    }

    [Fact]
    public void 登録_置き換えると絶対パスになる式は式のまま保存し使うときに置き換える()
    {
        var (target, error) = BookmarkPath.FromField(Folder(""), @"%P32_BASE%\sub", Work, P32Env);
        Assert.Null(error);
        Assert.Equal(@"%P32_BASE%\sub", PathExpression.Display(target!));
        Assert.Equal(@"D:\base\sub", BookmarkPath.Resolve(Folder(target!), P32Env));
        // 未定義の変数は文字として保存する（後で定義されても意味が変わらない）
        var (undefined, _) = BookmarkPath.FromField(Folder(""), @"C:\%P32_NOSUCH%", Work, P32Env);
        Assert.Equal(@"|C:\%%P32_NOSUCH%%", undefined);
    }

    [Fact]
    public void 登録_置き換えても相対パスならカレントフォルダ基準の実パスを式に書いて保存する()
    {
        var (target, error) = BookmarkPath.FromField(Folder(""), @"sub\%P32_INNER%", Work, P32Env);
        Assert.Null(error);
        // 変数は残らない
        Assert.Equal(@"C:\work\sub\in", target);
        var (rel, _) = BookmarkPath.FromField(Folder(""), @"%P32_REL%", Work, P32Env);
        Assert.Equal(@"C:\work\rel\x", rel);
    }

    [Fact]
    public void 登録_相対のP32_OUTERの例は保存して読み直して使うまで同じ実パス()
    {
        // P32_OUTER=sub\%P32_INNER%（値の中の % は置き換えない）、カレントフォルダ C:\work
        static string? Env(string n) => n switch { "P32_OUTER" => @"sub\%P32_INNER%", "P32_INNER" => "in", _ => null };
        var (target, error) = BookmarkPath.FromField(Folder(""), "%P32_OUTER%", Work, Env);
        Assert.Null(error);
        Assert.Equal(@"|C:\work\sub\%%P32_INNER%%", target);
        Assert.Equal(@"C:\work\sub\%P32_INNER%", BookmarkPath.Resolve(Folder(target!), Env));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"C:\a|b")]
    [InlineData("%P32_EMPTY%")]
    public void 登録_空や読めない入力は誤り(string input)
    {
        var (target, error) = BookmarkPath.FromField(Folder(""), input, Work, n => n == "P32_EMPTY" ? "" : null);
        Assert.Null(target);
        Assert.NotNull(error);
    }

    [Fact]
    public void 編集_欄を書き換えていなければ前の保存した値を一字も変えない()
    {
        // 旧設定の値（印が無く % が 2 つ以上）を勝手に式にしない
        const string legacy = @"C:\a%b%c";
        var preset = Folder(legacy);
        var (target, error) = BookmarkPath.FromField(preset, PathExpression.Display(preset.Target), Work, P32Env);
        Assert.Null(error);
        Assert.Equal(legacy, target);

        var marked = Folder(@"|%P32_BASE%\x");
        Assert.Equal(marked.Target, BookmarkPath.FromField(marked, PathExpression.Display(marked.Target), Work, P32Env).Target);
        // 書き換えたら、新しい式として保存し直す（未定義の変数は文字なので % はすべて二重にする）
        Assert.Equal(@"|C:\a%%b%%c", BookmarkPath.FromField(preset, @"C:\a%b%c ", Work, _ => null).Target);
    }

    [Theory]
    [InlineData(@"C:\work\50%")]
    [InlineData(@"C:\work\%a%b")]
    [InlineData(@"D:\%TEMP%")]
    [InlineData(@"C:\100%%")]
    public void 実パスから作る登録は式に書き保存して読み直すと同じ実パスに戻る(string real)
    {
        // 現在のフォルダを追加・カーソル位置・ドラッグ・参照・管理の追加がここを通る
        var stored = BookmarkPath.Store(real);
        Assert.Equal(real, BookmarkPath.Resolve(Folder(stored), n => n.ToUpperInvariant() is "TEMP" or "A" ? "VALUE" : null));
        Assert.Equal(real, BookmarkPath.Resolve(Folder(stored), _ => null));
    }

    [Fact]
    public void 実パスの保存はパーセントが2つ以上のときだけ印を付ける()
    {
        Assert.Equal(@"C:\work\50%", BookmarkPath.Store(@"C:\work\50%"));
        Assert.Equal(@"|D:\%%TEMP%%", BookmarkPath.Store(@"D:\%TEMP%"));
    }

    [Fact]
    public void ドラッグでの登録は式に書いた値を足す()
    {
        var dir = Path.Combine(Path.GetTempPath(), "P32_5_%drag%_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);   // 毎回新しい名前。片付けは手で行う（S8）
        var dest = new List<Bookmark>();
        Assert.True(BookmarkDropZone.InsertFiles(dest, 0, [dir]));
        Assert.Equal(BookmarkPath.Store(dir), dest[0].Target);
        Assert.Equal(dir, BookmarkPath.Resolve(dest[0], _ => null));
    }

    [Fact]
    public void 使えない項目_使う入口はnullを返し旧設定の値は登録し直しの知らせ()
    {
        var other = Folder(@"|%P32_BASE%\x");
        Assert.Equal(@"D:\base\x", BookmarkPath.Resolve(other, P32Env));
        // 環境が変わって変数が相対の値・未定義・空になった
        Assert.Null(BookmarkPath.Resolve(other, n => n == "P32_BASE" ? "rel" : null));
        Assert.Null(BookmarkPath.Resolve(other, _ => null));
        Assert.Null(BookmarkPath.Resolve(other, n => n == "P32_BASE" ? "" : null));
        Assert.Equal(@"%P32_BASE%\x は見つかりません。", BookmarkPath.Notice(other, _ => null));
        Assert.Null(BookmarkPath.Notice(other, P32Env));

        // 旧設定の値: 変数が定義されていても使わない
        var legacy = Folder(@"C:\work\%P32_BASE%\%x%");
        Assert.Null(BookmarkPath.Resolve(legacy, P32Env));
        Assert.Equal(@"C:\work\%P32_BASE%\%x% は登録し直してください。", BookmarkPath.Notice(legacy, P32Env));
        Assert.Null(BookmarkPath.Resolve(new Bookmark("", BookmarkKind.File, @"C:\a%b%c"), P32Env));
        // 相対の値は作業ディレクトリで解決しない
        Assert.Null(BookmarkPath.Resolve(Folder("sub"), P32Env));
    }

    [Fact]
    public void 管理の追加_フォルダとファイルは実パスを式に書いて足す()
    {
        const string real = @"C:\work\%x%y%";
        var b = BookmarkPath.ForRealPath(BookmarkKind.File, real);
        Assert.Equal(BookmarkKind.File, b.Kind);
        Assert.Equal("", b.Title);
        Assert.Equal(@"|C:\work\%%x%%y%%", b.Target);
        Assert.Equal(real, BookmarkPath.Resolve(b, _ => null));
    }

    [Fact]
    public void ドロップ_宛先は使える項目の実パスだけでそのまま転送の欄の既定値になる()
    {
        const string real = @"D:\base\%P32_INNER%\z";
        var folder = Folder(BookmarkPath.Store(real));
        var dest = BookmarkPath.Resolve(folder, P32Env);
        Assert.Equal(real, dest);
        // 転送の入力欄へは式に書いて入れる。欄を解決すると同じ宛先に戻る（置き換わらない）
        Assert.Equal(dest, PathResolver.Resolve(Work, PathField.Text(dest!), P32Env));
        // 使えない項目は宛先にならない
        Assert.Null(BookmarkPath.Resolve(Folder(@"C:\w\%a%b%"), P32Env));
        Assert.Null(BookmarkPath.Resolve(Folder(@"|%P32_NOSUCH%\z"), P32Env));
    }
}
