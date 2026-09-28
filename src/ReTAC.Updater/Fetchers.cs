using System;
using System.IO;
using System.Threading;
using ReTAC.Updater.Core;
using ReTAC.Updater.Net;

namespace ReTAC.Updater;

/// <summary>
/// R-109-2: 取得・照合・展開。置き換えるプロセスが置き換えの排他を取った後、作業用のスレッドで呼ぶ（INV-UPDATER-VERIFY-IN-WRITER）。
/// ダウンロードした zip は開いたハンドルのまま照合し、同じハンドルから準備フォルダへ展開する。
/// </summary>
internal static class Fetchers
{
    /// <param name="downloadFolder">zip を置く場所。昇格していなければ作業フォルダ、昇格していれば null（準備フォルダ＝保護されたインストール先の中）</param>
    public static Func<StagingFolder, Reason?> FromGitHub(string version, string? downloadFolder,
                                                          Action<string, string?> progress, CancellationToken cancel) =>
        staging =>
        {
            using var github = new GitHubReleases();
            var asset = github.AssetAsync(version, cancel).GetAwaiter().GetResult();
            if (!asset.Ok) return asset.Failure;

            using var zip = ZipPackage.CreateDownloadFile(downloadFolder ?? staging.FullPath);
            progress("ダウンロード", null);
            var reported = DateTime.UtcNow;
            var failed = github.DownloadAsync(asset.Value!.DownloadUrl, zip, bytes =>
            {
                // 昇格したプロセスでは progress.txt に書く。親はこれが変わるのを見て「進んでいる」と判断する
                if (DateTime.UtcNow - reported < TimeSpan.FromSeconds(5)) return;
                reported = DateTime.UtcNow;
                progress("ダウンロード", $"{bytes / (1024 * 1024)} MB");
            }, cancel).GetAwaiter().GetResult();
            if (failed is not null) return failed;

            return VerifyAndExtract(zip, asset.Value.Digest, staging, progress);
        };

#if DEBUG
    /// <summary>開発用のビルドだけ。ダウンロードの代わりにローカルの zip を使う（アップデータ自身の置き換えを確かめるため）。</summary>
    public static Func<StagingFolder, Reason?> FromLocalZip(string path, string sha256, string? downloadFolder,
                                                            Action<string, string?> progress) =>
        staging =>
        {
            using var zip = ZipPackage.CreateDownloadFile(downloadFolder ?? staging.FullPath);
            using (var source = File.OpenRead(path)) source.CopyTo(zip);
            return VerifyAndExtract(zip, "sha256:" + sha256, staging, progress);
        };
#endif

    private static Reason? VerifyAndExtract(Stream zip, string digest, StagingFolder staging, Action<string, string?> progress)
    {
        progress("照合", null);
        if (!ZipPackage.Matches(zip, digest)) return Reason.Corrupt;
        progress("展開", null);
        return ZipPackage.Extract(zip, staging);
    }
}
