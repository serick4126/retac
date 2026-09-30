using ReTAC.Domain.Entries;
using ReTAC.Shell;

namespace ReTAC.Domain.Tests;

/// <summary>
/// INV-THUMBNAIL-NO-CLOUD-DOWNLOAD の技術確認。RETAC_CLOUD_DIR に OneDrive の「オンラインのみ」のファイル・フォルダを置いたフォルダを入れて走らせる。
/// 印の問い合わせ・キャッシュだけのサムネイルのどちらでも、取り込みの状態（属性の RECALL / OFFLINE）が変わらないこと。
/// あわせて、同期状態の印の番号が 0 でないこと（R-118。SHGFI_ICON の付け忘れを実物で見つける）。
/// </summary>
public class CloudHydrationTests
{
    private static readonly string? Dir = Environment.GetEnvironmentVariable("RETAC_CLOUD_DIR");

    /// <summary>未設定のまま「合格」に見えないよう、スキップとして数える（xunit 2 には Assert.Skip が無い）。</summary>
    private sealed class CloudFactAttribute : FactAttribute
    {
        public CloudFactAttribute()
        {
            if (string.IsNullOrEmpty(Dir))
                Skip = "RETAC_CLOUD_DIR が未設定。OneDrive の技術確認（INV-THUMBNAIL-NO-CLOUD-DOWNLOAD）は未実施";
        }
    }

    [CloudFact]
    public void 印とキャッシュだけのサムネイルは取り込みを起こさない()
    {
        var targets = Directory.EnumerateFileSystemEntries(Dir!)
            .Where(p => CloudFiles.IsPlaceholder(File.GetAttributes(p))).ToList();
        Assert.NotEmpty(targets);   // オンラインのみの項目を置き忘れていないか
        foreach (var path in targets)
        {
            var before = File.GetAttributes(path);
            Assert.NotEqual(0, ShellOverlays.IndexOf(path));   // 同期状態の印
            ShellThumbnails.Get(path, 256, cacheOnly: true)?.Dispose();
            Thread.Sleep(500);   // 取り込みは非同期に始まるので少し待つ
            Assert.Equal(before, File.GetAttributes(path));
        }
    }
}
