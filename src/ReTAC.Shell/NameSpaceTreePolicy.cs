namespace ReTAC.Shell;

/// <summary>R-97: ドライブツリー（現在のドライブ/共有 1 つ）とデスクトップツリー（PC 全体で単一・固定）の別。</summary>
public enum NameSpaceTreeRootKind { Drive, Desktop }

/// <summary>R-97: 現在位置に追従する単一ルートの判定。</summary>
public static class NameSpaceTreePolicy
{
    /// <summary>デスクトップツリーのルートは実パスを持たないので、ドライブの実ルートとは絶対に一致しない印にする。</summary>
    public const string DesktopRootMarker = "*desktop*";

    public static string RootOf(string currentFolder) => ShellItemPath.RootOf(currentFolder);

    /// <summary>R-97: デスクトップツリーは PC 全体で単一のルートを持ち、ドライブ・UNC共有が変わっても作り直さない
    /// （<see cref="MustRebuildRoot(string,string)"/> に同じ印を渡すだけで常に false になる）。</summary>
    public static string RootOf(NameSpaceTreeRootKind kind, string currentFolder) =>
        kind == NameSpaceTreeRootKind.Desktop ? DesktopRootMarker : RootOf(currentFolder);

    /// <summary>R-97: デスクトップツリーは This PC 配下をドライブ文字でしか辿れない
    /// （実機ゲートで確認済み。マップ済みネットワークドライブも同様）。UNC文字列の現在位置は
    /// ドライブ文字への変換までは行わず、自動選択の対象外とする。
    /// ponytail: 実機でUNC作業が多ければ、マップ済みドライブへの変換に対応する。</summary>
    public static bool CanAutoSelectInDesktopTree(string currentFolder) =>
        !currentFolder.StartsWith(@"\\", StringComparison.Ordinal);

    public static bool IsCurrentPathOrAncestor(string itemPath, string currentFolder)
    {
        var item = Normalize(itemPath);
        var current = Normalize(currentFolder);
        if (string.Equals(item, current, StringComparison.OrdinalIgnoreCase)) return true;

        var prefix = Path.EndsInDirectorySeparator(item) ? item : item + Path.DirectorySeparatorChar;
        return current.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>デスクトップツリーの印(DesktopRootMarker)は絶対パスではないので、まず生の文字列一致を
    /// 見る。実パスどうしの比較(表記ゆれの吸収)だけが Normalize を必要とする。</summary>
    public static bool MustRebuildRoot(string oldRoot, string newRoot) =>
        !string.Equals(oldRoot, newRoot, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(Normalize(oldRoot), Normalize(newRoot), StringComparison.OrdinalIgnoreCase);

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

    /// <summary>
    /// R-97-2: 現在位置への展開を失敗とみなすのは、見張りを始めてから、または最後に 1 段進んでから timeout たったとき。
    /// 全体の時間で切ると、中身の多いフォルダを何段も辿る途中で、正常に進んでいるのに失敗扱いになる。
    /// lastProgress が前の選択の時刻（見張りの開始より前）なら、見張りの開始から数える。
    /// </summary>
    internal static bool SelectionTimedOut(long now, long watchStarted, long lastProgress, long timeout) =>
        now >= Math.Max(watchStarted, lastProgress) + timeout;

    /// <summary>
    /// R-97-2: 自動展開で命じた枝が、interval たっても開いていなければもう一度命じる。
    /// デスクトップツリーでは、親が開き終わった直後に命じた展開が OnBeforeExpand の後に止まることがある
    /// （AppData\Local\Temp で実測。手でのクリックと同じ経路でも止まり、少し後に命じ直すと開く）。
    /// 間隔を空けるのは、中身の多い枝が普通に読み込み中のときに命じ直しを重ねないため。
    /// requestedAt が 0 なら命じていない（着いたとき既に開いていた）ので命じ直さない。
    /// alreadyReissued が true なら、この段への命じ直しは既に 1 回行った後なので命じ直さない
    /// （命じ直すたびに interval が延びると、止まったままの枝に見張りの期限まで送り続けてしまう）。
    /// </summary>
    internal static bool ShouldReissueExpand(bool expanded, long requestedAt, long now, long interval, bool alreadyReissued) =>
        !expanded && !alreadyReissued && requestedAt != 0 && now - requestedAt >= interval;

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("フォルダの絶対パスが必要です。", nameof(path));
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
