using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using ReTAC.Updater;
using ReTAC.Updater.Core;
using ReTAC.Updater.Net;

namespace ReTAC.Domain.Tests.Updater;

/// <summary>
/// R-109: 部品をつないだ経路。取得の失敗が置き換えを通って表示まで届くこと、昇格したプロセスとの受け渡し、
/// 受け付けた ReTAC の見届け、展開先への書き込みの失敗。
/// </summary>
public sealed class UpdaterPathTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ReTAC-path-test-" + Guid.NewGuid().ToString("N"));
    private readonly string _install;

    public UpdaterPathTests()
    {
        _install = Path.Combine(_root, "install");
        Directory.CreateDirectory(_install);
        foreach (var name in Distribution.Names) File.WriteAllText(Path.Combine(_install, name), "v1:" + name);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private static HttpResponseMessage Status(int code, string? remaining = null, string? reset = null)
    {
        var response = new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent("{}") };
        if (remaining is not null) response.Headers.Add("x-ratelimit-remaining", remaining);
        if (reset is not null) response.Headers.Add("x-ratelimit-reset", reset);
        return response;
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private const string AssetJson = """{"tag_name":"v2.8.0","assets":[{"name":"ReTAC-2.8.0-win-x64.zip","browser_download_url":"https://example.invalid/r.zip","digest":"DIGEST"}]}""";

    private ReplaceReport RunWith(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var fetch = Fetchers.FromGitHub("2.8.0", _root, (_, _) => { }, CancellationToken.None,
                                        () => new GitHubReleases(new FakeHandler(respond), TimeSpan.FromSeconds(5)));
        return Replacer.Run(new ReplaceRequest(_install, fetch) { RetryDelay = TimeSpan.FromMilliseconds(10), Attempts = 2 });
    }

    // ---- 取得の失敗が表示まで届く（Fetchers → Replacer → Outcome）----

    [Fact]
    public void アセットの取得でのレート制限はやり直せる時刻まで表示に届く()
    {
        var at = new DateTimeOffset(2026, 9, 28, 10, 30, 0, TimeSpan.Zero);
        var report = RunWith(_ => Status(429, "0", at.ToUnixTimeSeconds().ToString()));

        Assert.Equal((ReplaceResult.Unchanged, Reason.RateLimited), (report.Result, report.Reason));
        Assert.Equal("HTTP 429", report.Detail);
        var time = at.ToLocalTime().ToString("HH:mm");
        Assert.Equal($"GitHub への問い合わせが多すぎます。{time} 以降にやり直してください。ReTAC は変更していません。",
                     Outcome.Message(report.Result, report.Reason, false, false, "v2.8.0", report.RetryAt));
    }

    [Fact]
    public void ダウンロードでの404は状態コードまで表示に届く()
    {
        var report = RunWith(r => r.RequestUri!.AbsoluteUri.Contains("/releases/tags/") ? Json(AssetJson) : Status(404));

        Assert.Equal((ReplaceResult.Unchanged, Reason.GitHubError, "HTTP 404"), (report.Result, report.Reason, report.Detail));
        Assert.All(Distribution.Names, n => Assert.Equal("v1:" + n, File.ReadAllText(Path.Combine(_install, n))));
    }

    [Fact]
    public void ダウンロードでのレート制限も表示に届く()
    {
        var report = RunWith(r => r.RequestUri!.AbsoluteUri.Contains("/releases/tags/") ? Json(AssetJson) : Status(403, "0", "abc"));

        Assert.Equal(Reason.RateLimited, report.Reason);
        Assert.Null(report.RetryAt);
        Assert.StartsWith("GitHub への問い合わせが多すぎます。しばらくしてから",
                          Outcome.Message(report.Result, report.Reason, false, false, "v2.8.0", report.RetryAt));
    }

    [Fact]
    public void 照合が合わなければ壊れているとして何も変えない()
    {
        var zip = ZipBytes();
        var json = AssetJson.Replace("DIGEST", "sha256:" + new string('0', 64));
        var report = RunWith(r => r.RequestUri!.AbsoluteUri.Contains("/releases/tags/") ? Json(json)
                                 : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) });

        Assert.Equal((ReplaceResult.Unchanged, Reason.Corrupt), (report.Result, report.Reason));
    }

    [Fact]
    public void 照合が合えば置き換える()
    {
        var zip = ZipBytes();
        var json = AssetJson.Replace("DIGEST", "sha256:" + Convert.ToHexString(SHA256.HashData(zip)));
        var report = RunWith(r => r.RequestUri!.AbsoluteUri.Contains("/releases/tags/") ? Json(json)
                                 : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) });

        Assert.Equal(ReplaceResult.Completed, report.Result);
        Assert.All(Distribution.Names, n => Assert.Equal("v2:" + n, File.ReadAllText(Path.Combine(_install, n))));
        Assert.Empty(Directory.GetFiles(_root, "ReTAC-*.zip"));   // ダウンロードした zip は閉じたときに消えている
    }

    private static byte[] ZipBytes()
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var name in Distribution.Names)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write("v2:" + name);
            }
        return memory.ToArray();
    }

    // ---- 展開先への書き込みの失敗 ----

    [Fact]
    public void 展開先に書けなければファイルを置けなかったとして扱う()
    {
        var zip = Path.Combine(_root, "v2.zip");
        File.WriteAllBytes(zip, ZipBytes());
        var request = new ReplaceRequest(_install, staging =>
        {
            // 書き出す名前の場所にフォルダがある（書き込めない）
            Directory.CreateDirectory(Path.Combine(staging.FullPath, "README.md"));
            using var stream = File.OpenRead(zip);
            return ZipPackage.Extract(stream, staging) is { } problem ? new FetchFailure(problem) : null;
        });

        var report = Replacer.Run(request);

        Assert.Equal((ReplaceResult.Unchanged, Reason.WriteFailed), (report.Result, report.Reason));
        Assert.Equal("ファイルを置けませんでした。ReTAC は変更していません。",
                     Outcome.Message(report.Result, report.Reason, false, false, "v2.8.0"));
        Assert.All(Distribution.Names, n => Assert.Equal("v1:" + n, File.ReadAllText(Path.Combine(_install, n))));
    }

    // ---- 昇格したプロセスとの受け渡し（R-109-5）----

    [Fact]
    public void 終わりの行は詳細とやり直せる時刻を往復する()
    {
        var line = ProgressLine.Parse(ProgressLine.End("HTTP 429", "10:30"))!;
        Assert.True(line.IsEnd);
        Assert.Equal(("HTTP 429", "10:30"), (line.File, line.RetryAt));
    }

    [Fact]
    public void 詳細の中のタブや改行は空白にして往復する()
    {
        var line = ProgressLine.Parse(ProgressLine.End("a\tb\r\nc", null))!;
        Assert.Equal("a b  c", line.File);
        Assert.Null(line.RetryAt);
    }

    [Fact]
    public void 進み具合の行は段とファイル名を往復する()
    {
        var line = ProgressLine.Parse(ProgressLine.Format("入れ替え", Protocol.ReTacExe))!;
        Assert.False(line.IsEnd);
        Assert.Equal(("入れ替え", Protocol.ReTacExe), (line.Stage, line.File));
        Assert.Null(ProgressLine.Parse(ProgressLine.Format("準備", null))!.File);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("壊れた行")]
    public void 読めない行は無いものとして扱う(string? raw) => Assert.Null(ProgressLine.Parse(raw));

    [Fact]
    public void 昇格したプロセスの詳細と時刻が表示に届く()
    {
        // 昇格したプロセスの結果（終了コード）と、最後の行（詳細・時刻）を親が合わせて表示する
        var code = ExitCodes.Encode(ReplaceResult.Unchanged, Reason.RateLimited);
        var end = ProgressLine.Parse(ProgressLine.End("HTTP 403", "10:30"))!;
        var (result, reason) = ExitCodes.Decode(code)!.Value;

        Assert.Equal("GitHub への問い合わせが多すぎます。10:30 以降にやり直してください。ReTAC は変更していません。",
                     Outcome.Message(result, reason, false, false, "v2.8.0", end.RetryAt));
        Assert.Equal("HTTP 403", end.File);
    }

    // ---- 受け付けた ReTAC の見届け（R-109-3）----

    private static PendingQuitEnd Watch(params PendingQuitState[][] steps)
    {
        var i = 0;
        return PendingQuitWatch.Wait(() => steps[Math.Min(i++, steps.Length - 1)], () => { }, idleChecks: 3);
    }

    private static readonly PendingQuitState Dialog = new(alive: true, idle: false);
    private static readonly PendingQuitState Open = new(alive: true, idle: true);
    private static readonly PendingQuitState Gone = new(alive: false, idle: false);

    [Fact]
    public void アップデータを閉じた後にK5で終了を選べば起動し直す()
    {
        // 受け付け → K-5 の確認を出している間にアップデータを閉じる → 利用者が「終了」を選ぶ → ReTAC が終わる
        Assert.Equal(PendingQuitEnd.AllExited, Watch([Dialog], [Dialog], [Dialog], [Dialog], [Gone]));
    }

    [Fact]
    public void K5で取りやめたらダイアログが閉じた後も生きているので起動し直さない() =>
        Assert.Equal(PendingQuitEnd.StayedOpen, Watch([Dialog], [Dialog], [Open], [Open], [Open]));

    [Fact]
    public void 受け付けた直後に操作できる状態が少し続いてから終わっても起動し直す() =>
        // ダイアログを出さずに閉じていく間は、少しの間は操作できる状態に見える
        Assert.Equal(PendingQuitEnd.AllExited, Watch([Open], [Open], [Gone]));

    [Fact]
    public void 複数のうち1つでも取りやめて残ればやめる() =>
        Assert.Equal(PendingQuitEnd.StayedOpen, Watch([Dialog, Dialog], [Gone, Open], [Gone, Open], [Gone, Open]));

    [Fact]
    public void すべて終わるまで見届ける() =>
        Assert.Equal(PendingQuitEnd.AllExited, Watch([Dialog, Dialog], [Gone, Dialog], [Gone, Gone]));
}
