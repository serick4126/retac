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

        // V-15: Enum.TryParse は数値文字列も通す。打ち間違いが別のコマンドに化けないようにする
        if (text.Length == 0 || char.IsAsciiDigit(text[0]) || text[0] == '-') return null;
        return Enum.TryParse<CommandId>(text, out var command) ? new BuiltinTarget(command) : null;
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
