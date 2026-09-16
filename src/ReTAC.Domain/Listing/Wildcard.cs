using System.Text;
using System.Text.RegularExpressions;

namespace ReTAC.Domain.Listing;

/// <summary>ワイルドカードで選択（R-22 / 0x8326）で使うパターン照合。大文字小文字を区別しない。</summary>
public static class Wildcard
{
    /// <summary>
    /// パターンごとに 1 回だけ組み立てて使い回す。
    /// 全件ループ（<c>ListState.MarkByWildcard</c>）から呼ばれるので、
    /// 1 件ごとに <c>new Regex</c> すると System32 の 5,000 件で 5,000 回組み立てることになる（V-11）。
    /// パターンは利用者が打つものなので、1 セッションで溜まる数は高が知れている。
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Regex> Cache = new();

    public static bool IsMatch(string name, string pattern)
    {
        // Windows の慣例に合わせ、*.* はピリオドの有無に関わらず全てに一致する
        if (pattern is "*" or "*.*") return true;
        return Cache.GetOrAdd(pattern, ToRegex).IsMatch(name);
    }

    private static Regex ToRegex(string pattern)
    {
        var sb = new StringBuilder("^");
        foreach (var ch in pattern)
        {
            sb.Append(ch switch
            {
                '*' => ".*",
                '?' => ".",
                _ => Regex.Escape(ch.ToString()),
            });
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
