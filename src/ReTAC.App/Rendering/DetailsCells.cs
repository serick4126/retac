using ReTAC.Domain.Entries;
using ReTAC.Domain.Formatting;
using ReTAC.Domain.Listing;
using ReTAC.Shell;

namespace ReTAC.App.Rendering;

/// <summary>R-114: 詳細表示の列の見出しとセルの文字。</summary>
public static class DetailsCells
{
    public static string Header(DetailsColumn? column) => column switch
    {
        null => "名前",
        DetailsColumn.Extension => "拡張子",   // Q19
        DetailsColumn.Size => "サイズ",
        DetailsColumn.Modified => "更新日時",
        DetailsColumn.Created => "作成日時",
        DetailsColumn.Type => "種類",
        DetailsColumn.Attributes => "属性",
        _ => "",
    };

    /// <summary>サイズだけ右寄せ。</summary>
    public static bool RightAligned(DetailsColumn? column) => column == DetailsColumn.Size;

    /// <summary>親フォルダの行は名前以外を空欄。フォルダのサイズは空欄（R-34）。種類は届くまで空欄で、背景へ頼む。</summary>
    public static string Text(Entry entry, DetailsColumn column)
    {
        if (entry.IsParent) return "";
        switch (column)
        {
            case DetailsColumn.Extension: return entry.Extension;
            case DetailsColumn.Size: return entry.Kind == EntryKind.File ? Display.Size(entry.Size) : "";
            case DetailsColumn.Modified: return Display.Timestamp(entry.LastWriteTime);
            case DetailsColumn.Created: return Display.Timestamp(entry.CreationTime);
            case DetailsColumn.Attributes: return Display.Attributes(entry.Attributes);
            case DetailsColumn.Type:
                var isFolder = entry.Kind == EntryKind.Folder;
                if (ShellFileType.TryGetCached(entry.FullPath, isFolder, out var name)) return name;
                ShellFileType.Request(entry.FullPath, isFolder);
                return "";
            default: return "";
        }
    }
}
