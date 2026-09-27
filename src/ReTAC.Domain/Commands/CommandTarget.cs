using System.Globalization;

namespace ReTAC.Domain.Commands;

/// <summary>
/// キーやメニューが指す先（F-06）。組み込みのコマンドか、登録した外部ツール。
/// <b>後から種類を足せるように</b>、2 択の型（bool や enum）ではなく派生の record で表す。
/// 設定ファイルには <see cref="Serialize"/> の文字列で書く。
/// </summary>
public abstract record CommandTarget
{
    public abstract string Serialize();

    /// <returns>読めなければ null（割り当てなしとして扱う。移行はしない・S-12）</returns>
    public static CommandTarget? Parse(string text)
    {
        if (text.StartsWith(ToolTarget.Prefix, StringComparison.Ordinal))
        {
            var number = text[ToolTarget.Prefix.Length..];
            return int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
                ? new ToolTarget(id)
                : null;
        }

        // V-15 / P11-1: 宣言済みの名前 1 つと完全に一致するときだけ受理する。Enum.TryParse は数値文字列（先頭の空白も許す）と
        // "A,B" の複合値も通し、複合値は OR した値になる。OpenFile,Delete は Delete と同じ値なので、解析後の値に
        // IsDefined を当てても見分けられない。打ち間違いが別のコマンドに化けないよう、名前で確かめる
        if (!Enum.IsDefined(typeof(CommandId), text)) return null;
        return new BuiltinTarget(Enum.Parse<CommandId>(text));
    }
}

public sealed record BuiltinTarget(CommandId Command) : CommandTarget
{
    public override string Serialize() => Command.ToString();
}

/// <param name="ToolId"><see cref="Tools.ExternalTool.Id"/></param>
public sealed record ToolTarget(int ToolId) : CommandTarget
{
    public const string Prefix = "Tool:";

    public override string Serialize() => Prefix + ToolId.ToString(CultureInfo.InvariantCulture);
}
