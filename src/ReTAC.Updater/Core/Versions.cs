using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ReTAC.Updater.Core;

/// <summary>R-109-2: 版の読み取りと比較。「最新です」と「完了」は、同じく 2 つの exe の版で判定する。</summary>
public static class Versions
{
    private static readonly Regex Tag = new(@"^v(\d+)\.(\d+)\.(\d+)$", RegexOptions.CultureInvariant);

    /// <summary><c>vX.Y.Z</c> だけを受け付ける。それ以外は null（「リリースの版番号を読めません」）。</summary>
    public static Version? ParseTag(string? tag)
    {
        if (tag is null) return null;
        var m = Tag.Match(tag);
        if (!m.Success) return null;
        try { return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)); }
        catch (OverflowException) { return null; }
    }

    /// <summary>ファイルバージョンの先頭 3 つ。4 つめは比べない。ファイルが無い・版が無ければ null。</summary>
    public static Version? OfFile(string path)
    {
        if (!File.Exists(path)) return null;
        var info = FileVersionInfo.GetVersionInfo(path);
        if (info.FileMajorPart == 0 && info.FileMinorPart == 0 && info.FileBuildPart == 0) return null;
        return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
    }

    /// <summary>
    /// R-109-2: 「最新です」の判定。2 つの exe の古いほうが最新の版以上なら最新。
    /// アップデータがインストール先に無いとき（<paramref name="updater"/> が null）は ReTAC.exe だけで判定する。
    /// </summary>
    public static bool IsUpToDate(Version latest, Version reTac, Version? updater)
    {
        var oldest = updater is null || reTac <= updater ? reTac : updater;
        return oldest >= latest;
    }

    /// <summary>
    /// R-109-4: 親が結果を判定し直す（昇格したプロセスの結果が読めないとき）。書き換えられるファイルは根拠にしない。
    /// インストール先の実物のハッシュで決める。「完了」の条件は「最新です」の判定と同じく、2 つの exe が両方新しい版であること。
    /// </summary>
    /// <param name="before">昇格したプロセスを起動する前の、配布物の名前ごとの SHA-256（無いファイルは null）</param>
    /// <param name="after">終わった後の同じもの</param>
    public static ReplaceResult Rejudge(
        IReadOnlyDictionary<string, string?> before, IReadOnlyDictionary<string, string?> after,
        Version target, Version? reTacAfter, Version? updaterAfter)
    {
        var changed = Distribution.Names.Any(n => Get(before, n) != Get(after, n));
        if (!changed) return ReplaceResult.Unchanged;
        if (reTacAfter == target && updaterAfter == target) return ReplaceResult.Completed;
        return ReplaceResult.Partial;

        static string? Get(IReadOnlyDictionary<string, string?> d, string key) => d.TryGetValue(key, out var v) ? v : null;
    }
}
