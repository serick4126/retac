using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;
using ReTAC.Updater.Core;

namespace ReTAC.Updater.Net;

/// <summary>GitHub の結果。失敗なら <see cref="Failure"/> が入る。</summary>
public sealed class Fetched<T> where T : class
{
    private Fetched(T? value, FetchFailure? failure)
    {
        Value = value;
        Failure = failure;
    }

    public T? Value { get; }
    public FetchFailure? Failure { get; }
    public bool Ok => Value is not null;

    public static Fetched<T> Success(T value) => new(value, null);
    public static Fetched<T> Fail(FetchFailure failure) => new(null, failure);
    public static Fetched<T> Fail(Reason reason, string? detail = null) => new(null, new FetchFailure(reason, detail));
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
    private static readonly TimeSpan DefaultStallTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _client;
    private readonly TimeSpan _stallTimeout;

    public GitHubReleases() : this(new HttpClientHandler(), DefaultStallTimeout)
    {
    }

    /// <summary>テストから、偽の応答を返すハンドラーと短い打ち切りの時間を渡す。</summary>
    public GitHubReleases(HttpMessageHandler handler, TimeSpan stallTimeout)
    {
        _stallTimeout = stallTimeout;
        // 時間の上限は要求ごとに付ける（ダウンロードには全体の上限を設けない）
        _client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var version = typeof(GitHubReleases).Assembly.GetName().Version ?? new Version(0, 0, 0);
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
    /// 受け取り側の失敗（通信）と、保存先への書き込みの失敗（ディスクの不足など）は、別の理由にする。
    /// </summary>
    public async Task<FetchFailure?> DownloadAsync(string url, Stream target, Action<long>? received, CancellationToken cancel)
    {
        using var stall = new CancellationTokenSource(_stallTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, stall.Token);
        try
        {
            using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return Classify(response);

            using var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var buffer = new byte[81920];
            long total = 0;
            while (true)
            {
                stall.CancelAfter(_stallTimeout);   // 届くたびに数え直す
                var read = await source.ReadAsync(buffer, 0, buffer.Length, linked.Token).ConfigureAwait(false);
                if (read == 0) break;
                try { await target.WriteAsync(buffer, 0, read).ConfigureAwait(false); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return new FetchFailure(Reason.WriteFailed, ex.Message);
                }
                total += read;
                received?.Invoke(total);
            }
            try { target.Flush(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new FetchFailure(Reason.WriteFailed, ex.Message);
            }
            return null;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { return new FetchFailure(Reason.Cancelled); }
        catch (OperationCanceledException) { return new FetchFailure(Reason.TimedOut); }
        catch (HttpRequestException ex) { return new FetchFailure(Reason.CannotConnect, (ex.InnerException ?? ex).Message); }
        catch (IOException ex) { return new FetchFailure(Reason.CannotConnect, ex.Message); }   // 受け取り側の切断
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
            if (!response.IsSuccessStatusCode) return Fetched<T>.Fail(Classify(response));
            using var body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var release = (ReleaseDto?)new DataContractJsonSerializer(typeof(ReleaseDto)).ReadObject(body);
            return release is null ? Fetched<T>.Fail(Reason.GitHubError, "応答を読めない") : pick(release);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { return Fetched<T>.Fail(Reason.Cancelled); }
        catch (OperationCanceledException) { return Fetched<T>.Fail(Reason.TimedOut); }
        catch (HttpRequestException ex) { return Fetched<T>.Fail(Reason.CannotConnect, (ex.InnerException ?? ex).Message); }
        catch (SerializationException ex) { return Fetched<T>.Fail(Reason.GitHubError, ex.Message); }
    }

    /// <summary>
    /// 403・429 で残りの回数が 0 ならレート制限。やり直せる時刻は x-ratelimit-reset から作り、読めない・範囲外なら省く。
    /// それ以外は GitHub のエラー（状態コードを詳細に入れる）。
    /// </summary>
    public static FetchFailure Classify(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        if (status is 403 or 429 && Header(response, "x-ratelimit-remaining") == "0")
            return new FetchFailure(Reason.RateLimited, $"HTTP {status}", RetryAt(Header(response, "x-ratelimit-reset")));
        return new FetchFailure(Reason.GitHubError, $"HTTP {status}");
    }

    private static string? RetryAt(string? reset)
    {
        if (!long.TryParse(reset, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)) return null;
        try { return DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture); }
        catch (ArgumentOutOfRangeException) { return null; }
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
