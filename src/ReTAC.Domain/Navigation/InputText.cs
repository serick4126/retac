namespace ReTAC.Domain.Navigation;

/// <summary>
/// B-01: 入力欄から受け取ったパス・名前の端を落とす。
///
/// <c>string.Trim()</c> は Unicode の空白をすべて落とすため、<b>全角空白（U+3000）まで消える。</b>
/// 末尾に全角空白を持つフォルダは Windows 上に実在するので、これを落とすと
/// 「作成できない」「存在しないと判定される」という形で表に出る。
///
/// 落とす対象を ASCII の空白とタブに限る。Windows はファイル名の末尾に
/// ASCII 空白を許さないので、こちらは落とすのが正しい。
/// </summary>
public static class InputText
{
    private static readonly char[] Edge = [' ', '\t'];

    public static string TrimEdge(string value) => value.Trim(Edge);

    /// <summary>
    /// R-127: パスを入れる欄の文字列から、囲みの二重引用符を外す。端の空白を落としたあと、先頭と末尾の <c>"</c> を
    /// あればそれぞれ 1 つ外し（片方だけでも外す）、もう一度端の空白を落とす。
    /// Windows の「パスのコピー」はパスを二重引用符で囲む。<c>"</c> は名前に使えない文字なので、端にあれば必ず囲みである。
    /// 途中の引用符には触らない（複数のパスを 1 つとして読まない）。全角の引用符・一重引用符は名前に使えるので外さない。
    /// </summary>
    public static string Unquote(string value)
    {
        var text = TrimEdge(value);
        if (text.StartsWith('"')) text = text[1..];
        if (text.EndsWith('"')) text = text[..^1];
        return TrimEdge(text);
    }
}
