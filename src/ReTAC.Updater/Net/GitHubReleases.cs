using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;
using ReTAC.Updater.Core;

namespace ReTAC.Updater.Net;

/// <summary>GitHub の結果。失敗なら <see cref="Failure"/> に理由が入る。</summary>
public sealed class Fetched<T> where T : class
{
    private Fetched(T? value, Reason failure, string? detail)
    {
        Value = value;
        Failure = failure;
        Detail = detail;
    }

    public T? Value { get; }
    public Reason Failure { get; }
    public string? Detail { get; }
    public bool Ok => Value is not null;

    public static Fetched<T> Success(T value) => new(value, Reason.None, null);
    public static Fetched<T> Fail(Reason reason, string? detail = null) => new(null, reason, detail);
}

/// <summary>リリースのアセット（配布用の zip）。</summary>
public sealed class ReleaseAsset
{
    public ReleaseAsset(string downloadUrl, string digest)
    {
        DownloadUrl = downloadUrl;
        Digest = digest;
    }

    public string DownloadUrl { get; }

    /// <summary><c>sha256:16進</c>。</summary>
    public string Digest { get; }
}

/// <summary>
/// R-109-2: GitHub との通信。ReTAC の中でインターネットに出るのはここだけ（INV-NO-INTERNET）。接続先は GitHub だけ。
/// REST API は User-Agent の無い要求を拒否し、HttpClient はこれを自動では付けないので、すべての要求に付ける。
/// プロキシは OS の設定をそのまま使う（HttpClient の既定）。認証はしない。
/// </summary>
public sealed class GitHubReleases : IDisposable
{
    private const string Api = "https://api.github.com/repos/serick4126/retac/releases";
    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _client;

    public GitHubReleases()
    {
        // 時間の上限は要求ごとに付ける（ダウンロードには全体の上限を設けない）
        _client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var version = typeof(GitHubReleases).Assembly.GetName().Version;
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ReTAC.Updater", $"{version.Major}.{version.Minor}.{version.Build}"));
    }

    public void Dispose() => _client.Dispose();

    /// <summary>最新のリリースの tag_name。この API はプレリリースと下書きを返さない（U5）。</summary>
    public Task<Fetched<string>> LatestTagAsync(CancellationToken cancel) =>
        GetReleaseAsync(Api + "/latest", release => release.TagName is { } tag ? Fetched<string>.Success(tag)
                                                                               : Fetched<string>.Fail(Reason.GitHubError, "tag_name が無い"), cancel);

    /// <summary>指定した版のリリースの、配布用の zip のアセット。名前が一致しない・digest が無ければ失敗。</summary>
    public Task<Fetched<ReleaseAsset>> AssetAsync(string version, CancellationToken cancel)
    {
        var zipName = $"ReTAC-{version}-win-x64.zip";
        return GetReleaseAsync($"{Api}/tags/v{version}", release =>
        {
            var asset = release.Assets?.FirstOrDefault(a => a.Name == zipName);
            if (asset?.BrowserDownloadUrl is null || string.IsNullOrEmpty(asset.Digest))
                return Fetched<ReleaseAsset>.Fail(Reason.AssetMissing, zipName);
            return Fetched<ReleaseAsset>.Success(new ReleaseAsset(asset.BrowserDownloadUrl, asset.Digest!));
        }, cancel);
    }

    /// <summary>
    /// <paramref name="target"/> へダウンロードする。全体の上限は設けず、30 秒間 1 バイトも届かなければ打ち切る。
    /// <paramref name="received"/> で受け取った量を知らせる（昇格したプロセスが progress.txt に書き、親が進み具合を見る）。
    /// </summary>
    public async Task<Reason?> DownloadAsync(string url, Stream target, Action<long>? received, CancellationToken cancel)
    {
        using var stall = new CancellationTokenSource(StallTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, stall.Token);
        try
        {
            using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return Classify(response).Failure;

            using var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var buffer = new byte[81920];
            long total = 0;
            while (true)
            {
                stall.CancelAfter(StallTimeout);   // 届くたびに 30 秒を数え直す
                var read = await source.ReadAsync(buffer, 0, buffer.Length, linked.Token).ConfigureAwait(false);
                if (read == 0) break;
                await target.WriteAsync(buffer, 0, read, linked.Token).ConfigureAwait(false);
                total += read;
                received?.Invoke(total);
            }
            target.Flush();
            return null;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { return Reason.Cancelled; }
        catch (OperationCanceledException) { return Reason.TimedOut; }
        catch (HttpRequestException) { return Reason.CannotConnect; }
        catch (IOException) { return Reason.CannotConnect; }
    }

    private async Task<Fetched<T>> GetReleaseAsync<T>(string url, Func<ReleaseDto, Fetched<T>> pick, CancellationToken cancel) where T : class
    {
        using var timeout = new CancellationTokenSource(ApiTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, timeout.Token);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        try
        {
            using var response = await _client.SendAsync(request, linked.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var (failure, detail) = Classify(response);
                return Fetched<T>.Fail(failure, detail);
            }
            using var body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var release = (ReleaseDto?)new DataContractJsonSerializer(typeof(ReleaseDto)).ReadObject(body);
            return release is null ? Fetched<T>.Fail(Reason.GitHubError, "応答を読めない") : pick(release);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { return Fetched<T>.Fail(Reason.Cancelled); }
        catch (OperationCanceledException) { return Fetched<T>.Fail(Reason.TimedOut); }
        catch (HttpRequestException ex) { return Fetched<T>.Fail(Reason.CannotConnect, (ex.InnerException ?? ex).Message); }
        catch (SerializationException ex) { return Fetched<T>.Fail(Reason.GitHubError, ex.Message); }
    }

    /// <summary>403・429 で残りの回数が 0 ならレート制限（やり直せる時刻を詳細に入れる）。それ以外は GitHub のエラー。</summary>
    private static (Reason Failure, string Detail) Classify(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        if (status is 403 or 429 && Header(response, "x-ratelimit-remaining") == "0")
        {
            var reset = Header(response, "x-ratelimit-reset");
            var detail = long.TryParse(reset, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture) + " 以降にやり直せます"
                : "";
            return (Reason.RateLimited, detail);
        }
        return (Reason.GitHubError, $"HTTP {status}");
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    [DataContract]
    private sealed class ReleaseDto
    {
        [DataMember(Name = "tag_name")] public string? TagName { get; set; }
        [DataMember(Name = "assets")] public AssetDto[]? Assets { get; set; }
    }

    [DataContract]
    private sealed class AssetDto
    {
        [DataMember(Name = "name")] public string? Name { get; set; }
        [DataMember(Name = "browser_download_url")] public string? BrowserDownloadUrl { get; set; }
        [DataMember(Name = "digest")] public string? Digest { get; set; }
    }
}
