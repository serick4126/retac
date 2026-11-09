using System.IO;

namespace ReTAC.Domain.Navigation;

/// <summary>R-61: コピー・移動の宛先入力欄が受け付ける相対パスを、カレントフォルダ基準で解決する。</summary>
public static class PathResolver
{
    private static readonly char[] Separators = ['\\', '/'];

    /// <summary>
    /// 解決結果の絶対パスを返す。解決できない場合は null。
    /// <paramref name="input"/> は欄の文字・保存した値（式）だけを渡す。実パスを渡すと 2 回置き換わる（R-147）。
    /// R-61-3: ドライブルートより上になる場合は無効な宛先として扱う。
    /// </summary>
    public static string? Resolve(string currentFolder, string input, Func<string, string?> env)
    {
        // 宛先の入力欄には何でも打てる。`|` や `C:`、260 字超で Path 系が投げるので、
        // 解決できないものはすべて「無効な宛先」= null に寄せる（呼び出し側は catch していない）
        try
        {
            var resolved = ResolveCore(currentFolder, input, env);
            return resolved is null || HasInvalidName(resolved) ? null : resolved;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();

    /// <summary>ルートより後ろの各要素に、名前として使えない文字（`|` `?` `:` `*` など）が混ざっていないか。</summary>
    internal static bool HasInvalidName(string path)
    {
        var root = Path.GetPathRoot(path);
        var rest = string.IsNullOrEmpty(root) ? path : path[root.Length..];
        return rest.Split(Separators).Any(segment => segment.AsSpan().IndexOfAny(InvalidNameChars) >= 0);
    }

    private static string? ResolveCore(string currentFolder, string input, Func<string, string?> env)
    {
        // 全角空白だけの入力は名前として意味があるので IsNullOrWhiteSpace では弾けない
        // R-127: 囲みの二重引用符も外す。パスを入れる欄はどれもここを通るので、ここ 1 か所で全部の欄に効く
        input = InputText.Unquote(input);
        if (input.Length == 0) return null;

        // R-147: 置き換えは引用符を外した後の 1 回だけ。結果の端は落とさない（全角空白の名前を残す）。
        // 空・ASCII の空白だけになった入力はここで無効にする（空欄の扱い R-63 に切り替わらないように）
        input = PathExpression.Expand(input, env);
        if (InputText.TrimEdge(input).Length == 0) return null;

        // 絶対パス・UNC は明示的な指定なのでそのまま採る（R-52-4 と同じ考え方）
        if (Path.IsPathRooted(input)) return Path.GetFullPath(input);

        var root = Path.GetPathRoot(currentFolder);
        if (string.IsNullOrEmpty(root)) return null;

        var parts = currentFolder[root.Length..]
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        foreach (var segment in input.Split(Separators))
        {
            if (segment.Length == 0 || segment == ".") continue;
            if (segment == "..")
            {
                if (parts.Count == 0) return null;   // R-61-3: ドライブルートを超える
                parts.RemoveAt(parts.Count - 1);
                continue;
            }
            parts.Add(segment);
        }

        return parts.Count == 0 ? root : Path.Combine(root, string.Join('\\', parts));
    }
}
