using System.IO.Compression;
using ReTAC.Updater.Core;

namespace ReTAC.Domain.Tests.Updater;

/// <summary>
/// R-109-4: 置き換え。実際のファイルでインストール先を作り、ローカルの zip から置き換える。
/// ファイルの中身は「版:名前」の文字列にして、どの版のファイルかを中身で見分ける。
/// </summary>
public sealed class UpdaterReplaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ReTAC-replace-test-" + Guid.NewGuid().ToString("N"));
    private readonly string _install;

    public UpdaterReplaceTests()
    {
        _install = Path.Combine(_root, "install");
        Directory.CreateDirectory(_install);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string Content(string version, string name) => $"{version}:{name}";

    /// <summary>旧版の一式と、利用者のファイル（設定ファイル・余分なファイル）を置く。</summary>
    private void Install(string version)
    {
        foreach (var name in Distribution.Names) File.WriteAllText(Path.Combine(_install, name), Content(version, name));
        File.WriteAllText(Path.Combine(_install, Protocol.SettingsFile), "settings");
        File.WriteAllText(Path.Combine(_install, "extra.txt"), "extra");
    }

    private string Zip(string version, IEnumerable<string>? names = null)
    {
        var path = Path.Combine(_root, $"{version}-{Guid.NewGuid():N}.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var name in names ?? Distribution.Names)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open());
            writer.Write(Content(version, Distribution.Canonical(Path.GetFileName(name)) ?? name));
        }
        return path;
    }

    private static Func<StagingFolder, FetchFailure?> FromZip(string zipPath) => staging =>
    {
        using var stream = File.OpenRead(zipPath);
        return ZipPackage.Extract(stream, staging) is { } problem ? new FetchFailure(problem) : null;
    };

    private ReplaceRequest Request(string zipPath) =>
        new(_install, FromZip(zipPath)) { RetryDelay = TimeSpan.FromMilliseconds(10), Attempts = 2 };

    private string Read(string name) => File.ReadAllText(Path.Combine(_install, name));

    private void AssertUserFilesUntouched()
    {
        Assert.Equal("settings", Read(Protocol.SettingsFile));
        Assert.Equal("extra", Read("extra.txt"));
    }

    private IEnumerable<string> StagingFolders() => Directory.GetDirectories(_install, StagingFolder.Prefix + "*");

    // ---- ハンドルと照合（R-109-2）----

    [Fact]
    public void ダウンロード先はほかから開けず閉じると消える()
    {
        string path;
        using (var stream = ZipPackage.CreateDownloadFile(_root))
        {
            path = stream.Name;
            stream.Write([1, 2, 3], 0, 3);
            Assert.ThrowsAny<IOException>(() => File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
        }
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void 照合はsha256のdigestと比べる()
    {
        using var stream = new MemoryStream([1, 2, 3]);
        var hash = ZipPackage.Sha256(stream);
        Assert.True(ZipPackage.Matches(stream, "sha256:" + hash.ToUpperInvariant()));
        Assert.False(ZipPackage.Matches(stream, "sha256:" + new string('0', 64)));
        Assert.False(ZipPackage.Matches(stream, hash));   // 形の違う digest
    }

    [Fact]
    public void 壊れたzipは壊れているとして拒否する()
    {
        Install("v1");
        var bad = Path.Combine(_root, "bad.zip");
        File.WriteAllText(bad, "not a zip");
        var report = Replacer.Run(Request(bad));
        Assert.Equal((ReplaceResult.Unchanged, Reason.Corrupt), (report.Result, report.Reason));
        Assert.Equal(Content("v1", Protocol.ReTacExe), Read(Protocol.ReTacExe));
        Assert.Empty(StagingFolders());
    }

    [Fact]
    public void 配布物の外を含むzipは何も置かずに拒否する()
    {
        Install("v1");
        var report = Replacer.Run(Request(Zip("v2", [.. Distribution.Names, "x.dll"])));
        Assert.Equal((ReplaceResult.Unchanged, Reason.UnsupportedFormat), (report.Result, report.Reason));
        Assert.All(Distribution.Names, n => Assert.Equal(Content("v1", n), Read(n)));
        Assert.Empty(StagingFolders());
    }

    [Fact]
    public void 準備フォルダに先に置かれたファイルは展開に失敗しても消さない()
    {
        Install("v1");
        string? planted = null;
        var request = new ReplaceRequest(_install, staging =>
        {
            // 展開の前に、同じ名前のファイルが置かれた
            planted = Path.Combine(staging.FullPath, "README.md");
            File.WriteAllText(planted, "user");
            return FromZip(Zip("v2"))(staging);
        }) { RetryDelay = TimeSpan.FromMilliseconds(10), Attempts = 2 };

        var report = Replacer.Run(request);

        Assert.Equal(ReplaceResult.Unchanged, report.Result);
        Assert.Equal("user", File.ReadAllText(planted!));
        Assert.All(Distribution.Names, n => Assert.Equal(Content("v1", n), Read(n)));
    }

    // ---- 置き換え ----

    [Fact]
    public void 配布物だけが新しくなり利用者のファイルは変わらない()
    {
        Install("v1");
        var report = Replacer.Run(Request(Zip("v2")));

        Assert.Equal((ReplaceResult.Completed, Distribution.Names.Count), (report.Result, report.Replaced));
        Assert.All(Distribution.Names, n => Assert.Equal(Content("v2", n), Read(n)));
        AssertUserFilesUntouched();
        Assert.Empty(StagingFolders());
    }

    [Fact]
    public void 大文字小文字の違う項目も正しい名前で置く()
    {
        Install("v1");
        Replacer.Run(Request(Zip("v2", ["RETAC.EXE", "retac.updater.EXE"])));
        Assert.Contains(Protocol.ReTacExe, Directory.GetFiles(_install).Select(Path.GetFileName));
        Assert.Equal(Content("v2", Protocol.ReTacExe), Read(Protocol.ReTacExe));
    }

    [Fact]
    public void インストール先に無かった配布物も置く()
    {
        Install("v1");
        File.Delete(Path.Combine(_install, "README.md"));
        Assert.Equal(ReplaceResult.Completed, Replacer.Run(Request(Zip("v2"))).Result);
        Assert.Equal(Content("v2", "README.md"), Read("README.md"));
    }

    [Fact]
    public void 置き換えの排他をほかが持っていれば何もしない()
    {
        Install("v1");
        var name = @"Global\ReTAC.Updater.Test." + Guid.NewGuid().ToString("N");
        using var held = NamedLock.TryAcquire(name);
        var request = Request(Zip("v2"));
        request.LockName = name;

        ReplaceReport? report = null;
        var thread = new Thread(() => report = Replacer.Run(request));
        thread.Start();
        thread.Join();

        Assert.Equal((ReplaceResult.Unchanged, Reason.OtherUpdater), (report!.Result, report.Reason));
        Assert.All(Distribution.Names, n => Assert.Equal(Content("v1", n), Read(n)));
    }

    // ---- 各境界で止めたとき（強制終了に見立てる）----

    public static TheoryData<int> Boundaries() => [.. Enumerable.Range(0, 7)];

    [Theory]
    [MemberData(nameof(Boundaries))]
    public void どの境界で止まっても各ファイルは旧版か新版でReTACexeは最後(int k)
    {
        Install("v1");
        var request = Request(Zip("v2"));
        request.BeforeReplace = i => { if (i == k) throw new SimulatedStop(); };
        Assert.Throws<SimulatedStop>(() => Replacer.Run(request));

        var newCount = Distribution.Names.Count(n => Read(n) == Content("v2", n));
        Assert.All(Distribution.Names, n => Assert.Contains(Read(n), new[] { Content("v1", n), Content("v2", n) }));
        Assert.Equal(k, newCount);
        Assert.Equal(k == Distribution.Names.Count, Read(Protocol.ReTacExe) == Content("v2", Protocol.ReTacExe));
        AssertUserFilesUntouched();

        // 止まったときの準備フォルダは残り、次の置き換えで知らされる。やり直せばすべて新版になる
        var again = Replacer.Run(Request(Zip("v2")));
        Assert.Equal(ReplaceResult.Completed, again.Result);
        Assert.Single(again.Leftovers);
        Assert.All(Distribution.Names, n => Assert.Equal(Content("v2", n), Read(n)));
        AssertUserFilesUntouched();
    }

    // ---- 入れ替えの失敗（MoveFileEx）----

    [Theory]
    [InlineData(Protocol.ReTacExe)]
    [InlineData("README.md")]
    public void 入れ替えに失敗したファイルは元の名前と中身のまま残る(string locked)
    {
        Install("v1");
        // 削除の共有を許さずに開いておくと、名前の付け替えで置き換えられない
        using (new FileStream(Path.Combine(_install, locked), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var report = Replacer.Run(Request(Zip("v2")));
            Assert.Equal(Reason.WriteFailed, report.Reason);
            Assert.NotEqual(ReplaceResult.Completed, report.Result);
            Assert.Equal(Content("v1", locked), Read(locked));
        }
        Assert.All(Distribution.Names, n => Assert.Contains(Read(n), new[] { Content("v1", n), Content("v2", n) }));
        Assert.Empty(StagingFolders());   // 今回の準備フォルダは片付いている
    }

    // ---- 同時に書いたとき（INV-UPDATER-CONCURRENT-SAFE）----

    /// <summary>
    /// A を k 個入れ替えたところで待たせ、B を j 個入れ替えたところで待たせ、A を最後まで進め、B を最後まで進める。
    /// 名前の付け替えは 1 回の操作なので、「強制終了されたプロセスの保留中の操作が後から済む」ことは「後から行う」ことで再現できる。
    /// </summary>
    private (ReplaceReport A, ReplaceReport B) Interleave(string zipA, string zipB, int k, int j)
    {
        using var aPaused = new ManualResetEventSlim();
        using var aGo = new ManualResetEventSlim();
        using var bPaused = new ManualResetEventSlim();
        using var bGo = new ManualResetEventSlim();

        ReplaceRequest Paused(string zip, int at, ManualResetEventSlim paused, ManualResetEventSlim go)
        {
            var request = Request(zip);   // 排他を使わない（強制終了で放棄された場合）
            request.BeforeReplace = i => { if (i == at) { paused.Set(); go.Wait(); } };
            return request;
        }

        ReplaceReport? a = null, b = null;
        var threadA = new Thread(() => a = Replacer.Run(Paused(zipA, k, aPaused, aGo)));
        var threadB = new Thread(() => b = Replacer.Run(Paused(zipB, j, bPaused, bGo)));
        threadA.Start();
        aPaused.Wait();
        threadB.Start();
        bPaused.Wait();
        aGo.Set();
        threadA.Join();
        bGo.Set();
        threadB.Join();
        return (a!, b!);
    }

    public static TheoryData<int, int> Pairs()
    {
        var data = new TheoryData<int, int>();
        for (var k = 0; k <= 6; k++)
            for (var j = 0; j <= 6; j++)
                data.Add(k, j);
        return data;
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void 同じ版を同時に書いても壊れない(int k, int j)
    {
        Install("v1");
        var (a, b) = Interleave(Zip("v2"), Zip("v2"), k, j);

        foreach (var report in new[] { a, b })
        {
            Assert.Contains(report.Result, new[] { ReplaceResult.Completed, ReplaceResult.Partial, ReplaceResult.Unchanged });
            Assert.Equal(report.Result == ReplaceResult.Unchanged, report.Replaced == 0);
            Assert.Equal(report.Result == ReplaceResult.Completed, report.Replaced == Distribution.Names.Count);
        }
        Assert.All(Distribution.Names, n => Assert.Contains(Read(n), new[] { Content("v1", n), Content("v2", n) }));
        AssertUserFilesUntouched();
        Assert.Empty(StagingFolders());   // それぞれが自分の準備フォルダを片付けた

        Assert.Equal(ReplaceResult.Completed, Replacer.Run(Request(Zip("v2"))).Result);
        Assert.All(Distribution.Names, n => Assert.Equal(Content("v2", n), Read(n)));
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void 異なる版が同時に書いても2つのexeが古ければ最新と判定しない(int k, int j)
    {
        // A（v1 を入れる）の k 個目以降を、B（v2 を入れる）が j 個目で止まっている間に行う
        Install("v0");
        Interleave(Zip("v1"), Zip("v2"), k, j);

        var versions = new[] { "v0", "v1", "v2" };
        Assert.All(Distribution.Names, n => Assert.Contains(Read(n), versions.Select(v => Content(v, n))));
        AssertUserFilesUntouched();

        var latest = new Version(2, 0, 0);
        var reTac = VersionOf(Protocol.ReTacExe);
        var updater = VersionOf(Protocol.UpdaterExe);
        var upToDate = Versions.IsUpToDate(latest, reTac, updater);
        Assert.Equal(reTac == latest && updater == latest, upToDate);

        // 「最新です」でなければ、もう一度 v2 で置き換えると 2 つの exe が v2 になる（文書は古く残りうるが許す）
        if (!upToDate) Replacer.Run(Request(Zip("v2")));
        Assert.Equal(latest, VersionOf(Protocol.ReTacExe));
        Assert.Equal(latest, VersionOf(Protocol.UpdaterExe));
    }

    /// <summary>
    /// A を k 個入れ替えたところで待たせ、その間に B を最後まで終えさせ、そのあと A の残りを行う。
    /// 強制終了された古いアップデータ（A）の保留中の入れ替えが、新しいアップデータ（B）の完了の後に済む順序。
    /// </summary>
    private (ReplaceReport A, ReplaceReport B) LateOld(string zipA, string zipB, int k)
    {
        using var aPaused = new ManualResetEventSlim();
        using var aGo = new ManualResetEventSlim();
        var requestA = Request(zipA);
        requestA.BeforeReplace = i => { if (i == k) { aPaused.Set(); aGo.Wait(); } };

        ReplaceReport? a = null;
        var threadA = new Thread(() => a = Replacer.Run(requestA));
        threadA.Start();
        aPaused.Wait();
        var b = Replacer.Run(Request(zipB));   // B は止まらずに最後まで
        aGo.Set();
        threadA.Join();
        return (a!, b);
    }

    public static TheoryData<int> LateOldPoints() => [.. Enumerable.Range(0, 7)];

    [Theory]
    [MemberData(nameof(LateOldPoints))]
    public void 新しいほうが終えた後に古いほうの入れ替えが済んでも最新の判定と再更新で直る(int k)
    {
        Install("v0");
        var (a, b) = LateOld(Zip("v1"), Zip("v2"), k);

        Assert.Equal(ReplaceResult.Completed, b.Result);
        Assert.Contains(a.Result, new[] { ReplaceResult.Completed, ReplaceResult.Partial, ReplaceResult.Unchanged });
        var versions = new[] { "v0", "v1", "v2" };
        Assert.All(Distribution.Names, n => Assert.Contains(Read(n), versions.Select(v => Content(v, n))));
        AssertUserFilesUntouched();
        Assert.Empty(StagingFolders());

        var latest = new Version(2, 0, 0);
        var reTac = VersionOf(Protocol.ReTacExe);
        var updater = VersionOf(Protocol.UpdaterExe);
        var upToDate = Versions.IsUpToDate(latest, reTac, updater);
        // A の残りが exe を古い版に戻したら「最新です」にはならない
        Assert.Equal(reTac == latest && updater == latest, upToDate);
        if (k < Distribution.Names.Count) Assert.False(upToDate);   // A は少なくとも ReTAC.exe（最後）を v1 に戻している

        if (!upToDate) Replacer.Run(Request(Zip("v2")));
        Assert.Equal(latest, VersionOf(Protocol.ReTacExe));
        Assert.Equal(latest, VersionOf(Protocol.UpdaterExe));
    }

    /// <summary>中身の「vN:」を版 N.0.0 として読む。</summary>
    private Version VersionOf(string name) => new(int.Parse(Read(name).Substring(1, 1)), 0, 0);
}
