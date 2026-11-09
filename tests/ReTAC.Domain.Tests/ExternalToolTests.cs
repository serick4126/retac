using ReTAC.App;
using ReTAC.Domain.Navigation;
using ReTAC.Domain.Selection;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

/// <summary>プロパティ表示（`R`）で開く対象の決め方（R-68 / R-56-2 / R-56-3）。外部ツールは LaunchPlannerTests</summary>
public class ExternalToolTests
{
    [Fact]
    public void マークが複数あれば実効対象すべてを開く()
    {
        var state = new ListState([TestEntries.Parent(), TestEntries.File("a.txt"), TestEntries.File("b.txt"), TestEntries.File("c.txt")]);
        state.ToggleMark(1);
        state.ToggleMark(2);
        state.MoveCursor(3);

        Assert.Equal(["a.txt", "b.txt"], TestEntries.Names(LaunchTargets.For(state)));
    }

    [Fact]
    public void マークが無ければカーソルの1件()
    {
        var state = new ListState([TestEntries.Parent(), TestEntries.File("a.txt"), TestEntries.File("b.txt")]);
        state.MoveCursor(2);

        Assert.Equal(["b.txt"], TestEntries.Names(LaunchTargets.For(state)));
    }

    [Fact]
    public void カーソルが親フォルダ項目なら対象は空()
    {
        var state = new ListState([TestEntries.Parent(), TestEntries.File("a.txt")]);

        Assert.Empty(LaunchTargets.For(state));
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    public void 十件以上で事前確認が要る(int count, bool expected)
    {
        Assert.Equal(expected, LaunchTargets.NeedsConfirmation(count));
    }

    [Theory]
    [InlineData("\"C:\\tools\\my tool.exe\"", @"C:\tools\my tool.exe")]   // R-127: 「パスのコピー」の形
    [InlineData("  \"notepad.exe\"  ", "notepad.exe")]
    [InlineData(@"C:\tools\a.exe", @"C:\tools\a.exe")]
    [InlineData("  notepad.exe ", "notepad.exe")]
    [InlineData("", "")]
    [InlineData("\"\u3000tool.exe\"", "\u3000tool.exe")]   // B-01: 全角空白は名前の一部
    [InlineData("C:\\t\\tool\u3000", "C:\\t\\tool\u3000")]
    public void 外部ツールの実行ファイルの欄は_囲みの引用符を外して保存する(string input, string expected) =>
        Assert.Equal(expected, ExternalToolPage.NormalizePath(input));

    // --- R-147: 実行ファイルのパスは設定ファイルの形の式 ---

    private static Func<string, string?> Env(params (string Name, string Value)[] vars) =>
        name => vars.Where(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(v => v.Value).FirstOrDefault();

    private static LaunchRequest Request(string storedPath, bool keepOpen = false) =>
        new(new ExternalTool { Id = 1, Name = "t", Path = storedPath, KeepWindowOpen = keepOpen }, [], @"C:\w", "x");

    [Fact]
    public void 初期登録の3件は式として読んでも起動できる()
    {
        foreach (var tool in DefaultExternalTools.Create())
        {
            var info = ToolProcess.StartInfo(Request(tool.Path), Env());
            Assert.NotNull(info);
            Assert.True(File.Exists(info!.FileName), $"{tool.Path} → {info.FileName}");
        }
    }

    [Fact]
    public void 名前だけに置き換わる変数はnotepadと同じ探し方で解決する()
    {
        var viaVariable = ToolProcess.StartInfo(Request(PathExpression.Store("%P32_EDITOR%")), Env(("P32_EDITOR", "notepad.exe")));
        var direct = ToolProcess.StartInfo(Request("notepad.exe"), Env());
        Assert.Equal(direct!.FileName, viaVariable!.FileName);
        Assert.True(Path.IsPathFullyQualified(viaVariable.FileName));
    }

    [Fact]
    public void 絶対パスに置き換わる変数は置き換えた文字を探し方へ渡す()
    {
        var asked = new List<string>();
        var result = ToolPath.Resolve(PathExpression.Store(@"%P32_DIR%\x.exe"), Env(("P32_DIR", @"C:\tools")), s => { asked.Add(s); return null; });
        Assert.False(result.Legacy);
        Assert.Equal([@"C:\tools\x.exe"], asked);
        Assert.Equal(@"C:\tools\x.exe", result.LaunchPath);   // 見つからなければ書かれたまま Windows に任せる
    }

    [Fact]
    public void 相対の指定は今までどおり探し方へそのまま渡す()
    {
        var asked = new List<string>();
        ToolPath.Resolve(@"tools\a.exe", Env(), s => { asked.Add(s); return null; });
        Assert.Equal([@"tools\a.exe"], asked);
    }

    [Fact]
    public void 旧設定の値は探しも起動もしない()
    {
        const string legacy = @"C:\%P32_A%\%P32_B%\x.exe";   // 印が無く % が 2 つ以上
        var asked = false;
        Assert.True(ToolPath.Resolve(legacy, Env(("P32_A", "a"), ("P32_B", "b")), _ => { asked = true; return "x"; }).Legacy);
        Assert.False(asked);
        Assert.Null(ToolProcess.StartInfo(Request(legacy), Env(("P32_A", "a"), ("P32_B", "b"))));
        Assert.Equal(@"C:\%P32_A%\%P32_B%\x.exe を登録し直してください。", ToolPath.LegacyNotice(legacy));
    }

    [Fact]
    public void コンソール保持の起動も置き換えた探し方の結果を渡す()
    {
        var info = ToolProcess.StartInfo(Request(PathExpression.Store("%P32_EDITOR%"), keepOpen: true), Env(("P32_EDITOR", "cmd.exe")));
        Assert.Contains(info!.ArgumentList, a => a.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase) && Path.IsPathFullyQualified(a));
    }

    [Fact]
    public void 設定画面の存在の確認も起動と同じ関数を通る()
    {
        var env = Env(("P32_EDITOR", "notepad.exe"));
        var check = ExternalToolPage.CheckPath(PathExpression.Store("%P32_EDITOR%"), env);
        Assert.True(check.Found);
        Assert.False(check.Script);
        Assert.False(ExternalToolPage.CheckPath(PathExpression.Store("%P32_EDITOR%"), Env(("P32_EDITOR", "no-such-tool-retac.exe"))).Found);
        // 旧設定の値は見つからない扱いにしない（登録し直しを促す別の知らせ）
        Assert.True(ExternalToolPage.CheckPath(@"C:\%A%\%B%\x.exe", env).Legacy);
    }

    [Fact]
    public void アイコンのパスも置き換えた探し方の結果_旧設定の値は無し()
    {
        var env = Env(("P32_EDITOR", "notepad.exe"));
        var viaVariable = ToolPath.IconPath(PathExpression.Store("%P32_EDITOR%"), env);
        Assert.Equal(ToolPath.IconPath("notepad.exe", Env()), viaVariable);
        Assert.True(Path.IsPathFullyQualified(viaVariable!));
        Assert.Null(ToolPath.IconPath(@"C:\%A%\%B%\x.exe", env));
        Assert.Equal("", ToolPath.IconPath("", env));
    }

    /// <summary>ExecutableResolver.Resolve( を呼んでよいのは、保存した値を式として読む ToolPath と、実パス・名前を受ける ShellIcons だけ。</summary>
    private static readonly string[] ResolverAllowList = ["ToolPath.cs", "ShellIcons.cs"];

    internal static IEnumerable<string> ResolverCallers(IEnumerable<(string Name, string Text)> sources) =>
        sources.Where(s => s.Text.Contains("ExecutableResolver.Resolve(") && !ResolverAllowList.Contains(s.Name)).Select(s => s.Name);

    [Fact]
    public void 実行ファイルの探し方を直接呼ぶのは許可した所だけ()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "ReTAC.App"))) dir = Path.GetDirectoryName(dir);
        var sources = new[] { "ReTAC.App", "ReTAC.Shell", "ReTAC.Domain" }
            .SelectMany(p => Directory.EnumerateFiles(Path.Combine(dir!, "src", p), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains(@"\obj\") && !f.Contains(@"\bin\") && Path.GetFileName(f) != "Executables.cs")
            .Select(f => (Path.GetFileName(f), File.ReadAllText(f))).ToList();
        Assert.Empty(ResolverCallers(sources));
        // 起動と設定画面は、置き換える入口を必ず通る
        foreach (var name in new[] { "ToolProcess.cs", "ExternalToolPage.cs" })
            Assert.Contains("ToolPath.Resolve(", sources.Single(s => s.Item1 == name).Item2);
    }

    [Fact]
    public void 探し方の直接呼び出しの検査は違反を捕まえる()
    {
        Assert.Equal(["Bad.cs"], ResolverCallers([("Bad.cs", "var p = ExecutableResolver.Resolve(tool.Path);"), ("ToolPath.cs", "ExecutableResolver.Resolve(x)"), ("Ok.cs", "ToolPath.Resolve(x)")]));
    }

    [Fact]
    public void 欄を書き換えていなければ保存した値を一字も変えない()
    {
        var env = Env(("P32_A", "a"));
        const string legacy = @"C:\%P32_A%\%P32_B%\x.exe";
        Assert.Equal(legacy, ExternalToolPage.StoredFromField(legacy, PathExpression.Display(legacy), legacy, env));
        var marked = PathExpression.Store("%P32_A%.exe");
        Assert.Equal(marked, ExternalToolPage.StoredFromField(PathExpression.Display(marked), PathExpression.Display(marked), marked, env));
        // 書き換えたら、置き換えの前の式を正規の形にして印を付ける
        Assert.Equal("|%P32_A%.exe", ExternalToolPage.StoredFromField("%P32_A%.exe", "other", "other", env));
        Assert.Equal(@"C:\Tools\x.exe", ExternalToolPage.StoredFromField("\"C:\\Tools\\x.exe\"", "", "", env));
        // 旧設定の値を直して OK したときは新しい式になる
        Assert.Equal(@"|C:\%P32_A%\%P32_C%\x.exe",
            ExternalToolPage.StoredFromField(@"C:\%P32_A%\%P32_C%\x.exe", PathExpression.Display(legacy), legacy, Env(("P32_A", "a"), ("P32_C", "c"))));
    }

    [Fact]
    public void 参照で選んだ実パスは文字の値として欄へ入れる_往復で変わらない()
    {
        const string chosen = @"C:\p%P32_A%x\a%.exe";
        var field = ExternalToolPage.FieldTextFor(chosen);
        Assert.Equal(chosen, PathExpression.Expand(field, Env(("P32_A", "ZZ"))));
        Assert.Equal(chosen, ToolPath.Resolve(ExternalToolPage.StoredFromField(field, "", "", Env(("P32_A", "ZZ"))), Env(("P32_A", "ZZ")), s => null).Expanded);
    }

    [Fact]
    public void 参照の最初の場所は置き換えた文字で_旧設定の値なら指定しない()
    {
        Assert.Equal(@"C:\tools\x.exe", ExternalToolPage.BrowseStart(PathExpression.Store(@"%P32_DIR%\x.exe"), Env(("P32_DIR", @"C:\tools"))));
        Assert.Equal("notepad.exe", ExternalToolPage.BrowseStart("notepad.exe", Env()));
        Assert.Null(ExternalToolPage.BrowseStart(@"C:\%A%\%B%\x.exe", Env()));
    }

    [Fact]
    public void 表示は印を外した式で出す()
    {
        var stored = PathExpression.Store(@"%P32_DIR%\x.exe");
        var request = Request(stored);
        Assert.Equal(@"%P32_DIR%\x.exe", request.DisplayCommandLine());
    }
}
