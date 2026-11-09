using System.Text;
using ReTAC.Domain.Navigation;

namespace ReTAC.Domain.Tools;

/// <summary>R-136: 初期値は引数へ分割せず、各マクロの先頭の値を埋める。</summary>
public sealed class ScalarTemplate
{
    private ScalarTemplate(IReadOnlyList<TemplatePart> parts, IReadOnlyList<TemplateError> errors)
    { Parts = parts; Errors = errors; }

    public IReadOnlyList<TemplatePart> Parts { get; }
    public IReadOnlyList<TemplateError> Errors { get; }
    public bool IsValid => Errors.Count == 0;

    public static ScalarTemplate Parse(string text)
    {
        var parsed = TemplateParser.Parse(text, TemplateMode.Scalar);
        return new(parsed.Arguments[0].Parts, parsed.Errors);
    }

    public string? Expand(MacroContext context, out MacroName? emptyRequired) =>
        ExpandParts(context, out emptyRequired) is { } parts ? string.Concat(parts.Select(p => p.Text)) : null;

    /// <summary>
    /// R-136 / R-147: 展開した結果を、地の文字（式の断片）とマクロの値（文字の値）の並びで返す。
    /// フォルダ・ファイルの項目の欄へは、これを <see cref="PathExpression.Write"/> で 1 回だけ式に書いて入れる
    /// （部分ごとに書いてつなぐと、`%P32` と `.a%` のように別々では変数にならない文字が変数の形になる）。
    /// </summary>
    public IReadOnlyList<PathPart>? ExpandParts(MacroContext context, out MacroName? emptyRequired)
    {
        emptyRequired = null;
        if (!IsValid) return null;
        context = context with { PathForm = null, Prompt = null };
        var result = new List<PathPart>();
        foreach (var part in Parts)
        {
            if (part is LiteralPart literal) { result.Add(PathPart.Expression(literal.Text)); continue; }
            var macro = (MacroPart)part;
            var value = MacroValues.Value(macro.Name, MacroValues.Targets(macro.Name, context).FirstOrDefault(), context);
            if (value.Length == 0 && macro.Required) { emptyRequired = macro.Name; return null; }
            result.Add(PathPart.Value(value));
        }
        return result;
    }

    /// <summary>
    /// R-147: フォルダ・ファイルの項目の初期値のテンプレートの正規の形。地の文字の断片ごとに式の正規の形にして、マクロは書き直す。
    /// 全体を 1 つの式として正規にすると、マクロの記号（`${`・`$$`）を式の文字として読んで壊す。
    /// 誤りのあるテンプレートは直さずそのまま返す（誤りの知らせは検証が行う）。
    /// </summary>
    public static string Canonical(string text, Func<string, string?> env)
    {
        var template = Parse(text);
        if (!template.IsValid) return text;
        var sb = new StringBuilder();
        foreach (var part in template.Parts)
        {
            if (part is LiteralPart literal) sb.Append(PromptImport.EscapeMacroLiteral(PathExpression.Canonical(literal.Text, env)));
            else
            {
                var macro = (MacroPart)part;
                sb.Append("${").Append(ArgumentTemplate.Names.First(n => n.Value == macro.Name).Key).Append('}');
                if (macro.Required) sb.Append('!');
            }
        }
        return sb.ToString();
    }
}
