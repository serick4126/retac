using ReTAC.Domain.Entries;
using ReTAC.Domain.Selection;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

/// <summary>F-01 の初期登録と、F-03 / F-09 の起動の計画</summary>
public class LaunchPlannerTests
{
    private const string Cwd = @"C:\work";

    private static ExternalTool Tool(string arguments, bool perItem = false) =>
        new() { Id = 7, Name = "テスト", Path = "tool.exe", Arguments = arguments, LaunchPerItem = perItem };

    private static IReadOnlyList<LaunchRequest> Plan(ExternalTool tool, Entry[] targets, bool suppress = false) =>
        LaunchPlanner.Plan(tool, ArgumentTemplate.Parse(tool.Arguments), targets, targets.FirstOrDefault(), Cwd, [], suppress);

    [Fact]
    public void 初期登録はOS標準の3件()
    {
        var tools = DefaultExternalTools.Create();

        Assert.Equal(["テキスト エディタ", "テキスト ビューア", "ターミナル"], tools.Select(t => t.Name));
        Assert.Equal(["notepad.exe", "notepad.exe", "cmd.exe"], tools.Select(t => t.Path));
        Assert.Equal(["${file}", "${file}!", ""], tools.Select(t => t.Arguments));
        Assert.Equal([DefaultExternalTools.EditorId, DefaultExternalTools.ViewerId, DefaultExternalTools.TerminalId], tools.Select(t => t.Id));
        Assert.All(tools, t => Assert.True(t.ShowInPopup));
        Assert.True(DefaultExternalTools.FirstFreeId > tools.Max(t => t.Id));
    }

    [Fact]
    public void 初期登録に環境固有のパスを埋めない()
    {
        // B-05: 実行ファイル名だけ。ディレクトリを含むパスは、その製品を入れた環境でしか通らない
        Assert.All(DefaultExternalTools.Create(), t =>
        {
            Assert.DoesNotContain('\\', t.Path);
            Assert.DoesNotContain('/', t.Path);
            Assert.DoesNotContain(':', t.Path);
        });
    }

    [Fact]
    public void 既定のエディタは対象が無くても開きビューアは起動しない()
    {
        // F-02 / B-05: 初回の利用者が ${file} と ${file}! の差に触れて、! の意味が分かるようにしてある
        var tools = DefaultExternalTools.Create();
        var editor = tools.Single(t => t.Id == DefaultExternalTools.EditorId);
        var viewer = tools.Single(t => t.Id == DefaultExternalTools.ViewerId);

        Assert.Single(Plan(editor, []));      // 引数なしで notepad が開く
        Assert.Empty(Plan(viewer, []));       // 必須マクロが空なので起動しない
        Assert.Empty(Plan(editor, []).Single().Arguments);
    }

    [Fact]
    public void 全体の設定がONならマークがあってもカーソルの1件()
    {
        var state = new ListState([TestEntries.Parent(), TestEntries.File("a.txt"), TestEntries.File("b.txt")]);
        state.ToggleMark(1);
        state.MoveCursor(2);

        Assert.Equal(["b.txt"], TestEntries.Names(LaunchPlanner.TargetsFor(state, suppressMultiple: true)));
        Assert.Equal(["a.txt"], TestEntries.Names(LaunchPlanner.TargetsFor(state, suppressMultiple: false)));
    }

    [Fact]
    public void マークが無くカーソルが親フォルダ項目ならその項目を渡す()
    {
        // ${path} をカレントフォルダにするため。プロパティ表示の LaunchTargets.For とはここが違う
        var state = new ListState([TestEntries.Parent(), TestEntries.File("a.txt")]);
        Assert.Equal([".."], TestEntries.Names(LaunchPlanner.TargetsFor(state, suppressMultiple: false)));
    }

    [Fact]
    public void まとめて起動するなら1回()
    {
        var request = Assert.Single(Plan(Tool("${file}"), [TestEntries.File("a.txt"), TestEntries.File("b.txt")]));
        Assert.Equal([@"C:\work\a.txt", @"C:\work\b.txt"], request.Arguments);
        Assert.Equal(Cwd, request.WorkingDirectory);
    }

    [Fact]
    public void 項目ごとに起動するなら1件ずつ()
    {
        var requests = Plan(Tool("${file}", perItem: true), [TestEntries.File("a.txt"), TestEntries.File("b.txt")]);
        Assert.Equal(["a.txt", "b.txt"], requests.Select(r => r.TargetLabel));
        Assert.Equal([@"C:\work\b.txt"], requests[1].Arguments);
    }

    [Fact]
    public void 全体の設定がONなら項目ごとの指定は効かない()
    {
        Assert.False(LaunchPlanner.RunsPerItem(Tool("", perItem: true), suppressMultiple: true));
        Assert.Single(Plan(Tool("${file}", perItem: true), [TestEntries.File("a.txt"), TestEntries.File("b.txt")], suppress: true));
    }

    [Fact]
    public void 項目ごとの起動で起動しない項目は抜ける()
    {
        var requests = Plan(Tool("${file}!", perItem: true), [TestEntries.Folder("sub"), TestEntries.File("a.txt")]);
        Assert.Equal(["a.txt"], requests.Select(r => r.TargetLabel));
    }

    [Fact]
    public void 引数の無いツールは対象が無くても起動する()
    {
        var request = Assert.Single(Plan(Tool(""), []));
        Assert.Empty(request.Arguments);
    }

    [Theory]
    [InlineData(true, 9, false)]
    [InlineData(true, 10, true)]
    [InlineData(false, 50, false)]   // まとめて 1 回なら何件でも確認しない
    public void 実際に10回以上起動するときだけ確認する(bool perItem, int count, bool expected)
    {
        Assert.Equal(expected, LaunchPlanner.NeedsManyConfirmation(Tool("", perItem), suppressMultiple: false, count));
    }

    [Fact]
    public void 確認に出すコマンドラインは空白を含む引数と空の引数を囲む()
    {
        var request = new LaunchRequest(Tool("") with { Path = @"C:\Program Files\x.exe" }, ["-m", "a b", ""], Cwd, "a");
        Assert.Equal("\"C:\\Program Files\\x.exe\" -m \"a b\" \"\"", request.DisplayCommandLine());
    }
}
