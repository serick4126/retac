using ReTAC.App;
using ReTAC.Domain.Navigation;

namespace ReTAC.Domain.Tests;

/// <summary>クイックアクセス（16.2 節）</summary>
public class QuickAccessTests
{
    /// <summary>並べ替え・表示の検証用に 2 件入れたリストを作る。既定の登録は無い（B-05）。</summary>
    private static QuickAccessList TwoEntries()
    {
        var list = new QuickAccessList(_ => null);
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
        var list = new QuickAccessList(_ => null);
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
        var list = new QuickAccessList(_ => null);

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
        var list = new QuickAccessList(_ => null);
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
        var list = new QuickAccessList(_ => null);

        list.ReplaceAll([new QuickAccessEntry("仕事", @"D:\work"), new QuickAccessEntry("別名", @"D:\work")]);

        Assert.Equal(["仕事"], list.Items.Select(e => e.Title));
    }

    // ---- R-147: フォルダ・ファイルは設定ファイルの形の式を持つ（作業ディレクトリ・カレントフォルダには依らない） ----

    private static string? P32Env(string name) => name.ToUpperInvariant() switch
    {
        "P32_BASE" => @"D:\base",
        "P32_REL" => @"rel\x",
        _ => null,
    };

    private static Func<string, string?> Moved(string? @base) =>
        name => name.Equals("P32_BASE", StringComparison.OrdinalIgnoreCase) ? @base : null;

    private static (string? Stored, string? Error) Field(string? preset, string text, Func<string, bool>? exists = null) =>
        QuickAccessPath.FromField(preset, text, P32Env, exists ?? (_ => true));

    [Fact]
    public void 登録_引用符で囲んだ絶対パスは引用符を外して保存する()
    {
        Assert.Equal((@"C:\Windows", (string?)null), Field(null, "\"C:\\Windows\""));
    }

    [Fact]
    public void 登録_変数を含む絶対パスは正規の形の式で保存し_存在は実パスで確かめる()
    {
        string? asked = null;
        var (stored, error) = Field(null, @"%P32_BASE%\sub", p => { asked = p; return true; });
        Assert.Null(error);
        Assert.Equal(@"|%P32_BASE%\sub", stored);   // % が 2 つ以上の式は印付き
        Assert.Equal(@"D:\base\sub", asked);
    }

    [Fact]
    public void 登録_置き換えて相対パスになる入力は存在確認の前に拒む()
    {
        var (stored, error) = Field(null, @"%P32_REL%", _ => true);
        Assert.Null(stored);
        Assert.Equal(@"%P32_REL% は存在しません。", error);
        Assert.Equal(@"sub は存在しません。", Field(null, "sub").Error);
    }

    [Fact]
    public void 登録_存在しない絶対パスと空は拒む()
    {
        Assert.Equal(@"C:\none は存在しません。", Field(null, @"C:\none", _ => false).Error);
        Assert.NotNull(Field(null, "").Error);
    }

    [Fact]
    public void 登録_書き換えていない欄は保存した値を一字も変えない()
    {
        // 旧設定の値（印なしで % が 2 つ）は旧設定のまま残す。存在確認もしない（題名だけ直せるように）
        const string legacy = @"C:\a%b%c";
        Assert.Equal((legacy, (string?)null), Field(legacy, PathExpression.Display(legacy), _ => false));
        const string marked = @"|C:\%P32_BASE%\%x";
        Assert.Equal((marked, (string?)null), Field(marked, PathExpression.Display(marked), _ => false));
    }

    [Fact]
    public void 登録_パーセントを二つ含む実パスは式に書いた印付きの値で往復する()
    {
        const string folder = @"C:\work\%a%b";
        var stored = QuickAccessPath.Store(folder);   // 追加・参照・現在のフォルダの初期値すべて同じ入口
        Assert.StartsWith("|", stored);
        Assert.Equal(folder, PathExpression.ResolveSaved(stored, P32Env));
        // 現在のフォルダの初期値として欄に出した文字を、そのまま登録しても同じ値になる
        Assert.Equal((stored, (string?)null), Field(null, PathField.Text(folder)));
    }

    [Fact]
    public void 重複_変数の形と絶対パスの形は同じ場所なら重複()
    {
        var list = new QuickAccessList(P32Env);
        Assert.True(list.Add(new QuickAccessEntry("", @"|%P32_BASE%\x")));
        Assert.False(list.Add(new QuickAccessEntry("", @"d:\BASE\x\")));
        Assert.True(list.Add(new QuickAccessEntry("", @"D:\base\x", BookmarkKind.File)));   // 種類が違えば別
        Assert.Equal(0, list.IndexOfPath(@"D:\base\x"));
    }

    [Fact]
    public void 重複_使えない項目は保存した文字で比べ_コマンドは文字のまま()
    {
        var list = new QuickAccessList(P32Env);
        Assert.True(list.Add(new QuickAccessEntry("", @"C:\a%b%c")));            // 旧設定の値
        Assert.False(list.Add(new QuickAccessEntry("", @"c:\A%B%C\")));
        Assert.True(list.Add(new QuickAccessEntry("", @"|%P32_REL%")));            // 相対になる
        Assert.False(list.Add(new QuickAccessEntry("", @"|%p32_rel%")));
        Assert.True(list.Add(new QuickAccessEntry("", "Refresh", BookmarkKind.Command)));
        Assert.False(list.Add(new QuickAccessEntry("別", "Refresh", BookmarkKind.Command)));
    }

    [Fact]
    public void 使えない項目_環境変数が変わると_J_と下矢印と重複の判定が同じ結論になる()
    {
        var env = Moved(null);   // 設定した後で変数が無くなった
        var entry = new QuickAccessEntry("", @"|%P32_BASE%\x");
        Assert.Null(QuickAccessPath.Resolve(entry, env));
        var jump = QuickAccessPath.Notice(entry.Path, env);
        Assert.Equal(@"%P32_BASE%\x は使えません。", jump);
        var pick = PathRecall.Pick(new RecallItem(PathExpression.Display(entry.Path), entry.Path, true), env);
        Assert.Null(pick.Insert);
        Assert.Equal(jump, pick.Message);
        // 重複: 実パスにならないので保存した文字だけで比べる（別の場所の絶対パスとは一致しない）
        var list = new QuickAccessList(env);
        Assert.True(list.Add(entry));
        Assert.True(list.Add(new QuickAccessEntry("", @"D:\base\x")));
        Assert.False(list.Add(entry with { Title = "別名" }));
    }

    [Fact]
    public void 使えない項目_旧設定の値は登録し直しを促す_入口で同じ()
    {
        var entry = new QuickAccessEntry("", @"C:\a%b%c");
        Assert.Null(QuickAccessPath.Resolve(entry, P32Env));
        var jump = QuickAccessPath.Notice(entry.Path, P32Env);
        Assert.Equal(@"C:\a%b%c は登録し直してください。", jump);
        Assert.Equal(jump, PathRecall.Pick(new RecallItem(entry.Path, entry.Path, true), P32Env).Message);
    }

    [Fact]
    public void 使える項目はJと下矢印で同じ実パスになる_カレントフォルダに依らない()
    {
        var entry = new QuickAccessEntry("", QuickAccessPath.Store(@"C:\w\%a%b"));
        var real = QuickAccessPath.Resolve(entry, P32Env);
        Assert.Equal(@"C:\w\%a%b", real);
        Assert.Null(QuickAccessPath.Notice(entry.Path, P32Env));
        var insert = PathRecall.Pick(new RecallItem("", entry.Path, true), P32Env).Insert!;
        Assert.Equal(real, PathResolver.Resolve(@"Z:\other", insert, P32Env));
        Assert.Null(QuickAccessPath.Resolve(new QuickAccessEntry("", "Refresh", BookmarkKind.Command), P32Env));
    }

    [Fact]
    public void ブックマークから写す_印付きの式はそのまま写し_使えなければ追加しない()
    {
        var marked = new Bookmark("b", BookmarkKind.Folder, QuickAccessPath.Store(@"C:\w\%a%b"));
        var (entry, error) = QuickAccessPath.FromBookmark(marked, P32Env);
        Assert.Null(error);
        Assert.Equal(new QuickAccessEntry("b", marked.Target, BookmarkKind.Folder), entry);

        var legacy = new Bookmark("l", BookmarkKind.File, @"C:\a%b%c");
        Assert.Equal((null, @"C:\a%b%c は登録し直してください。"), QuickAccessPath.FromBookmark(legacy, P32Env));
        var gone = new Bookmark("g", BookmarkKind.Folder, @"|%P32_BASE%\x");
        Assert.Equal((null, @"%P32_BASE%\x は使えません。"), QuickAccessPath.FromBookmark(gone, Moved(null)));
        var command = new Bookmark("c", BookmarkKind.Command, "Refresh");
        Assert.Equal(new QuickAccessEntry("c", "Refresh", BookmarkKind.Command), QuickAccessPath.FromBookmark(command, Moved(null)).Entry);
    }

    [Fact]
    public void 一覧の表示は印を外した式()
    {
        var list = new QuickAccessList(P32Env) { ShowTitles = false };
        Assert.Equal(@"C:\%a%b", list.LabelOf(new QuickAccessEntry("t", @"|C:\%a%b")));
    }

    private static string? SameEnv(string name) => name.ToUpperInvariant() is "P32_A" or "P32_B" ? @"D:\same" : null;

    [Fact]
    public void 読み込み_環境で同じ場所になるだけの項目は両方残り_登録では断る()
    {
        var a = new QuickAccessEntry("a", @"|%P32_A%\x");
        var b = new QuickAccessEntry("b", @"|%P32_B%\x");

        var list = new QuickAccessList(SameEnv);
        Assert.True(list.AddLoaded(a));
        Assert.True(list.AddLoaded(b));
        Assert.Equal([a, b], list.Items);

        list.ReplaceAll([a, b]);
        Assert.Equal([a, b], list.Items);

        var user = new QuickAccessList(SameEnv);
        Assert.True(user.Add(a));
        Assert.False(user.Add(b));
        Assert.Equal(-1, new QuickAccessList(SameEnv).IndexOf(b));
    }

    [Fact]
    public void 読み込み_保存した文字が同じ重複は従来どおり後ろを落とす()
    {
        var list = new QuickAccessList(SameEnv);
        list.ReplaceAll([new QuickAccessEntry("先", @"D:\work"), new QuickAccessEntry("後", @"d:\WORK\"),
                         new QuickAccessEntry("", "Refresh", BookmarkKind.Command), new QuickAccessEntry("", "Refresh", BookmarkKind.Command),
                         new QuickAccessEntry("", @"D:\work", BookmarkKind.File)]);
        Assert.Equal(["先", "", ""], list.Items.Select(e => e.Title));
        Assert.Equal([BookmarkKind.Folder, BookmarkKind.Command, BookmarkKind.File], list.Items.Select(e => e.Kind));
    }
}
