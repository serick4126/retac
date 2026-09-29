using System.Collections.Concurrent;
using Microsoft.Win32;

namespace ReTAC.Shell;

/// <summary>
/// R-01-7 / Q20: 拡張子が OS に登録されているか（エクスプローラーの「登録されている拡張子は表示しない」と同じ見方）。
/// 描画のたびに引くので拡張子ごとにアプリ全体で 1 度だけ調べる。列挙は Task.Run 上でも走るので ConcurrentDictionary。
/// </summary>
public static class RegisteredExtensions
{
    private static readonly ConcurrentDictionary<string, bool> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="extension">".xlsx" の形。大文字小文字は区別しない。空は常に未登録。</param>
    public static bool IsRegistered(string extension) =>
        extension.Length > 0 && Cache.GetOrAdd(extension, Query);

    private static bool Query(string extension)
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(extension);
            if (key is null) return false;
            if (key.GetValue(null) is string progId && progId.Length > 0) return true;
            using var withProgIds = key.OpenSubKey("OpenWithProgids");
            // OpenWithProgids の中身は実際には値（名前が ProgID）。サブキーの形もあるので両方見る
            return withProgIds is not null && (withProgIds.SubKeyCount > 0 || withProgIds.ValueCount > 0);
        }
        catch (Exception)
        {
            return false;   // アクセス拒否などは未登録として扱う（拡張子を隠さない側に倒す）
        }
    }
}
