using System.Text.RegularExpressions;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-147 / S8: 源の検査（書き方の検査。すべての漏れを捕まえるものではない）。実パスを式に書かずに欄・イベント・参照・保存の型へ入れる書き方と、
/// 実 OS のテストが消す・属性を変える部品を使う書き方を、源の文字から見つけて失敗にする。
/// それぞれ、検出したい書き方を入れた例の文字で検出されること（陰性例）も確かめる。
/// </summary>
public class PathExpressionSourceTests
{
    private const string Real = @"selected|folder|_folder|[\w.]*FileName|_currentFolder|target";

    // 1. 欄の Text へ、実パスらしい値を直接代入する（PathField.Text を通さない）
    private static readonly Regex FieldAssign = new(
        @"\b(?:_input|_path|_destination|box|input)\.Text\s*=\s*(?:selected\b|folder\b|_folder\b|[\w.]*FileName\b|(?:System\.IO\.)?Path\.Combine\()");

    // 2. JumpRequested の引数が、欄の文字でも PathField.JumpText でもない（パンくずの実パスをそのまま出す）
    private static readonly Regex JumpArgument = new(
        @"JumpRequested\?\.Invoke\(\s*[\w.]+\s*,\s*\(\s*(?!_input\.Text\b|PathField\.JumpText\(|PathExpression\.Literal\()");

    // 3. 参照（フォルダ・ファイル）の最初の場所に、欄の Text・保存の値をそのまま渡す
    private const string RawPathValue = @"(?:\w\.Text\b|InputText\.Unquote\(|\.Target\b|\bentry\.Path\b|\btool\.Path\b)";
    private static readonly Regex[] BrowseStart =
    [
        new(@"FolderBrowser\.Select\([^;{}]*?" + RawPathValue),
        new(@"OpenFileDialog\s*\{[^}]*?\b(?:FileName|InitialDirectory)\s*=\s*[^,}]*?" + RawPathValue),
        new(@"\b\w+\.(?:FileName|InitialDirectory)\s*=\s*[^;]*?" + RawPathValue),
    ];

    // 4. 保存の型の生成と with で、式に書かない値を Target・Path・Initial・LastText へ入れる
    private static readonly Regex[] StoreValue =
    [
        new(@"(?:new\s+(?:Bookmark|QuickAccessEntry|ExternalTool|PromptItem)\b|\bwith\s*\{)[^;]*?\b(?:Target|Path|Initial|LastText)\s*=\s*(?:" + Real + @")\b(?![.(])"),
        new(@"new\s+(?:Bookmark|QuickAccessEntry)\s*\([^;()]*?,\s*(?:" + Real + @")\s*[,)]"),
    ];

    // 5. 保存した値（式）をファイル・プロセス・シェルの関数へ直接渡す（作業 5〜7 の部品を通す）
    private static readonly Regex[] StoredToApi =
    [
        new(@"\b(?:File|Directory)\.\w+\(\s*[\w.]*\.Target\b"),
        new(@"\b(?:File|Directory)\.\w+\(\s*(?:entry|tool|item|q)\.Path\b"),
        new(@"\bProcess\.Start\(\s*(?:new\s+ProcessStartInfo\(\s*)?[\w.]*\.(?:Target|Path)\s*[,)]"),
        new(@"\bShell\w*\.\w+\(\s*[\w.]*\.Target\b"),
    ];

    /// <summary>行の終わりのコメントを外す（コメントの中の例を数えない）</summary>
    private static string StripComments(string text) =>
        Regex.Replace(text, @"(?<!:)//[^\r\n]*", "");

    private static IEnumerable<string> Hits(IEnumerable<Regex> rules, string text)
    {
        var code = StripComments(text);
        return rules.SelectMany(r => r.Matches(code).Select(m => m.Value));
    }

    private static List<(string Name, string Text)> AppSources()
    {
        var root = Path.Combine(SchemaManifest.RepoRoot(), "src", "ReTAC.App");
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(@"\obj\") && !f.Contains(@"\bin\"))
            .Select(f => (Path.GetFileName(f), File.ReadAllText(f))).ToList();
    }

    /// <summary>検査の例外（「ファイル名: 検出した文字」→ 理由）。足すときは理由を必ず書く</summary>
    private static readonly Dictionary<string, string> Allowed = new()
    {
        // 現在のフォルダ・カーソルのフォルダ（実パス）を登録のダイアログの最初の値にする。presetIsRealPath: true を渡し、ダイアログが PathField.Text で式に書いて欄へ入れる
        ["MainForm.cs: new QuickAccessEntry(\"\", target)"] = "presetIsRealPath: true",
    };

    private static List<string> Violations(IEnumerable<Regex> rules) =>
        AppSources().SelectMany(s => Hits(rules, s.Text).Select(h => $"{s.Name}: {h}")).Where(v => !Allowed.ContainsKey(v)).ToList();

    [Fact]
    public void 源を読めている() => Assert.Contains(AppSources(), s => s.Name == "AddressBar.cs" && s.Text.Contains("PathField.Text("));

    // ---- 1 ----

    [Fact]
    public void 欄のTextへ実パスを直接代入しない() => Assert.Empty(Violations([FieldAssign]));

    [Theory]
    [InlineData("_input.Text = selected;")]
    [InlineData("_path.Text = folder;")]
    [InlineData("_destination.Text = _folder;")]
    [InlineData("box.Text = open.FileName;")]
    [InlineData("input.Text = Path.Combine(cwd, name);")]
    public void 欄への直接代入の検査は違反を捕まえる(string line) => Assert.NotEmpty(Hits([FieldAssign], line));

    [Theory]
    [InlineData("_input.Text = PathField.Text(selected);")]
    [InlineData("_path.Text = PathField.Text(open.FileName);")]
    [InlineData("_input.Text = \"\";")]
    public void 欄への代入の検査は正しい書き方を通す(string line) => Assert.Empty(Hits([FieldAssign], line));

    // ---- 2 ----

    [Fact]
    public void ジャンプの引数は欄の文字か式に書いた値() => Assert.Empty(Violations([JumpArgument]));

    [Theory]
    [InlineData("JumpRequested?.Invoke(this, (_segments[i].Path, false));")]
    [InlineData("JumpRequested?.Invoke(this, (path, false));")]
    public void ジャンプの引数の検査は違反を捕まえる(string line) => Assert.NotEmpty(Hits([JumpArgument], line));

    [Theory]
    [InlineData("JumpRequested?.Invoke(this, (_input.Text, false));")]
    [InlineData("JumpRequested?.Invoke(this, (PathField.JumpText(path), false));")]
    public void ジャンプの引数の検査は正しい書き方を通す(string line) => Assert.Empty(Hits([JumpArgument], line));

    // ---- 3 ----

    [Fact]
    public void 参照の最初の場所に欄の文字や保存の値をそのまま渡さない() => Assert.Empty(Violations(BrowseStart));

    [Theory]
    [InlineData("FolderBrowser.Select(this, _path.Text, _currentFolder)")]
    [InlineData("FolderBrowser.Select(this, InputText.Unquote(_input.Text), cwd)")]
    [InlineData("FolderBrowser.Select(this, bookmark.Target, cwd)")]
    [InlineData("FolderBrowser.Select(this, entry.Path, cwd)")]
    [InlineData("FolderBrowser.Select(this, tool.Path, cwd)")]
    [InlineData("new OpenFileDialog { FileName = _path.Text }")]
    [InlineData("new OpenFileDialog { InitialDirectory = entry.Path, CheckFileExists = true }")]
    [InlineData("dialog.FileName = InputText.Unquote(_path.Text);")]
    [InlineData("open.InitialDirectory = tool.Path;")]
    public void 参照の検査は違反を捕まえる(string text) => Assert.NotEmpty(Hits(BrowseStart, text));

    [Theory]
    [InlineData("FolderBrowser.Select(this, start, _currentFolder)")]
    [InlineData("new OpenFileDialog { InitialDirectory = _currentFolder, CheckFileExists = true }")]
    [InlineData("dialog.FileName = start;")]
    public void 参照の検査は正しい書き方を通す(string text) => Assert.Empty(Hits(BrowseStart, text));

    // ---- 4 ----

    [Fact]
    public void 保存の型へ式に書かない値を入れない() => Assert.Empty(Violations(StoreValue));

    [Theory]
    [InlineData("new Bookmark(\"\", BookmarkKind.Folder, selected)")]
    [InlineData("new Bookmark(\"\", BookmarkKind.File, open.FileName)")]
    [InlineData("new QuickAccessEntry(name, kind, _currentFolder)")]
    [InlineData("new QuickAccessEntry { Path = folder }")]
    [InlineData("new ExternalTool { Path = open.FileName }")]
    [InlineData("new PromptItem { Initial = target }")]
    [InlineData("item with { Initial = _currentFolder }")]
    [InlineData("b with { Target = selected, Title = x }")]
    [InlineData("p with {\n    LastText = folder,\n}")]
    public void 保存の型の検査は違反を捕まえる(string text) => Assert.NotEmpty(Hits(StoreValue, text));

    [Theory]
    [InlineData("new Bookmark(\"\", BookmarkKind.Folder, BookmarkPath.Store(selected))")]
    [InlineData("new ExternalTool { Path = PathExpression.Store(PathExpression.Literal(folder)) }")]
    [InlineData("item with { Initial = stored }")]
    [InlineData("new Bookmark(title, kind, b.Target)")]
    public void 保存の型の検査は正しい書き方を通す(string text) => Assert.Empty(Hits(StoreValue, text));

    // ---- 5 ----

    [Fact]
    public void 保存した値をファイルやプロセスの関数へ直接渡さない() => Assert.Empty(Violations(StoredToApi));

    [Theory]
    [InlineData("File.Exists(bookmark.Target)")]
    [InlineData("Directory.Exists(b.Target)")]
    [InlineData("File.Exists(entry.Path)")]
    [InlineData("Directory.EnumerateFiles(tool.Path)")]
    [InlineData("Process.Start(tool.Path)")]
    [InlineData("Process.Start(new ProcessStartInfo(b.Target))")]
    [InlineData("ShellItem.Open(bookmark.Target)")]
    public void 保存した値の直接の使用の検査は違反を捕まえる(string text) => Assert.NotEmpty(Hits(StoredToApi, text));

    [Theory]
    [InlineData("File.Exists(BookmarkPath.Resolve(b, env))")]
    [InlineData("Directory.Exists(real)")]
    [InlineData("Process.Start(ToolPath.Resolve(tool.Path, env).LaunchPath)")]
    public void 保存した値の直接の使用の検査は正しい書き方を通す(string text) => Assert.Empty(Hits(StoredToApi, text));

    // ---- 6 ----

    /// <summary>
    /// 実 OS のテストのクラスと、呼び出す作業場所・見張りの部品。Sta.cs は WithTempFolder を持つので読まず、呼び出しだけを下で確かめる
    /// （Sta.Run は消さない）。シェルの操作（ShellFileOperation）は src の側で DestructiveApiTests が数える。
    /// </summary>
    private static readonly string[] RealOsFiles =
    [
        "tests/ReTAC.Domain.Tests/Probe/Phase32Workspace.cs",
        "tests/ReTAC.Domain.Tests/Probe/TreeWatch.cs",
        "tests/ReTAC.Domain.Tests/Phase32WorkspaceTests.cs",
        "tests/ReTAC.Domain.Tests/RealOsTransferTests.cs",
    ];

    /// <summary>実 OS のテストで使ってよい、消す・移す・上書きするのに見える呼び出しは、コピーの 1 か所だけ</summary>
    private const string TheCopy = "operation.Copy(";

    private static readonly Regex[] Forbidden =
    [
        new(@"Sta\.WithTempFolder"),
        new(@"\bSetAttributes\b|\bFileInfo\b[^;]*\.Attributes\s*=|\.Attributes\s*=[^=]"),
        new(@"\bDirectory\.Delete\b|\bFile\.Delete\b|\.Delete\("),
    ];

    internal static List<string> RealOsViolations(string name, string text)
    {
        var code = StripComments(text);
        var list = Hits(Forbidden, code).Select(h => $"{name}: {h}").ToList();
        // DestructiveApiTests が数える呼び出し。許可するのはコピーの 1 か所だけ（それ以外は 1 つでも失敗）
        var destructive = DestructiveApiTests.Destructive.Matches(code).Select(m => m.Value).ToList();
        var allowed = code.Contains(TheCopy) && name.EndsWith("RealOsTransferTests.cs") ? 1 : 0;
        if (destructive.Count > allowed) list.AddRange(destructive.Skip(allowed).Select(h => $"{name}: {h}"));
        return list;
    }

    [Fact]
    public void 実OSのテストの部品は消す呼び出しも属性を変える呼び出しも持たない()
    {
        var root = SchemaManifest.RepoRoot();
        var problems = new List<string>();
        foreach (var file in RealOsFiles)
        {
            Assert.True(File.Exists(Path.Combine(root, file)), file);
            problems.AddRange(RealOsViolations(Path.GetFileName(file), File.ReadAllText(Path.Combine(root, file))));
        }
        Assert.Empty(problems);
        // コピーは作業場所の中の新しいファイルを、決めた宛先へ複製する 1 か所だけ
        var transfer = File.ReadAllText(Path.Combine(root, RealOsFiles[^1]));
        Assert.Single(Regex.Matches(StripComments(transfer), Regex.Escape(TheCopy)));
        // 呼び出す部品は、読んだ部品とシェルの転送と STA の実行（消さない）だけ
        Assert.Contains("Sta.Run(", transfer);
    }

    [Theory]
    [InlineData("Sta.WithTempFolder(root => { });")]
    [InlineData("Directory.Delete(root, true);")]
    [InlineData("File.Delete(path);")]
    [InlineData("File.SetAttributes(file, FileAttributes.Normal);")]
    [InlineData("new FileInfo(p).Attributes = FileAttributes.Hidden;")]
    [InlineData("File.WriteAllText(path, text);")]
    [InlineData("new FileStream(path, FileMode.Create);")]
    [InlineData("File.Move(a, b);")]
    [InlineData("operation.Move(a, b);")]
    [InlineData("operation.Copy(a, b); operation.Copy(c, d);")]
    public void 実OSのテストの検査は違反を捕まえる(string text) => Assert.NotEmpty(RealOsViolations("RealOsTransferTests.cs", text));

    [Theory]
    [InlineData("operation.Copy(source, destination);")]
    [InlineData("using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);")]
    [InlineData("// Directory.Delete は呼ばない")]
    public void 実OSのテストの検査は新規作成とコピーの1か所を通す(string text) => Assert.Empty(RealOsViolations("RealOsTransferTests.cs", text));

    [Fact]
    public void コピーの2か所目は違反()
    {
        Assert.NotEmpty(RealOsViolations("RealOsTransferTests.cs", "operation.Copy(a, b);\noperation.Copy(c, d);"));
        Assert.NotEmpty(RealOsViolations("TreeWatch.cs", "operation.Copy(a, b);"));
    }
}
