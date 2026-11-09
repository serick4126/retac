using System.Text.Json;
using ReTAC.App;
using ReTAC.Domain.Entries;
using ReTAC.Domain.Navigation;
using ReTAC.Domain.Tools;
using ReTAC.Shell;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-147 / S8: 宛先の文字の置き換えが、本物の OS のコピーで誤った宛先に当たらないこと。
/// 毎回新しく作った作業場所（Phase32Workspace。消さない）の中に、正しい宛先（名前に %P32_…% を含むフォルダ）と、
/// 誤って読んだときに当たる宛先（その変数の値のフォルダ。式として読むと正しい宛先の隣になる。見張りのファイルと空のフォルダを置く）を作る。
/// 環境変数は引数で渡し、プロセスの環境は書き換えない。ShellFileOperation 自体は変数を置き換えない（ReTAC が決めた実パスを渡す）。
/// 正しい宛先に届いたこと（陽性対照）を確かめないまま合格にしない。
/// </summary>
public class RealOsTransferTests
{
    private sealed record Scene(Phase32Workspace Ws, string Cwd, string Wrong, string Correct, string Source,
        Func<string, string?> Env, Dictionary<string, string> Before);

    /// <summary>名前に変数の形を含む正しい宛先と、その変数が指す誤った宛先を作る。フォルダ名は変数の名前そのまま</summary>
    private static Scene Arrange(string variable)
    {
        var ws = Phase32Workspace.Create();
        var wrong = ws.CreateFolder("area", "wrong");
        ws.CreateFile("original", "area", "wrong", "sentinel.txt");
        ws.CreateFolder("area", "wrong", "empty");
        var correct = ws.CreateFolder("area", $"%{variable}%");
        var source = ws.CreateFile("payload", "src", "payload.txt");
        string? Env(string name) => name.Equals(variable, StringComparison.OrdinalIgnoreCase) ? "wrong" : null;   // 相対の値。式として読むと area\wrong に当たる
        // 見張るのは作業場所の全体。正しい宛先の中だけが変わってよい
        return new Scene(ws, Path.Combine(ws.Root, "area"), wrong, correct, source, Env, TreeWatch.Snapshot([ws.Root]));
    }

    /// <summary>
    /// 決まった宛先が null（使えない）ならコピーの要求を作らない。作ったら true。
    /// コピーは本物の OS で行う（IFileOperation は STA）。宛先は実パスなので、ここで変数を置き換えない。
    /// </summary>
    private static bool CopyTo(string? destination, string source)
    {
        if (destination is null) return false;
        Sta.Run(() =>
        {
            using var operation = new ShellFileOperation(IntPtr.Zero, silentOverwrite: false, noUi: true);
            operation.Copy(source, destination);
            operation.Execute();
        });
        return true;
    }

    /// <summary>誤った宛先の中身・属性が変わっておらず、正しい宛先にだけコピーされている</summary>
    private static void AssertOnlyCorrectChanged(Scene s)
    {
        Assert.Equal("PASS", TreeWatch.Compare(s.Before, TreeWatch.Snapshot([s.Ws.Root]), s.Correct).ToString());
        Assert.Equal("original", File.ReadAllText(Path.Combine(s.Wrong, "sentinel.txt")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(s.Wrong, "empty")));
        // 陽性対照: 正しい宛先に届いている（何も起きなかったから合格、を防ぐ）
        Assert.Equal("payload", File.ReadAllText(Path.Combine(s.Correct, "payload.txt")));
    }

    [Fact]
    public void コピーの宛先_欄の文字から実パスにして_正しい宛先にだけコピーされる()
    {
        var s = Arrange("P32_DEST");
        var text = PathField.Text(s.Correct);   // 欄へ実パスを入れる形
        Assert.True(CopyTo(PathResolver.Resolve(s.Cwd, text, s.Env), s.Source));
        AssertOnlyCorrectChanged(s);
    }

    [Fact]
    public void ブックマークへのドロップ_実パスから式に書いて_欄を通して_正しい宛先にだけコピーされる()
    {
        var s = Arrange("P32_DEST");
        var bookmark = BookmarkPath.ForRealPath(BookmarkKind.Folder, s.Correct);
        var real = BookmarkPath.Resolve(bookmark, s.Env);
        Assert.Equal(s.Correct, real);
        // ドロップの宛先は実パスなので、欄の文字（式）に書いてから確定の入口で解決する
        Assert.True(CopyTo(PathResolver.Resolve(s.Cwd, PathField.Text(real!), s.Env), s.Source));
        AssertOnlyCorrectChanged(s);
    }

    [Fact]
    public void 保存して読み直したブックマーク_保存した式から実パスにして_正しい宛先にだけコピーされる()
    {
        var s = Arrange("P32_DEST");
        var saved = JsonSerializer.Serialize(new List<Bookmark> { BookmarkPath.ForRealPath(BookmarkKind.Folder, s.Correct) });
        var read = JsonSerializer.Deserialize<List<Bookmark>>(saved)!.Single();
        Assert.StartsWith("|", read.Target);   // 設定ファイルの印
        Assert.True(CopyTo(BookmarkPath.Resolve(read, s.Env), s.Source));
        AssertOnlyCorrectChanged(s);
    }

    [Fact]
    public void 旧設定のブックマーク_実パスは作られず_コピーの要求も作られない()
    {
        var s = Arrange("P32_DEST");
        var legacy = new Bookmark("", BookmarkKind.Folder, s.Correct);   // 印の無い、% を 2 つ含む旧設定の値
        // 陽性対照: 式として読めば誤った宛先に当たる値である（このテストが意味を持つ条件）
        var asExpression = PathExpression.Expand(s.Correct, s.Env);
        Assert.Equal(s.Wrong, asExpression, ignoreCase: true);
        var real = BookmarkPath.Resolve(legacy, s.Env);
        Assert.Null(real);
        Assert.False(CopyTo(real, s.Source));
        Assert.Equal("PASS", TreeWatch.Compare(s.Before, TreeWatch.Snapshot([s.Ws.Root]), s.Correct).ToString());
        Assert.Empty(Directory.EnumerateFileSystemEntries(s.Correct));   // 正しい宛先にも何も届かない
    }

    [Fact]
    public void 初期値のマクロを連結した宛先_部品を分けたまま式に書いて_正しい宛先にだけコピーされる()
    {
        // フォルダ名 %P32.dest% は ${fileBasenameNoExtension} = %P32 と ${fileExtname} = .dest% に分かれる。部品ごとに書いてつなぐと変数になる
        var s = Arrange("P32.dest");
        var name = "%P32.dest%";
        var file = Entry.ForFile(Path.Combine(s.Cwd, name), name, 0, 0, default);
        var context = new MacroContext([file], file, s.Cwd);
        var definition = new PromptDefinition
        {
            Items = [new PromptItem { Id = 1, Kind = PromptItemKind.Folder, Label = "宛先", Initial = @"${cwd}\${fileBasenameNoExtension}${fileExtname}" }],
            Arguments = [PromptArgument.Item(1)],
        };
        var initial = PromptAnswerRules.Initial(definition, context, s.Env);
        Assert.Null(initial.Error);
        var check = PromptAnswerRules.Check(definition, initial.Values!, s.Cwd, s.Env);
        Assert.Null(check.Error);
        Assert.True(CopyTo(check.Values![1].Text, s.Source));
        AssertOnlyCorrectChanged(s);
    }
}
