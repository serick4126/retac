using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ReTAC.Updater.Core;

/// <summary>
/// R-109 / INV-UPDATER-ANY-MIX: 配布物の名前と、受け付ける zip の検査。
/// 配布物のうち実行されるのは 2 つの exe だけで、どちらも単独で動く。だからファイルごとに新旧が混ざっても使える。
/// DLL など「組で動くファイル」を足すとこの前提が崩れるので、ここに無い名前を含む zip は受け付けない。
/// </summary>
public static class Distribution
{
    /// <summary>配布物の名前。公式のリリースはこの 6 つをすべて含む（INV-RELEASE-EXACT-SET）。検査のスクリプトもここから読む。</summary>
    public static readonly IReadOnlyList<string> Names = new[]
    {
        Protocol.ReTacExe,
        Protocol.UpdaterExe,
        "LICENSE",
        "README.md",
        "DOTNET-LICENSE.txt",
        "DOTNET-ThirdPartyNotices.txt",
    };

    /// <summary>配布物の名前のうち、大文字小文字を区別せずに一致するものの正しい綴り。無ければ null。</summary>
    public static string? Canonical(string name) =>
        Names.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// R-109-2 / INV-UPDATER-ACCEPTS-SUBSET: zip の項目の名前（ZipArchiveEntry.FullName）を、書き出す前に全部検査する。
    /// 受け付けられれば null。
    /// </summary>
    /// <param name="stagingFolder">書き出す先の準備フォルダ。項目の出力先がこの直下であることを確かめる</param>
    public static Reason? Check(IEnumerable<string> entryNames, string stagingFolder)
    {
        var root = Path.GetFullPath(stagingFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entryNames)
        {
            // フォルダの項目（末尾が区切り）は配布物に無い
            if (entry.Length == 0 || entry.EndsWith("/", StringComparison.Ordinal) || entry.EndsWith("\\", StringComparison.Ordinal))
                return Reason.UnsupportedFormat;

            // 見た目（".." の有無）ではなく、正規化した絶対パスで準備フォルダの直下かを見る
            string full;
            try { full = Path.GetFullPath(Path.Combine(root, entry)); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return Reason.Corrupt; }
            if (!string.Equals(Path.GetDirectoryName(full), root, StringComparison.OrdinalIgnoreCase)) return Reason.Corrupt;

            var name = Path.GetFileName(full);
            if (Canonical(name) is null) return Reason.UnsupportedFormat;

            // 大文字小文字だけ違う項目も同じ出力先になる。後の項目が先の項目を上書きし、照合した中身と食い違う
            if (!seen.Add(name)) return Reason.UnsupportedFormat;
        }

        // 2 つの exe は両方要る。アップデータを欠くと、古いアップデータが残って同じ更新を勧め続ける
        if (!seen.Contains(Protocol.ReTacExe) || !seen.Contains(Protocol.UpdaterExe)) return Reason.UnsupportedFormat;
        return null;
    }

    /// <summary>R-109-4: 入れ替えの順。ReTAC.exe を最後にする（「ReTAC.exe が新版」が「すべて済んだ」を意味するように）。</summary>
    public static IReadOnlyList<string> ReplaceOrder(IEnumerable<string> names)
    {
        var list = names.ToList();
        var ordered = list.Where(n => !IsReTacExe(n)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        ordered.AddRange(list.Where(IsReTacExe));
        return ordered;
    }

    private static bool IsReTacExe(string name) => string.Equals(name, Protocol.ReTacExe, StringComparison.OrdinalIgnoreCase);
}
