using System.Security.Cryptography;

namespace ReTAC.Domain.Tests;

/// <summary>前後の記録を比べた結果。増えた・消えた・変わったものの一覧</summary>
internal sealed record TreeDiff(IReadOnlyList<string> Created, IReadOnlyList<string> Removed, IReadOnlyList<string> Changed)
{
    public bool IsEmpty => Created.Count == 0 && Removed.Count == 0 && Changed.Count == 0;

    public override string ToString() => IsEmpty ? "PASS"
        : $"FAIL created=[{string.Join(", ", Created)}] removed=[{string.Join(", ", Removed)}] changed=[{string.Join(", ", Changed)}]";
}

/// <summary>
/// R-147 / S8: 実 OS のテストの見張り。見張る根の下の<b>ファイル（中身のハッシュ・属性）とフォルダ（有無・属性。根そのものも含む）</b>を記録して比べる。
/// 書庫の調査の見張り（OutsideWriteCheck）はファイルしか記録せず、空のフォルダが増える・消える・属性だけが変わるのを見逃すので、別に持つ。
/// 読むだけで、何も作らない・変えない・消さない。
/// </summary>
internal static class TreeWatch
{
    public static string DirRecord(FileAttributes attributes) => $"D {attributes}";

    public static string FileRecord(byte[] content, FileAttributes attributes) =>
        $"F {Convert.ToHexString(SHA256.HashData(content))} {attributes}";

    /// <summary>根（含む）の下のすべてのフォルダとファイルの記録（パス → 記録）</summary>
    public static Dictionary<string, string> Snapshot(IEnumerable<string> roots)
    {
        var records = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            records[root] = DirRecord(File.GetAttributes(root));
            foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
            {
                var attributes = File.GetAttributes(path);
                records[path] = attributes.HasFlag(FileAttributes.Directory) ? DirRecord(attributes) : FileRecord(File.ReadAllBytes(path), attributes);
            }
        }
        return records;
    }

    /// <summary>
    /// 前後を比べる。正しい宛先の<b>中</b>（allowedDestination の配下。コピーで変わってよい）は比べない。
    /// 宛先のフォルダ自体は比べる（有無・属性）。正しい宛先にコピーされたことは、呼び出し側が別に確かめる（陽性対照）。
    /// </summary>
    public static TreeDiff Compare(IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after, string allowedDestination)
    {
        var inside = Path.TrimEndingDirectorySeparator(allowedDestination) + Path.DirectorySeparatorChar;
        bool Watched(string path) => !path.StartsWith(inside, StringComparison.OrdinalIgnoreCase);
        var created = after.Keys.Where(k => Watched(k) && !before.ContainsKey(k)).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var removed = before.Keys.Where(k => Watched(k) && !after.ContainsKey(k)).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var changed = before.Where(p => Watched(p.Key) && after.TryGetValue(p.Key, out var now) && now != p.Value)
            .Select(p => p.Key).Order(StringComparer.OrdinalIgnoreCase).ToList();
        return new TreeDiff(created, removed, changed);
    }
}
