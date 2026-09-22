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

    /// <summary>R-97-2: マウスで確定する候補になったクリック。展開ボタン・余白は候補にしない時点で除外する。</summary>
    internal readonly record struct PendingTreeClick(string Path, Point DownPoint, bool OnIconOrLabel, bool IsDoubleClick);

    /// <summary>
    /// R-97-2: 名前・アイコンの左クリックを、ドラッグへ移行せず離した場合だけ確定する。
    /// ドラッグしきい値は FileListView 等と同じ SystemInformation.DragSize で揃える。
    /// ダブルクリックの2回目は最初のクリックで既に確定しているので、二重に確定させない。
    /// </summary>
    internal static bool ShouldCommit(PendingTreeClick pending, Point upPoint, bool dragStarted, bool buttonReleased)
    {
        if (!buttonReleased || dragStarted) return false;
        if (!pending.OnIconOrLabel || pending.IsDoubleClick) return false;
        return Math.Abs(upPoint.X - pending.DownPoint.X) < SystemInformation.DragSize.Width
            && Math.Abs(upPoint.Y - pending.DownPoint.Y) < SystemInformation.DragSize.Height;
    }

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("フォルダの絶対パスが必要です。", nameof(path));
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
