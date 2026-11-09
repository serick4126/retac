namespace ReTAC.Domain.Tests;

/// <summary>
/// R-147 / S8: 実 OS のテストの作業場所（Phase32Workspace）と見張り（TreeWatch）の部品のテスト。
/// 作業場所は成功・例外・判定の失敗のどれで終わっても何も変えず消さない。ここでも読むだけ・足すだけで、消さない・属性を変えない。
/// </summary>
public class Phase32WorkspaceTests
{
    /// <summary>作った中身と属性の記録（終わった後に読み直して比べる）</summary>
    private static Dictionary<string, string> Record(Phase32Workspace ws) => TreeWatch.Snapshot([ws.Root]);

    private static (Phase32Workspace Ws, Dictionary<string, string> Made) Populate(Phase32Workspace ws)
    {
        ws.CreateFolder("a", "empty");
        ws.CreateFile("hello", "a", "f.txt");
        ws.CreateFolder("%P32_X%");
        return (ws, Record(ws));
    }

    [Fact]
    public void 成功で終わっても作業場所は残る()
    {
        var (ws, made) = Populate(Phase32Workspace.Create());
        Assert.True(Directory.Exists(ws.Root));
        Assert.Equal(made, Record(ws));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(ws.Root, "a", "f.txt")));
    }

    [Fact]
    public void テストの本体の例外で終わっても作業場所は残る()
    {
        Phase32Workspace? created = null;
        Dictionary<string, string>? made = null;
        Action body = () =>
        {
            (created, made) = Populate(Phase32Workspace.Create());
            throw new InvalidOperationException("テストの本体の例外");
        };
        Assert.Throws<InvalidOperationException>(body);
        Assert.True(Directory.Exists(created!.Root));
        Assert.Equal(made, Record(created));
    }

    [Fact]
    public void 判定の失敗で終わっても作業場所は残る()
    {
        Phase32Workspace? created = null;
        Dictionary<string, string>? made = null;
        Action body = () =>
        {
            (created, made) = Populate(Phase32Workspace.Create());
            Assert.Equal("PASS", "FAIL");
        };
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(body);
        Assert.True(Directory.Exists(created!.Root));
        Assert.Equal(made, Record(created));
    }

    [Fact]
    public void 既にあれば作らず_既存のものに触らない()
    {
        var first = Phase32Workspace.Create();
        var marker = first.CreateFile("keep", "marker.txt");
        var before = Record(first);
        var ex = Assert.Throws<IOException>(() => Phase32Workspace.Create(Path.GetDirectoryName(first.Root)!, Path.GetFileName(first.Root)));
        Assert.Contains(first.Root, ex.Message);
        Assert.Equal(before, Record(first));
        Assert.Equal("keep", File.ReadAllText(marker));
        // 既にあるファイル・フォルダも、作り直さず例外にする（上書きしない）
        Assert.Throws<IOException>(() => first.CreateFile("other", "marker.txt"));
        Assert.Throws<IOException>(() => first.CreateFolder("marker.txt"));
        Assert.Equal("keep", File.ReadAllText(marker));
    }

    [Fact]
    public void 名前は_retac_p32_で始まり_毎回違う()
    {
        var a = Phase32Workspace.Create();
        var b = Phase32Workspace.Create();
        Assert.NotEqual(a.Root, b.Root);
        Assert.StartsWith("retac-p32-", Path.GetFileName(a.Root));
        Assert.Equal(Path.GetFullPath(Path.GetTempPath()), Path.GetDirectoryName(a.Root) + Path.DirectorySeparatorChar);
    }
}

/// <summary>R-147 / S8: 見張り。フォルダも記録する（OutsideWriteCheck はファイルだけで、空のフォルダの増減・属性を見逃す）</summary>
public class TreeWatchTests
{
    private const string Root = @"C:\ws";
    private const string Wrong = @"C:\ws\wrong";
    private const string Correct = @"C:\ws\dest\%P32_DEST%";

    private static readonly FileAttributes Dir = FileAttributes.Directory;

    /// <summary>手で作った記録（OS には何もしない）</summary>
    private static Dictionary<string, string> Base() => new(StringComparer.OrdinalIgnoreCase)
    {
        [Root] = TreeWatch.DirRecord(Dir),
        [Wrong] = TreeWatch.DirRecord(Dir),
        [Wrong + @"\empty"] = TreeWatch.DirRecord(Dir),
        [Wrong + @"\sentinel.txt"] = TreeWatch.FileRecord("original"u8.ToArray(), FileAttributes.Archive),
        [@"C:\ws\dest"] = TreeWatch.DirRecord(Dir),
        [Correct] = TreeWatch.DirRecord(Dir),
    };

    private static string Judge(Dictionary<string, string> after) => TreeWatch.Compare(Base(), after, Correct).ToString();

    [Fact]
    public void 何も変わらなければ_変わっていない() => Assert.Equal("PASS", Judge(Base()));

    [Theory]
    [InlineData(@"C:\ws\wrong")]             // 誤った宛先の根のフォルダの属性だけ
    [InlineData(@"C:\ws\wrong\empty")]       // 子のフォルダの属性だけ
    [InlineData(@"C:\ws")]                   // 作業場所の根
    public void フォルダの属性だけが違えば変わった(string folder)
    {
        var after = Base();
        after[folder] = TreeWatch.DirRecord(Dir | FileAttributes.Hidden);
        var diff = TreeWatch.Compare(Base(), after, Correct);
        Assert.Equal([folder], diff.Changed);
        Assert.Empty(diff.Created);
        Assert.Empty(diff.Removed);
    }

    [Fact]
    public void 空のフォルダが増えれば変わった()
    {
        var after = Base();
        after[Wrong + @"\new-empty"] = TreeWatch.DirRecord(Dir);
        Assert.Equal([Wrong + @"\new-empty"], TreeWatch.Compare(Base(), after, Correct).Created);
    }

    [Fact]
    public void 空のフォルダが消えれば変わった()
    {
        var after = Base();
        after.Remove(Wrong + @"\empty");
        Assert.Equal([Wrong + @"\empty"], TreeWatch.Compare(Base(), after, Correct).Removed);
    }

    [Fact]
    public void ファイルの属性だけが違えば変わった_中身が違っても変わった()
    {
        var attributes = Base();
        attributes[Wrong + @"\sentinel.txt"] = TreeWatch.FileRecord("original"u8.ToArray(), FileAttributes.Archive | FileAttributes.ReadOnly);
        Assert.Equal([Wrong + @"\sentinel.txt"], TreeWatch.Compare(Base(), attributes, Correct).Changed);
        var content = Base();
        content[Wrong + @"\sentinel.txt"] = TreeWatch.FileRecord("changed"u8.ToArray(), FileAttributes.Archive);
        Assert.Equal([Wrong + @"\sentinel.txt"], TreeWatch.Compare(Base(), content, Correct).Changed);
    }

    [Fact]
    public void 正しい宛先の中だけが違えば変わっていない()
    {
        var after = Base();
        after[Correct + @"\payload.txt"] = TreeWatch.FileRecord("x"u8.ToArray(), FileAttributes.Archive);
        after[Correct + @"\sub"] = TreeWatch.DirRecord(Dir);
        Assert.Equal("PASS", Judge(after));
    }

    [Fact]
    public void 同じ名前で始まる兄弟は宛先の中ではない()
    {
        var after = Base();
        after[Correct + "2"] = TreeWatch.DirRecord(Dir);
        after[Correct + @"2\x.txt"] = TreeWatch.FileRecord("x"u8.ToArray(), FileAttributes.Archive);
        Assert.Equal([Correct + "2", Correct + @"2\x.txt"], TreeWatch.Compare(Base(), after, Correct).Created);
    }

    [Fact]
    public void 宛先のフォルダ自体の属性は比べる()
    {
        var after = Base();
        after[Correct] = TreeWatch.DirRecord(Dir | FileAttributes.ReadOnly);
        Assert.Equal([Correct], TreeWatch.Compare(Base(), after, Correct).Changed);
    }

    [Fact]
    public void 記録を取ると_フォルダと属性と根が入る()
    {
        var ws = Phase32Workspace.Create();
        var empty = ws.CreateFolder("wrong", "empty");
        var file = ws.CreateFile("original", "wrong", "sentinel.txt");
        var records = TreeWatch.Snapshot([ws.Root]);
        Assert.Equal(TreeWatch.DirRecord(File.GetAttributes(ws.Root)), records[ws.Root]);   // 根そのもの
        Assert.Equal(TreeWatch.DirRecord(File.GetAttributes(empty)), records[empty]);       // 空のフォルダ
        Assert.StartsWith("D ", records[empty]);
        Assert.Contains("Directory", records[empty]);
        Assert.Equal(TreeWatch.FileRecord("original"u8.ToArray(), File.GetAttributes(file)), records[file]);
    }

    [Fact]
    public void 新しい空のフォルダを足すと記録が変わる()
    {
        var ws = Phase32Workspace.Create();
        ws.CreateFolder("wrong");
        var elsewhere = Path.Combine(ws.Root, "elsewhere");
        var before = TreeWatch.Snapshot([ws.Root]);
        var legacyBefore = OutsideWriteCheck.Snapshot([ws.Root], elsewhere);

        var added = ws.CreateFolder("wrong", "added-empty");

        Assert.Equal([added], TreeWatch.Compare(before, TreeWatch.Snapshot([ws.Root]), elsewhere).Created);
        // 書庫の調査の見張り（ファイルしか記録しない）は、空のフォルダが増えても合格にする。Phase 32 で取り替えない理由
        Assert.Equal("PASS", OutsideWriteCheck.Judge(legacyBefore, OutsideWriteCheck.Snapshot([ws.Root], elsewhere)));
    }
}
