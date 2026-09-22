using System.IO;

namespace ReTAC.Domain.FileOps;

/// <summary>ドロップで起こす操作。</summary>
public enum DropAction { None, Copy, Move }

/// <summary>
/// R-65: ドラッグ＆ドロップで「コピーになるか移動になるか」の判定。
/// Windows の作法に合わせる ── 同じドライブなら移動、別のドライブならコピー。
/// `Ctrl` はコピー、`Shift` は移動を強制する。利用者はこの挙動を他のアプリで身につけている。
/// </summary>
public static class DropRules
{
    public static DropAction Decide(string sourcePath, string destinationFolder, bool ctrl, bool shift)
    {
        if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(destinationFolder)) return DropAction.None;

        // 同じフォルダへのドロップは何も起こさない（自分自身の中へ落とした場合）
        if (PathEquals(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(sourcePath)), destinationFolder))
            return DropAction.None;

        // フォルダを自分自身の中へ落とすのも無効。
        // 同じ判定を C / M の宛先欄と Ctrl+V も通す必要があるので TransferGuards に置いてある
        if (TransferGuards.IsInsideOrSame(destinationFolder, sourcePath)) return DropAction.None;

        if (ctrl && !shift) return DropAction.Copy;
        if (shift && !ctrl) return DropAction.Move;

        return SameDrive(sourcePath, destinationFolder) ? DropAction.Move : DropAction.Copy;
    }

    /// <summary>
    /// R-97-3 / §9: ツリーへのドラッグ中の判定。パスを持たない項目(仮想項目)は転送先にしない。
    /// パスがあれば通常の Decide（自分自身・自分の子孫への判定を含む）と同じ。
    /// </summary>
    public static DropAction DecideForTree(string sourcePath, string? destinationPath, bool ctrl, bool shift) =>
        destinationPath is null ? DropAction.None : Decide(sourcePath, destinationPath, ctrl, shift);

    /// <summary>
    /// R-78: ドラッグ元が許す効果に合わせる。移動を許さないドラッグ元（コピーしか渡さないアプリ）から
    /// 同じドライブへ落とされたとき、規則どおり移動すると、ドラッグ元が想定しない形で元のファイルが消える。
    /// コピーを移動に格上げすることはしない。
    /// </summary>
    public static DropAction Allow(DropAction action, bool copyAllowed, bool moveAllowed) => action switch
    {
        DropAction.Move when moveAllowed => DropAction.Move,
        DropAction.Move or DropAction.Copy when copyAllowed => DropAction.Copy,
        _ => DropAction.None,
    };

    /// <summary>
    /// 落とされた項目を、コピーするものと移動するものに振り分ける（R-65 / R-78。ファイルリスト・ドライブバー・ブックマーク・パンくずで共通）。
    /// 項目ごとに <see cref="Decide"/> → <see cref="Allow"/> を通し、どちらでもないものは落とす。並びはドロップ元の順。
    /// 修飾キーはドロップの時点のものを渡すこと（後に回した処理の中で読むと、利用者はもうキーを離している）。
    /// </summary>
    public static (IReadOnlyList<string> Copies, IReadOnlyList<string> Moves) Split(
        IReadOnlyList<string> files, string destination, bool ctrl, bool shift, bool copyAllowed, bool moveAllowed)
    {
        var copies = new List<string>();
        var moves = new List<string>();
        foreach (var file in files)
        {
            switch (Allow(Decide(file, destination, ctrl, shift), copyAllowed, moveAllowed))
            {
                case DropAction.Copy: copies.Add(file); break;
                case DropAction.Move: moves.Add(file); break;
            }
        }
        return (copies, moves);
    }

    private static bool SameDrive(string a, string b) =>
        PathEquals(Path.GetPathRoot(a), Path.GetPathRoot(b));

    private static bool PathEquals(string? a, string? b) =>
        a is not null && b is not null
        && string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b),
            StringComparison.OrdinalIgnoreCase);
}
