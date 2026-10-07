using ReTAC.Domain.Entries;

namespace ReTAC.Domain.Tools;

// R-71 / R-72 / R-136 / R-137: 対象の分類と値取得を両文法でそろえる。
internal static class MacroValues
{
    internal static bool IsFileFamily(MacroName name) =>
        name is MacroName.File or MacroName.FileBasenameNoExtension or MacroName.FileExtname;

    internal static bool IsPathFamily(MacroName name) =>
        name is MacroName.Path or MacroName.PathBase or MacroName.PathBasename or MacroName.PathBasenameNoExtension;

    internal static IReadOnlyList<Entry> Targets(MacroName name, MacroContext context) =>
        IsFileFamily(name) ? context.Targets.Where(e => e.Kind == EntryKind.File).ToList()
        : IsPathFamily(name) ? context.Targets : [];

    internal static string Value(MacroName name, Entry? target, MacroContext context)
    {
        string Form(string path) => context.PathForm is null ? path : context.PathForm(path);
        string PathValue(Entry? entry, bool withoutExtension)
        {
            if (entry is null) return "";
            var path = Form(entry.IsParent ? context.CurrentFolder : entry.FullPath);
            if (!withoutExtension || entry.Kind != EntryKind.File || entry.Extension.Length == 0) return path;
            // R-09 / R-137: .gitignore は短縮後にドットがなくても最終名を全部除く。
            var separator = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
            if (entry.BaseName.Length == 0) return path[..(separator + 1)];
            var dot = path.LastIndexOf('.');
            return dot > separator ? path[..dot] : path;
        }
        return name switch
        {
            MacroName.File => PathValue(target, false),
            MacroName.Path => PathValue(target, false),
            MacroName.PathBase => PathValue(target, true),
            MacroName.FileBasenameNoExtension => target?.BaseName ?? "",
            MacroName.FileExtname => target?.Extension ?? "",
            MacroName.CursorFile => context.Cursor is { Kind: EntryKind.File } file ? PathValue(file, false) : "",
            MacroName.CursorFileBasenameNoExtension => context.Cursor is { Kind: EntryKind.File } file ? file.BaseName : "",
            MacroName.CursorPath => PathValue(context.Cursor, false),
            MacroName.CursorPathBase => PathValue(context.Cursor, true),
            MacroName.PathBasename => NameValue(target, false),
            MacroName.PathBasenameNoExtension => NameValue(target, true),
            MacroName.CursorPathBasename => NameValue(context.Cursor, false),
            MacroName.CursorPathBasenameNoExtension => NameValue(context.Cursor, true),
            MacroName.Cwd => Form(context.CurrentFolder),
            MacroName.CwdBasename => LastPart(context.CurrentFolder),
            _ => "",
        };
    }

    // R-144: .\ を付けて相対パスにする用途なので、カレントフォルダを指す「..」は空にする（${path} と違う）。
    // カレントフォルダの外の対象（R-97-3 のツリーの対象）でも名前だけを返す。8.3 形式の短縮は名前に掛けない。
    private static string NameValue(Entry? entry, bool withoutExtension) =>
        entry is null || entry.IsParent ? ""
        : withoutExtension && entry.Kind == EntryKind.File ? entry.BaseName
        : entry.Name;

    private static string LastPart(string path)
    {
        path = path.TrimEnd('\\', '/');
        return path[(Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/')) + 1)..];
    }
}
