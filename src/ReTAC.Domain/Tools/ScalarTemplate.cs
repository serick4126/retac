using System.Text;

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

    public string? Expand(MacroContext context, out MacroName? emptyRequired)
    {
        emptyRequired = null;
        if (!IsValid) return null;
        context = context with { PathForm = null, Prompt = null };
        var result = new StringBuilder();
        foreach (var part in Parts)
        {
            if (part is LiteralPart literal) { result.Append(literal.Text); continue; }
            var macro = (MacroPart)part;
            var value = MacroValues.Value(macro.Name, MacroValues.Targets(macro.Name, context).FirstOrDefault(), context);
            if (value.Length == 0 && macro.Required) { emptyRequired = macro.Name; return null; }
            result.Append(value);
        }
        return result.ToString();
    }
}
