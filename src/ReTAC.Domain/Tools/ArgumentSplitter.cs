using System.Text;

namespace ReTAC.Domain.Tools;

/// <summary>
/// 文字どおりの文字列を、引数欄と同じ区切りの規則で引数に分ける（R-130）。
/// 区切るのは半角空白とタブだけ（全角空白は名前の一部）。引用符で囲んだ所は区切らず、引用符そのものは外す。
/// マクロ・<c>!</c>・<c>$$</c> は解釈しない。入力ダイアログの「前に付ける」「後ろに付ける」・チェックボックスと選択肢の送る値・
/// インポートしたコマンドライン（R-134）に使う。ここに書いた文字は、書いたとおりに送られなければならない。
/// </summary>
public static class ArgumentSplitter
{
    public static bool IsSeparator(char c) => c is ' ' or '\t';

    /// <returns>区切った引数。引用符が閉じていなければ null</returns>
    public static IReadOnlyList<string>? Split(string text)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var started = false;
        var inQuote = false;
        foreach (var c in text)
        {
            if (c == '"')
            {
                inQuote = !inQuote;
                started = true;   // "" は空の引数になる（引数欄と同じ）
                continue;
            }
            if (!inQuote && IsSeparator(c))
            {
                if (started) result.Add(current.ToString());
                current.Clear();
                started = false;
                continue;
            }
            current.Append(c);
            started = true;
        }
        if (inQuote) return null;
        if (started) result.Add(current.ToString());
        return result;
    }

    /// <summary>
    /// 値を 1 つの引数として送れる形にする。空・空白かタブを含むなら引用符で囲む。
    /// 引用符の規則に逃がしの書き方が無いので、<c>"</c> を含む値は表せない（null）。
    /// </summary>
    public static string? QuoteOne(string value) =>
        value.Contains('"') ? null
        : value.Length == 0 || value.Any(IsSeparator) ? $"\"{value}\""
        : value;

    /// <summary>
    /// R-130: 前に付ける＋値＋後ろに付ける。値そのものは区切らない（空白を含むパスが割れない）。
    /// 「前に付ける」が空白で終わっていなければ最後のまとまりが値とつながり、
    /// 「後ろに付ける」が空白で始まっていなければ最初のまとまりが値とつながる。
    /// </summary>
    /// <param name="mergeBackslash">
    /// フォルダ・ファイルの項目。値の末尾の <c>\</c> と、つながる「後ろに付ける」の先頭の <c>\</c> を 1 つにする。
    /// <c>C:\</c> に <c>\</c> を付けても <c>C:\</c>（末尾の <c>\</c> を外して <c>C:</c> にすると、そのドライブのカレントフォルダを指してしまう）
    /// </param>
    /// <returns>引数の並び。どちらかの引用符が閉じていなければ null</returns>
    public static IReadOnlyList<string>? Join(string prefix, string value, string suffix, bool mergeBackslash)
    {
        if (Split(prefix) is not { } before || Split(suffix) is not { } after) return null;
        var head = before.ToList();
        var tail = after.ToList();
        var middle = value;

        if (head.Count > 0 && !IsSeparator(prefix[^1]))
        {
            middle = head[^1] + middle;
            head.RemoveAt(head.Count - 1);
        }
        if (tail.Count > 0 && !IsSeparator(suffix[0]))
        {
            var glue = tail[0];
            if (mergeBackslash && middle.EndsWith('\\') && glue.StartsWith('\\')) glue = glue[1..];
            middle += glue;
            tail.RemoveAt(0);
        }
        return [.. head, middle, .. tail];
    }
}
