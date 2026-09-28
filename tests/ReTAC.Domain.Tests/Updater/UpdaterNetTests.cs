using System.Net;
using System.Text;
using ReTAC.Updater.Core;
using ReTAC.Updater.Net;

namespace ReTAC.Domain.Tests.Updater;

/// <summary>
/// R-109-2: GitHub との通信の失敗の見分けと、表示まで届くこと。偽の応答を返すハンドラーで確かめる（実際には通信しない）。
/// </summary>
public class UpdaterNetTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Status(int code, string? remaining = null, string? reset = null)
    {
        var response = new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent("{}") };
        if (remaining is not null) response.Headers.Add("x-ratelimit-remaining", remaining);
        if (reset is not null) response.Headers.Add("x-ratelimit-reset", reset);
        return response;
    }

    private static GitHubReleases Client(Func<HttpRequestMessage, HttpResponseMessage> respond, out FakeHandler handler)
    {
        handler = new FakeHandler(respond);
        return new GitHubReleases(handler, TimeSpan.FromSeconds(5));
    }

    private static string Reset(DateTimeOffset at) => at.ToUnixTimeSeconds().ToString();

    // ---- ヘッダー ----

    [Fact]
    public async Task 要求にはUserAgentとAPIの版を付ける()
    {
        using var github = Client(_ => Json("""{"tag_name":"v2.8.0","assets":[]}"""), out var handler);
        var tag = await github.LatestTagAsync(CancellationToken.None);

        Assert.Equal("v2.8.0", tag.Value);
        var request = handler.Requests.Single();
        Assert.Contains("ReTAC.Updater", request.Headers.UserAgent.ToString());
        Assert.Equal("2022-11-28", request.Headers.GetValues("X-GitHub-Api-Version").Single());
        Assert.Contains("application/vnd.github+json", request.Headers.Accept.ToString());
        Assert.EndsWith("/releases/latest", request.RequestUri!.AbsoluteUri);
    }

    // ---- 各段階の失敗 ----

    public static TheoryData<int> LimitCodes() => [403, 429];

    [Theory]
    [MemberData(nameof(LimitCodes))]
    public async Task 最新版の確認でのレート制限はやり直せる時刻を付ける(int code)
    {
        var at = new DateTimeOffset(2026, 9, 28, 10, 30, 0, TimeSpan.Zero);
        using var github = Client(_ => Status(code, "0", Reset(at)), out _);
        var failure = (await github.LatestTagAsync(CancellationToken.None)).Failure!;

        Assert.Equal(Reason.RateLimited, failure.Reason);
        Assert.Equal(at.ToLocalTime().ToString("HH:mm"), failure.RetryAt);
        Assert.Equal($"HTTP {code}", failure.Detail);
        Assert.Equal($"GitHub への問い合わせが多すぎます。{failure.RetryAt} 以降にやり直してください。ReTAC は変更していません。",
                     Outcome.Message(ReplaceResult.Unchanged, failure.Reason, false, false, "", failure.RetryAt));
    }

    [Theory]
    [MemberData(nameof(LimitCodes))]
    public async Task アセットの取得でのレート制限も見分ける(int code)
    {
        using var github = Client(_ => Status(code, "0", Reset(DateTimeOffset.UtcNow)), out var handler);
        var failure = (await github.AssetAsync("2.8.0", CancellationToken.None)).Failure!;

        Assert.Equal(Reason.RateLimited, failure.Reason);
        Assert.NotNull(failure.RetryAt);
        Assert.EndsWith("/releases/tags/v2.8.0", handler.Requests.Single().RequestUri!.AbsoluteUri);
    }

    [Theory]
    [MemberData(nameof(LimitCodes))]
    public async Task ダウンロードでのレート制限も見分ける(int code)
    {
        using var github = Client(_ => Status(code, "0", Reset(DateTimeOffset.UtcNow)), out _);
        using var target = new MemoryStream();
        var failure = await github.DownloadAsync("https://example.invalid/x.zip", target, null, CancellationToken.None);

        Assert.Equal(Reason.RateLimited, failure!.Reason);
    }

    [Fact]
    public async Task 残りの回数が0でない403はGitHubのエラー()
    {
        using var github = Client(_ => Status(403, "12"), out _);
        var failure = (await github.LatestTagAsync(CancellationToken.None)).Failure!;
        Assert.Equal((Reason.GitHubError, "HTTP 403"), (failure.Reason, failure.Detail));
    }

    [Fact]
    public async Task 無い版の404は状態コードを詳細に入れる()
    {
        using var github = Client(_ => Status(404), out _);
        var failure = (await github.AssetAsync("9.9.9", CancellationToken.None)).Failure!;
        Assert.Equal((Reason.GitHubError, "HTTP 404"), (failure.Reason, failure.Detail));
    }

    [Theory]
    [InlineData("99999999999999999")]   // FromUnixTimeSeconds の範囲外
    [InlineData("-99999999999999999")]
    [InlineData("abc")]
    [InlineData(null)]
    public async Task やり直せる時刻が読めなければ時刻を省いてレート制限として扱う(string? reset)
    {
        using var github = Client(_ => Status(403, "0", reset), out _);
        var failure = (await github.LatestTagAsync(CancellationToken.None)).Failure!;

        Assert.Equal(Reason.RateLimited, failure.Reason);
        Assert.Null(failure.RetryAt);
        Assert.StartsWith("GitHub への問い合わせが多すぎます。しばらくしてから",
                          Outcome.Message(ReplaceResult.Unchanged, failure.Reason, false, false, "", failure.RetryAt));
    }

    [Fact]
    public async Task 目的のzipやdigestが無ければリリースに更新用のファイルが無い()
    {
        using var github = Client(_ => Json("""{"tag_name":"v2.8.0","assets":[{"name":"ReTAC-2.8.0-win-x64.zip","browser_download_url":"https://x/y.zip"}]}"""), out _);
        var failure = (await github.AssetAsync("2.8.0", CancellationToken.None)).Failure!;
        Assert.Equal(Reason.AssetMissing, failure.Reason);
    }

    [Fact]
    public async Task アセットは名前が一致するものの場所とdigestを返す()
    {
        using var github = Client(_ => Json("""{"tag_name":"v2.8.0","assets":[{"name":"other.zip","browser_download_url":"https://x/other.zip","digest":"sha256:00"},{"name":"ReTAC-2.8.0-win-x64.zip","browser_download_url":"https://x/y.zip","digest":"sha256:ab"}]}"""), out _);
        var asset = (await github.AssetAsync("2.8.0", CancellationToken.None)).Value!;
        Assert.Equal(("https://x/y.zip", "sha256:ab"), (asset.DownloadUrl, asset.Digest));
    }

    // ---- 保存先の失敗 ----

    /// <summary>書き込むと失敗するストリーム（ディスクの不足などに見立てる）。</summary>
    private sealed class FullDisk : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("ディスクの空きが足りません。");

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            throw new IOException("ディスクの空きが足りません。");
    }

    [Fact]
    public async Task 保存先への書き込みの失敗は通信の失敗と分ける()
    {
        using var github = Client(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[1000]) }, out _);
        using var target = new FullDisk();
        var failure = await github.DownloadAsync("https://example.invalid/x.zip", target, null, CancellationToken.None);

        Assert.Equal(Reason.WriteFailed, failure!.Reason);
        Assert.Contains("ディスク", failure.Detail);
    }

    [Fact]
    public async Task ダウンロードは受け取った量を知らせて最後まで書く()
    {
        var bytes = Enumerable.Range(0, 200_000).Select(i => (byte)i).ToArray();
        using var github = Client(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }, out _);
        using var target = new MemoryStream();
        long last = 0;
        var failure = await github.DownloadAsync("https://example.invalid/x.zip", target, n => last = n, CancellationToken.None);

        Assert.Null(failure);
        Assert.Equal(bytes, target.ToArray());
        Assert.Equal(bytes.Length, last);
    }

    // ---- 閉じるときに ReTAC を起動するか（R-109-6）----

    [Theory]
    // 結果を出し終えた後に閉じる: 起動しない（起動は結果の前に決め済み）
    [InlineData(false, true, true, true, false)]
    [InlineData(false, true, false, false, false)]
    // 昇格した作業が「変更なし」で終わり、ReTAC も終了させていない: 起動しない
    [InlineData(false, true, false, true, false)]
    // 作業中に閉じる: 入れ替えに入っていれば起動する
    [InlineData(true, false, false, true, true)]
    [InlineData(true, false, false, false, false)]
    // 待っている間に閉じる: 終了させた ReTAC があれば起動する
    [InlineData(false, false, true, false, true)]
    [InlineData(false, false, false, false, false)]
    public void 閉じるときにReTACを起動するか(bool workRunning, bool finished, bool stopped, bool replacing, bool launch) =>
        Assert.Equal(launch, Outcome.LaunchOnClose(workRunning, finished, stopped, replacing));
}
