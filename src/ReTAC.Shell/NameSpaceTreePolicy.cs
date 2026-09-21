namespace ReTAC.Shell;

/// <summary>R-97: 現在位置に追従する単一ルートの判定。</summary>
public static class NameSpaceTreePolicy
{
    public static string RootOf(string currentFolder) => ShellItemPath.RootOf(currentFolder);

    public static bool IsCurrentPathOrAncestor(string itemPath, string currentFolder)
    {
        var item = Normalize(itemPath);
        var current = Normalize(currentFolder);
        if (string.Equals(item, current, StringComparison.OrdinalIgnoreCase)) return true;

        var prefix = Path.EndsInDirectorySeparator(item) ? item : item + Path.DirectorySeparatorChar;
        return current.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool MustRebuildRoot(string oldRoot, string newRoot) =>
        !string.Equals(Normalize(oldRoot), Normalize(newRoot), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("フォルダの絶対パスが必要です。", nameof(path));
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
