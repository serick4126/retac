using System.Security.Cryptography;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-146-1 / S6: 取り出し先の外が変わっていないかの判定。自動テストと実機調査のツール（同じファイルを取り込む）で共通に使う。
/// 判定そのものの誤り（正常を不合格にする・外への書き込みを見逃す）は OutsideWriteCheckTests が検出する。
/// </summary>
internal static class OutsideWriteCheck
{
    /// <summary>見張る場所にある、取り出し先（dest）の外のファイルと中身のハッシュ（見張りのファイルも含む）</summary>
    public static Dictionary<string, string> Snapshot(IEnumerable<string> roots, string dest) =>
        roots.SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            .Where(file => !file.StartsWith(Path.TrimEndingDirectorySeparator(dest) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(file => file, file => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))), StringComparer.OrdinalIgnoreCase);

    /// <summary>外に新しいファイルができた・既存のファイル（見張り）が変わった・消えたら FAIL。どれも無ければ PASS</summary>
    public static string Judge(IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after)
    {
        var created = after.Keys.Where(key => !before.ContainsKey(key)).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var changed = before.Where(pair => !after.TryGetValue(pair.Key, out var hash) || hash != pair.Value)
            .Select(pair => pair.Key).Order(StringComparer.OrdinalIgnoreCase).ToList();
        return created.Count == 0 && changed.Count == 0
            ? "PASS"
            : $"FAIL created=[{string.Join(", ", created)}] changed=[{string.Join(", ", changed)}]";
    }
}
