namespace ReTAC.Domain.Tools;

/// <summary>外部ツールの引数に書けるマクロ（F-02）。名前は VS Code の変数に合わせる。</summary>
public enum MacroName
{
    File,
    Path,
    FileBasenameNoExtension,
    FileExtname,
    CursorFile,
    CursorPath,
    Cwd,
    Prompt,
    CwdBasename,
    CursorFileBasenameNoExtension,
    PathBase,
    CursorPathBase,
}

public abstract record TemplatePart;

public sealed record LiteralPart(string Text) : TemplatePart;

/// <param name="Required">直後の <c>!</c>。空になったらツールを起動しない</param>
public sealed record MacroPart(MacroName Name, bool Required) : TemplatePart;

/// <param name="Quoted">引用符を含んでいた。マクロが空になっても <c>""</c> として残す</param>
public sealed record TemplateArgument(IReadOnlyList<TemplatePart> Parts, bool Quoted);

public sealed record TemplateError(int Position, string Message);

/// <summary>
/// 外部ツールの引数の文字列を解析したもの（F-02 / R-71）。
/// <b>引数を区切ってから展開する</b>ので、展開した値に空白があっても 1 つの引数のまま渡る。
/// 知らない名前は誤りにする。文字どおり渡すと、後で同じ名前のマクロを足した瞬間に既存の設定の意味が変わる（F-02）。
/// </summary>
public sealed class ArgumentTemplate
{
    public static IReadOnlyDictionary<string, MacroName> Names { get; } = new Dictionary<string, MacroName>(StringComparer.Ordinal)
    {
        ["file"] = MacroName.File,
        ["path"] = MacroName.Path,
        ["fileBasenameNoExtension"] = MacroName.FileBasenameNoExtension,
        ["fileExtname"] = MacroName.FileExtname,
        ["cursorFile"] = MacroName.CursorFile,
        ["cursorPath"] = MacroName.CursorPath,
        ["cwd"] = MacroName.Cwd,
        ["prompt"] = MacroName.Prompt,
        ["cwdBasename"] = MacroName.CwdBasename,
        ["cursorFileBasenameNoExtension"] = MacroName.CursorFileBasenameNoExtension,
        ["pathBase"] = MacroName.PathBase,
        ["cursorPathBase"] = MacroName.CursorPathBase,
    };

    private ArgumentTemplate(IReadOnlyList<TemplateArgument> arguments, IReadOnlyList<TemplateError> errors)
    {
        Arguments = arguments;
        Errors = errors;
    }

    public IReadOnlyList<TemplateArgument> Arguments { get; }
    public IReadOnlyList<TemplateError> Errors { get; }
    public bool IsValid => Errors.Count == 0;

    /// <summary>R-130: <c>${prompt}</c> がある。入力ダイアログを出し、その位置に定義の引数の並びを展開する</summary>
    public bool HasPrompt => Arguments.Any(a => a.Parts.Any(p => p is MacroPart { Name: MacroName.Prompt }));

    /// <summary><c>${file}</c> と同じ対象（実効対象のファイル）から値を取るマクロ。</summary>
    public static bool IsFileFamily(MacroName name) =>
        MacroValues.IsFileFamily(name);

    public static ArgumentTemplate Parse(string text)
    {
        var parsed = TemplateParser.Parse(text, TemplateMode.Arguments);
        return new(parsed.Arguments, parsed.Errors);
    }

}
