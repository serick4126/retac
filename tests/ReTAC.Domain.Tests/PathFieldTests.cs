using ReTAC.App;
using ReTAC.Domain.Navigation;

namespace ReTAC.Domain.Tests;

/// <summary>R-147: 実パスを欄・ジャンプの入口・参照へ渡すときの文字（画面に触れずに確かめる）</summary>
public class PathFieldTests
{
    private const string Current = @"C:\work";

    private static string? Env(string name) => name.Equals("P32_DEST", StringComparison.OrdinalIgnoreCase) ? @"C:\other" : null;

    // 普通・% が 1 つ・% が 2 つ以上・定義済みの変数と同じ表記
    public static TheoryData<string> Folders => new()
    {
        @"C:\work\plain", @"C:\work\50%", @"C:\work\%a%b", @"C:\work\100%%", @"C:\work\%P32_DEST%",
    };

    [Theory, MemberData(nameof(Folders))]
    public void 履歴_クイックアクセス_手入力は同じ文字なら同じ実パス(string folder)
    {
        var typed = PathResolver.Resolve(Current, PathField.Text(folder), Env);
        var viaHistory = PathResolver.Resolve(Current,
            PathRecall.Pick(new RecallItem(folder, folder, false), Env).Insert!, Env);
        var saved = PathExpression.Store(PathExpression.Literal(folder));
        var viaQuick = PathResolver.Resolve(Current,
            PathRecall.Pick(new RecallItem(PathExpression.Display(saved), saved, true), Env).Insert!, Env);

        Assert.Equal(folder, typed);
        Assert.Equal(folder, viaHistory);
        Assert.Equal(folder, viaQuick);
    }

    [Theory, MemberData(nameof(Folders))]
    public void パンくずと一覧が出す値はジャンプの入口で元のフォルダへ(string folder)
    {
        var target = JumpInput.Decide(Current, PathField.JumpText(folder),
            p => p == folder, _ => false, Env);
        Assert.Equal(new JumpTarget(folder, null), target);
    }

    [Fact]
    public void 使えない保存値は欄に入れず知らせる()
    {
        var legacy = PathRecall.Pick(new RecallItem(@"C:\a%b%c", @"C:\a%b%c", true), Env);
        Assert.Null(legacy.Insert);
        Assert.Equal(@"C:\a%b%c は登録し直してください。", legacy.Message);

        var relative = PathRecall.Pick(new RecallItem("sub", "sub", true), Env);
        Assert.Null(relative.Insert);
        Assert.Equal("sub は使えません。", relative.Message);

        var marked = PathRecall.Pick(new RecallItem(@"%P32_NOSUCH%\x", @"|%P32_NOSUCH%\x", true), Env);
        Assert.Null(marked.Insert);
        Assert.Equal(@"%P32_NOSUCH%\x は使えません。", marked.Message);
    }

    [Fact]
    public void 一覧のクイックアクセスは保存した式で出し_履歴は実パスで出す()
    {
        var items = PathRecall.Items([@"C:\h%1%2"], [@"|%P32_DEST%\x", @"C:\q"]);
        Assert.Equal([@"C:\h%1%2", "", @"%P32_DEST%\x", @"C:\q"], items.Select(i => i.Label));
        Assert.Equal(@"|%P32_DEST%\x", items[2].Value);
        Assert.True(items[2].IsSaved);
        Assert.False(items[0].IsSaved);
        Assert.Equal(["a"], PathRecall.Items(["a"], []).Select(i => i.Label));
    }

    [Fact]
    public void 参照の最初の場所_欄の式は実パスへ()
    {
        Assert.Equal(@"C:\other\x", PathField.BrowseStartFromField(@"%P32_DEST%\x", Current, Env));
        Assert.Equal(@"C:\other\x", PathField.BrowseStartFromField(@"""%P32_DEST%\x""", Current, Env));
        Assert.Equal(@"C:\work\50%\x", PathField.BrowseStartFromField(@"C:\work\50%%\x", Current, Env));
        Assert.Equal(@"C:\work\sub", PathField.BrowseStartFromField("sub", Current, Env));
        // 選んだ結果を式に書いて欄に入れた後、続けて開いても同じ場所
        var field = PathField.Text(@"C:\work\%P32_DEST%");
        Assert.Equal(@"C:\work\%P32_DEST%", PathField.BrowseStartFromField(field, Current, Env));
        Assert.Null(PathField.BrowseStartFromField("", Current, Env));
        Assert.Null(PathField.BrowseStartFromField("  ", Current, Env));
        Assert.Null(PathField.BrowseStartFromField(@"""""", Current, Env));
        Assert.Null(PathField.BrowseStartFromField("a|b", Current, Env));
        Assert.Null(PathField.BrowseStartFromField("%P32_BLANK%", Current, n => n == "P32_BLANK" ? " " : null));   // 空白に置き換わる
    }

    [Fact]
    public void 参照の最初の場所_保存した値は保存した式から実パスへ()
    {
        Assert.Equal(@"C:\other\x", PathField.BrowseStartFromSaved(@"|%P32_DEST%\x", Env));   // 印付き。印が無ければ旧設定の値
        Assert.Equal(@"C:\work\100%", PathField.BrowseStartFromSaved(@"|C:\work\100%%", Env));
        Assert.Null(PathField.BrowseStartFromSaved(@"C:\work\%P32_DEST%\%a%", Env));   // 旧設定の値
        Assert.Null(PathField.BrowseStartFromSaved("sub", Env));                       // 相対
        Assert.Null(PathField.BrowseStartFromSaved("", Env));
    }

    [Fact]
    public void クイックアクセスの登録の欄は_保存した値なら印を外し_実パスなら式に書く()
    {
        var saved = new ReTAC.Domain.Navigation.QuickAccessEntry("t", @"|%USERPROFILE%\x%");
        Assert.Equal(@"%USERPROFILE%\x%", QuickAccessEntryDialog.InitialPathText(saved, Current, presetIsRealPath: false));
        Assert.Equal(@"%%USERPROFILE%%\x%%", QuickAccessEntryDialog.InitialPathText(
            new QuickAccessEntry("", @"%USERPROFILE%\x%"), Current, presetIsRealPath: true));
        Assert.Equal(@"C:\work\a%%b%%", QuickAccessEntryDialog.InitialPathText(null, @"C:\work\a%b%", presetIsRealPath: false));
    }

    [Fact]
    public void 連結先の初期値は式に書く()
    {
        var text = ConcatDialog.InitialDestination(@"C:\50%\a%");
        Assert.Equal(@"C:\50%%\a%%\concat.txt", text);
        Assert.Equal(@"C:\50%\a%\concat.txt", PathResolver.Resolve(Current, text, Env));
    }
}
