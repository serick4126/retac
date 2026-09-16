using System.Text;
using ReTAC.Domain.Entries;

namespace ReTAC.Domain.Tools;

/// <param name="Targets">渡す対象（表示順）。カーソルが <c>..</c> のときは親フォルダ項目を含む</param>
/// <param name="PromptAnswers">入力ダイアログで入力された値。<see cref="ArgumentTemplate.Prompts"/> と同じ順</param>
/// <param name="PathForm">パスの値に掛ける変換。長いパスを 8.3 形式にするために App が渡す</param>
public sealed record MacroContext(
    IReadOnlyList<Entry> Targets,
    Entry? Cursor,
    string CurrentFolder,
    IReadOnlyList<string> PromptAnswers,
    Func<string, string>? PathForm = null);

/// <summary>引数の展開（F-02 / R-71 / R-72）。</summary>
public static class ArgumentExpander
{
    /// <returns>展開した引数。<c>!</c> の付いたマクロが空になったら null（ツールを起動しない）</returns>
    public static IReadOnlyList<string>? Expand(ArgumentTemplate template, MacroContext context)
    {
        var files = context.Targets.Where(t => t.Kind == EntryKind.File).ToList();
        var result = new List<string>();

        foreach (var argument in template.Arguments)
        {
            var macros = argument.Parts.OfType<MacroPart>().ToList();
            // 対象の数だけ引数を作るマクロ。解析で ${path} と ${file} 系の混在は弾いてある
            IReadOnlyList<Entry>? list =
                macros.Any(m => ArgumentTemplate.IsFileFamily(m.Name)) ? files
                : macros.Any(m => m.Name == MacroName.Path) ? context.Targets
                : null;

            var count = list is { Count: > 0 } ? list.Count : 1;
            for (var k = 0; k < count; k++)
            {
                var item = list is { Count: > 0 } ? list[k] : null;
                var text = new StringBuilder();
                var empty = false;

                foreach (var part in argument.Parts)
                {
                    if (part is LiteralPart literal)
                    {
                        text.Append(literal.Text);
                        continue;
                    }

                    var macro = (MacroPart)part;
                    var value = Value(macro, item, context);
                    if (value.Length == 0)
                    {
                        if (macro.Required) return null;
                        empty = true;
                    }
                    text.Append(value);
                }

                // 引用符が無ければ空のマクロを含む引数ごと取り除く。あれば "" として残す
                if (empty && !argument.Quoted) continue;
                result.Add(text.ToString());
            }
        }
        return result;
    }

    private static string Value(MacroPart macro, Entry? item, MacroContext context)
    {
        string Form(string path) => context.PathForm is null ? path : context.PathForm(path);

        return macro.Name switch
        {
            MacroName.File => item is null ? "" : Form(item.FullPath),
            MacroName.FileBasenameNoExtension => item?.BaseName ?? "",
            MacroName.FileExtname => item?.Extension ?? "",
            MacroName.Path => item switch
            {
                null => "",
                { IsParent: true } => Form(context.CurrentFolder),
                { } entry => Form(entry.FullPath),
            },
            MacroName.CursorFile => context.Cursor is { Kind: EntryKind.File } file ? Form(file.FullPath) : "",
            MacroName.CursorPath => context.Cursor switch
            {
                null => "",
                { IsParent: true } => Form(context.CurrentFolder),
                { } cursor => Form(cursor.FullPath),
            },
            MacroName.Cwd => Form(context.CurrentFolder),
            MacroName.Prompt => macro.PromptIndex >= 0 && macro.PromptIndex < context.PromptAnswers.Count
                ? context.PromptAnswers[macro.PromptIndex]
                : "",
            _ => "",
        };
    }
}
