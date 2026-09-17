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

    private static bool SameDrive(string a, string b) =>
        PathEquals(Path.GetPathRoot(a), Path.GetPathRoot(b));

    private static bool PathEquals(string? a, string? b) =>
        a is not null && b is not null
        && string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b),
            StringComparison.OrdinalIgnoreCase);
}
