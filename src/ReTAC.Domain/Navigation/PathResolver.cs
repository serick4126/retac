using System.IO;

namespace ReTAC.Domain.Navigation;

/// <summary>R-61: コピー・移動の宛先入力欄が受け付ける相対パスを、カレントフォルダ基準で解決する。</summary>
public static class PathResolver
{
    private static readonly char[] Separators = ['\\', '/'];

    /// <summary>
    /// 解決結果の絶対パスを返す。解決できない場合は null。
    /// R-61-3: ドライブルートより上になる場合は無効な宛先として扱う。
    /// </summary>
    public static string? Resolve(string currentFolder, string input)
    {
        // 宛先の入力欄には何でも打てる。`|` や `C:`、260 字超で Path 系が投げるので、
        // 解決できないものはすべて「無効な宛先」= null に寄せる（呼び出し側は catch していない）
        try
        {
            var resolved = ResolveCore(currentFolder, input);
            return resolved is null || HasInvalidName(resolved) ? null : resolved;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();

    /// <summary>ルートより後ろの各要素に、名前として使えない文字（`|` `?` `:` `*` など）が混ざっていないか。</summary>
    private static bool HasInvalidName(string path)
    {
        var root = Path.GetPathRoot(path);
        var rest = string.IsNullOrEmpty(root) ? path : path[root.Length..];
        return rest.Split(Separators).Any(segment => segment.AsSpan().IndexOfAny(InvalidNameChars) >= 0);
    }

    private static string? ResolveCore(string currentFolder, string input)
    {
        // 全角空白だけの入力は名前として意味があるので IsNullOrWhiteSpace では弾けない
        input = InputText.TrimEdge(input);
        if (input.Length == 0) return null;

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
