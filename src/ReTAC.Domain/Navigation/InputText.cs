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
}
