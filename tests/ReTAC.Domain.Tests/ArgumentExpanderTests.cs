using ReTAC.Domain.Entries;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

/// <summary>F-02: 引数の展開</summary>
public class ArgumentExpanderTests
{
    private const string Cwd = @"C:\work";

    private static IReadOnlyList<string>? Expand(string template, Entry[] targets, Entry? cursor = null,
                                                string[]? answers = null, Func<string, string>? form = null) =>
        ArgumentExpander.Expand(ArgumentTemplate.Parse(template),
            new MacroContext(targets, cursor ?? targets.FirstOrDefault(), Cwd, answers ?? [], form));

    private static string[] Args(string template, Entry[] targets, Entry? cursor = null,
                                 string[]? answers = null, Func<string, string>? form = null)
    {
        var result = Expand(template, targets, cursor, answers, form);
        Assert.NotNull(result);
        return [.. result];
    }

    private static readonly Entry A = TestEntries.File("a.txt");
    private static readonly Entry B = TestEntries.File("b.txt");
    private static readonly Entry Sub = TestEntries.Folder("sub");
    private static readonly Entry Parent = TestEntries.Parent();

    [Fact]
    public void 固定の引数だけならそのまま()
    {
        Assert.Equal(["/b"], Args("/b", [A]));
        Assert.Empty(Args("", [A]));
    }

    [Fact]
    public void ファイルのパスになる()
    {
        Assert.Equal([@"C:\work\a.txt"], Args("${file}", [A]));
    }

    [Fact]
    public void 複数は表示順に1件ずつ別の引数になり引用符でもつながない()
    {
        Assert.Equal([@"C:\work\a.txt", @"C:\work\b.txt"], Args("${file}", [A, B]));
        Assert.Equal([@"C:\work\a.txt", @"C:\work\b.txt"], Args("\"${file}\"", [A, B]));
    }

    [Fact]
    public void フォルダはfileに入らない()
    {
        Assert.Equal([@"C:\work\a.txt"], Args("${file}", [Sub, A]));
    }

    [Fact]
    public void フォルダ上のfileは空になり書き方で3通りに分かれる()
    {
        Assert.Empty(Args("${file}", [Sub]));             // 取り除く（秀丸エディタは新規ドキュメント）
        Assert.Equal([""], Args("\"${file}\"", [Sub]));    // "" を渡す
        Assert.Null(Expand("${file}!", [Sub]));            // 起動しない（ビューア）
        Assert.Null(Expand("\"${file}!\"", [Sub]));        // 引用符と ! なら ! が勝つ
    }

    [Fact]
    public void 空のマクロを含む引数は前後の文字ごと取り除く()
    {
        Assert.Empty(Args("--out=${file}", [Sub]));
    }

    [Fact]
    public void pathはフォルダを含み親フォルダ項目はカレントフォルダ()
    {
        Assert.Equal([@"C:\work\sub"], Args("${path}", [Sub]));
        Assert.Equal([Cwd], Args("${path}", [Parent]));
    }

    [Fact]
    public void 名前と拡張子はファイルごとに取り出す()
    {
        var a = TestEntries.File("a.avi");
        var b = TestEntries.File("b.avi");
        Assert.Equal(["-i", @"C:\work\a.avi", @"C:\work\b.avi", "a.mp4", "b.mp4"],
            Args("-i ${file} ${fileBasenameNoExtension}.mp4", [a, b]));
        Assert.Equal([".avi"], Args("${fileExtname}", [a]));
    }

    [Fact]
    public void カーソルのマクロはマークに関係なくカーソルの1件()
    {
        var c = TestEntries.File("c.txt");
        Assert.Equal([@"C:\work\c.txt"], Args("${cursorFile}", [A, B], cursor: c));
        Assert.Empty(Args("${cursorFile}", [A], cursor: Sub));
        Assert.Equal([@"C:\work\sub"], Args("${cursorPath}", [A], cursor: Sub));
        Assert.Equal([Cwd], Args("${cursorPath}", [A], cursor: Parent));
    }

    [Fact]
    public void cwdはカレントフォルダ()
    {
        Assert.Equal(["-path", Cwd], Args("-path ${cwd}", [A]));
    }

    [Fact]
    public void 入力した文字列は空白を含んでも1つの引数()
    {
        Assert.Equal(["commit", "-m", "直した 箇所"], Args("commit -m ${prompt:メッセージ}", [A], answers: ["直した 箇所"]));
    }

    [Fact]
    public void 入力が空のときも書き方で3通りに分かれる()
    {
        Assert.Equal(["-search", ""], Args("-search \"${prompt}\"", [A], answers: [""]));
        Assert.Equal(["-search"], Args("-search ${prompt}", [A], answers: [""]));
        Assert.Null(Expand("switch ${prompt}!", [A], answers: [""]));
    }

    [Fact]
    public void 対象が0件ならfileは空()
    {
        Assert.Null(Expand("${file}!", []));
        Assert.Equal(["x"], Args("x ${file}", []));
    }

    [Fact]
    public void パスの値にだけ変換を掛ける()
    {
        var form = (string path) => "S:" + path;
        Assert.Equal([@"S:C:\work\a.txt", "a", "S:" + Cwd], Args("${file} ${fileBasenameNoExtension} ${cwd}", [A], form: form));
    }

    [Fact]
    public void 全角空白を含むパスも1つの引数()
    {
        var file = TestEntries.File("a　b.txt");
        Assert.Equal([@"C:\work\a　b.txt"], Args("${file}", [file]));
    }
}
