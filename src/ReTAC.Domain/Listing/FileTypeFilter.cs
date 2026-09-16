using System.IO;
using ReTAC.Domain.Entries;

namespace ReTAC.Domain.Listing;

/// <summary>表示ファイルタイプの区分（16.4 節の 7 項目のうち、種類で分かれる 5 つ）。</summary>
public enum FileTypeKind { Folder, Program, Associated, Archive, Other }

/// <summary>
/// 表示するファイルタイプの設定（0x82FF）。16.4 節の 7 項目。現行はすべて ON。
/// 隠し・システムは属性で、それ以外は種類で決まる。
/// </summary>
public sealed class FileTypeFilter
{
    public bool Folders { get; set; } = true;
    public bool Programs { get; set; } = true;
    public bool Associated { get; set; } = true;
    public bool Archives { get; set; } = true;
    public bool Others { get; set; } = true;
    public bool SystemFiles { get; set; } = true;
    public bool HiddenFiles { get; set; } = true;

    private static readonly string[] ProgramExtensions = [".exe", ".com", ".bat", ".cmd", ".scr", ".pif", ".msi"];
    private static readonly string[] ArchiveExtensions =
        [".zip", ".lzh", ".lha", ".rar", ".7z", ".cab", ".tar", ".gz", ".bz2", ".xz", ".arj", ".ace"];

    /// <param name="hasAssociation">拡張子に関連付けがあるか。判定はシェルに聞くので呼び出し側が渡す</param>
    public static FileTypeKind Classify(Entry entry, bool hasAssociation)
    {
        if (entry.Kind == EntryKind.Folder) return FileTypeKind.Folder;
        var extension = entry.Extension.ToLowerInvariant();
        if (ProgramExtensions.Contains(extension)) return FileTypeKind.Program;
        if (ArchiveExtensions.Contains(extension)) return FileTypeKind.Archive;
        return hasAssociation ? FileTypeKind.Associated : FileTypeKind.Other;
    }

    /// <summary>R-04: 親フォルダ項目は常に表示する（絞り込みの対象外）。</summary>
    public bool Accepts(Entry entry, bool hasAssociation)
    {
        if (entry.IsParent) return true;

        // 属性による除外が先。隠しかつシステムのファイルはどちらかが OFF なら出さない
        if (!HiddenFiles && entry.Attributes.HasFlag(FileAttributes.Hidden)) return false;
        if (!SystemFiles && entry.Attributes.HasFlag(FileAttributes.System)) return false;

        return Classify(entry, hasAssociation) switch
        {
            FileTypeKind.Folder => Folders,
            FileTypeKind.Program => Programs,
            FileTypeKind.Associated => Associated,
            FileTypeKind.Archive => Archives,
            _ => Others,
        };
    }

    /// <summary>すべて ON か。既定の状態と同じなら列挙時に判定そのものを省ける。</summary>
    public bool AcceptsEverything =>
        Folders && Programs && Associated && Archives && Others && SystemFiles && HiddenFiles;
}
