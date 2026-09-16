using System.IO;
using ReTAC.Domain.Entries;

namespace ReTAC.Domain.Listing;

/// <summary>カレントフォルダ直下を列挙し、親フォルダ項目を先頭に付けてソートする。</summary>
public static class FolderEnumerator
{
    /// <summary>R-39: 階層の上限はカレントドライブのルートフォルダ。</summary>
    public static bool IsDriveRoot(string folder)
    {
        var root = Path.GetPathRoot(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar);
        return root is not null && PathEquals(root, folder);
    }

    /// <summary>R-39: ドライブルートでは null を返す（何も起きない）。</summary>
    public static string? ParentOf(string folder)
    {
        if (IsDriveRoot(folder)) return null;
        return Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder));
    }

    /// <param name="include">表示ファイルタイプの絞り込み（0x82FF）。null ならすべて表示する</param>
    public static IReadOnlyList<Entry> Enumerate(string folder, SortOrder order, Func<Entry, bool>? include = null)
    {
        var entries = new List<Entry>();

        if (ParentOf(folder) is { } parent)
            entries.Add(Entry.ForParent(parent));

        // EnumerateFileSystemInfos は FIND_DATA の内容をそのまま保持するため、
        // 属性・サイズ・日時の取得で追加の I/O が発生しない
        foreach (var info in new DirectoryInfo(folder).EnumerateFileSystemInfos())
        {
            try
            {
                var entry = info is FileInfo file
                    ? Entry.ForFile(file.FullName, file.Name, file.Attributes, file.Length, file.LastWriteTime)
                    : Entry.ForFolder(info.FullName, info.Name, info.Attributes, info.LastWriteTime);
                if (include is null || include(entry)) entries.Add(entry);
            }
            catch (IOException)
            {
                // 6 章: 取得できなかったエントリがあっても列挙は継続する
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return FolderListing.Sort(entries, order);
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(a),
            Path.TrimEndingDirectorySeparator(b),
            StringComparison.OrdinalIgnoreCase);
}
