using System.IO;

namespace ReTAC.Domain.Entries;

/// <summary>エントリの種別。Parent は親フォルダ項目（R-04）。</summary>
public enum EntryKind
{
    Parent,
    Folder,
    File,
}

/// <summary>ファイルリストの 1 行が表す対象。</summary>
public sealed record Entry
{
    public required string FullPath { get; init; }

    /// <summary>表示名（基底名 + 拡張子）。</summary>
    public required string Name { get; init; }

    /// <summary>R-09: 名前の最後のピリオドより前。フォルダは名前全体。</summary>
    public required string BaseName { get; init; }

    /// <summary>R-07: ピリオドを含む拡張子。フォルダは常に空。</summary>
    public required string Extension { get; init; }

    public required EntryKind Kind { get; init; }
    public required FileAttributes Attributes { get; init; }
    public required long Size { get; init; }
    public required DateTime LastWriteTime { get; init; }

    /// <summary>R-04: 親フォルダ項目はソート対象外・マーク対象外。</summary>
    public bool IsParent => Kind == EntryKind.Parent;

    public static Entry ForParent(string parentFullPath) => new()
    {
        FullPath = parentFullPath,
        Name = "..",
        BaseName = "..",
        Extension = "",
        Kind = EntryKind.Parent,
        Attributes = FileAttributes.Directory,
        Size = 0,
        LastWriteTime = default,
    };

    public static Entry ForFolder(string fullPath, string name, FileAttributes attributes, DateTime lastWriteTime) => new()
    {
        FullPath = fullPath,
        Name = name,
        BaseName = name,   // R-07: フォルダは拡張子を分離しない
        Extension = "",
        Kind = EntryKind.Folder,
        Attributes = attributes,
        Size = 0,
        LastWriteTime = lastWriteTime,
    };

    public static Entry ForFile(string fullPath, string name, FileAttributes attributes, long size, DateTime lastWriteTime)
    {
        var (baseName, extension) = SplitName(name);
        return new Entry
        {
            FullPath = fullPath,
            Name = name,
            BaseName = baseName,
            Extension = extension,
            Kind = EntryKind.File,
            Attributes = attributes,
            Size = size,
            LastWriteTime = lastWriteTime,
        };
    }

    /// <summary>R-09: 最後のピリオド以降を拡張子とする。<c>.gitignore</c> は基底名が空。</summary>
    public static (string BaseName, string Extension) SplitName(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot < 0 ? (name, "") : (name[..dot], name[dot..]);
    }
}
