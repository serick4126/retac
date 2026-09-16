using ReTAC.Domain.Entries;

namespace ReTAC.Domain.Formatting;

/// <summary>`I`（ファイル名をコピー）の 3 形式（D-04 / scope §7.1）。</summary>
public enum NameFormat
{
    /// <summary>パス＋名前（0x83A9）。</summary>
    PathAndName,
    /// <summary>名前のみ（0x7FC4）。</summary>
    NameOnly,
    /// <summary>`/` 区切りのパス（0x9001）。WSL やスクリプトへそのまま貼れる。</summary>
    SlashPath,
}

public static class NameFormats
{
    /// <summary>R-57-2: 実効対象が複数あるときは改行区切りで並べる。</summary>
    public static string Format(IEnumerable<Entry> targets, NameFormat format) =>
        string.Join(Environment.NewLine, targets.Where(e => !e.IsParent).Select(e => Format(e, format)));

    public static string Format(Entry entry, NameFormat format) => format switch
    {
        NameFormat.NameOnly => entry.Name,
        NameFormat.SlashPath => entry.FullPath.Replace('\\', '/'),
        _ => entry.FullPath,
    };
}
