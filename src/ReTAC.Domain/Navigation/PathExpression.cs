using System.IO;
using System.Text;

namespace ReTAC.Domain.Navigation;

/// <summary>
/// 式に書く部品 1 つ。式の断片（利用者が書いた式・テンプレートの地の文字）か、文字の値（実パス・マクロの値）。
/// </summary>
public readonly record struct PathPart(string Text, bool IsExpression)
{
    public static PathPart Expression(string text) => new(text, true);
    public static PathPart Value(string text) => new(text, false);
}

/// <summary>
/// R-147: パスの欄の文字・設定ファイルに保存した値（式）と、実パス・文字の値との行き来。
/// 式から実パスへは <see cref="Expand"/>、実パスや文字の値から式へは <see cref="Write"/> だけで移る。
/// </summary>
public static class PathExpression
{
    /// <summary>
    /// 式の置き換え。環境変数は <paramref name="env"/>（名前 → 値。無ければ null。大文字・小文字は呼び出し側の辞書が決める）。
    /// </summary>
    public static string Expand(string text, Func<string, string?> env)
    {
        return string.Concat(Tokenize(text, env).Select(t => t.Text));
    }

    /// <summary>
    /// 部品の並びから式を 1 つ書く。判断は並び全体で行う（部品ごとに書いて連結すると、
    /// `%P32` と `.a%` のように別々では変数にならない文字が、つないだとき `%P32.a%` という変数の形になる）。
    /// </summary>
    public static string Write(IReadOnlyList<PathPart> parts, Func<string, string?> env)
    {
        var tokens = new List<Token>();
        foreach (var p in parts)
        {
            if (p.IsExpression) tokens.AddRange(Tokenize(p.Text, env));
            else tokens.Add(new Token(p.Text, null));   // 文字の値は全体が「文字」
        }

        // 変数が無く文字の % が 1 つ以下なら、置き換えても変わらないので、普通のパスは見た目を変えない
        var percents = tokens.Where(t => t.Variable is null).Sum(t => t.Text.Count(c => c == '%'));
        var hasVariable = tokens.Any(t => t.Variable is not null);
        var sb = new StringBuilder();
        foreach (var t in tokens)
        {
            if (!hasVariable && percents <= 1) sb.Append(t.Text);
            else if (t.Variable is not null) sb.Append('%').Append(t.Variable).Append('%');
            else sb.Append(t.Text.Replace("%", "%%"));
        }
        return sb.ToString();
    }

    /// <summary>文字の値 1 つを式に書く（環境に依らない）。実パスを欄へ入れる・保存するときに使う。</summary>
    public static string Literal(string value) => Write([PathPart.Value(value)], static _ => null);

    /// <summary>利用者が書いた式 1 つの正規の形。保存した時点の意味（未定義の `%名前%` は文字）が、後で名前が定義されても変わらない。</summary>
    public static string Canonical(string text, Func<string, string?> env) => Write([PathPart.Expression(text)], env);

    /// <summary>設定ファイルに書く形。`%` が 2 つ以上の式だけ先頭に印 `|` を付ける（`|` は名前に使えないのでパスの先頭に現れない）。</summary>
    public static string Store(string expression) => CountPercent(expression) >= 2 ? Mark + expression : expression;

    /// <summary>
    /// 設定ファイルの値 → 式。印が無く `%` が 2 つ以上のものは Phase 32 より前の設定の値で、
    /// 新しい式と同じ文字から見分けられないので、どちらの意味でも使わない（null）。
    /// </summary>
    public static string? Read(string stored)
    {
        if (stored.StartsWith(Mark, StringComparison.Ordinal)) return stored[Mark.Length..];
        return CountPercent(stored) >= 2 ? null : stored;
    }

    /// <summary>画面に出す形。印を外す。旧設定の値はそのまま出す（使えない項目として一覧には残る）。</summary>
    public static string Display(string stored) =>
        stored.StartsWith(Mark, StringComparison.Ordinal) ? stored[Mark.Length..] : stored;

    /// <summary>
    /// ブックマーク・クイックアクセス用。保存した値から実パスを作る。
    /// 旧設定の値・置き換えて空になる値・絶対パスにならない値・無効な名前を含む値は null（カレントフォルダでは解決しない）。
    /// </summary>
    public static string? ResolveSaved(string stored, Func<string, string?> env)
    {
        var expression = Read(stored);
        if (expression is null) return null;
        try
        {
            var expanded = Expand(expression, env);
            if (InputText.TrimEdge(expanded).Length == 0 || !Path.IsPathFullyQualified(expanded)) return null;
            var full = Path.GetFullPath(expanded);
            return PathResolver.HasInvalidName(full) ? null : full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private const string Mark = "|";

    private static int CountPercent(string s) => s.Count(c => c == '%');

    /// <summary>Variable は定義されていた変数の名前（書かれたまま）。null なら Text は文字（置き換え後）。</summary>
    private readonly record struct Token(string Text, string? Variable);

    /// <summary>
    /// 式の断片を左から読んで、変数と文字に分ける。未定義の `%名前%` は丸ごと文字にして閉じの `%` の後から読む。
    /// 閉じの `%` を次の組の始まりに使うと（Windows の ExpandEnvironmentStrings）、保存した式の変数が別の PC で
    /// 未定義のとき、ほかの部分の読み方がずれる。`Environment.ExpandEnvironmentVariables` を使わない理由。
    /// </summary>
    private static IEnumerable<Token> Tokenize(string text, Func<string, string?> env)
    {
        var literal = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] != '%') { literal.Append(text[i++]); continue; }
            var close = text.IndexOf('%', i + 1);
            if (close < 0) { literal.Append(text, i, text.Length - i); break; }   // 対の無い %
            if (close == i + 1) { literal.Append('%'); i = close + 1; continue; }   // %%

            var name = text.Substring(i + 1, close - i - 1);
            var value = env(name);
            if (value is null) literal.Append(text, i, close - i + 1);   // 未定義: 丸ごと文字
            else
            {
                if (literal.Length > 0) { yield return new Token(literal.ToString(), null); literal.Clear(); }
                yield return new Token(value, name);   // 値の中の % はもう置き換えない
            }
            i = close + 1;
        }
        if (literal.Length > 0) yield return new Token(literal.ToString(), null);
    }
}
