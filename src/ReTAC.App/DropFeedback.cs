using System.IO;
using System.Windows.Forms;
using ReTAC.Domain.FileOps;
using ReTAC.Domain.Listing;

namespace ReTAC.App;

/// <summary>
/// R-78: ドロップを受ける側の表示。効果（カーソルの形）と、カーソルの横の説明（「◯◯へ移動」）を同じ判定で決める。
/// 説明が出るのはドラッグ元が画像付きのときだけ（エクスプローラーと、R-78 以降の ReTAC のファイルリスト）。
/// </summary>
internal static class DropFeedback
{
    private const int CtrlKey = 8, ShiftKey = 4;

    /// <param name="destination">落とす先のフォルダ。落とせない場所なら null</param>
    /// <param name="label">説明の %1 に入れる名前</param>
    public static void Apply(DragEventArgs e, string? destination, string label)
    {
        var action = DropAction.None;
        // 判定は先頭の項目で行う（エクスプローラーと同じ）。実際の転送は項目ごとに判定する
        if (destination is { Length: > 0 } && e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            action = DropRules.Allow(
                DropRules.Decide(files[0], destination, (e.KeyState & CtrlKey) != 0, (e.KeyState & ShiftKey) != 0),
                copyAllowed: e.AllowedEffect.HasFlag(DragDropEffects.Copy),
                moveAllowed: e.AllowedEffect.HasFlag(DragDropEffects.Move));
        }

        (e.Effect, e.DropImageType, e.Message) = action switch
        {
            DropAction.Copy => (DragDropEffects.Copy, DropImageType.Copy, "%1 へコピー"),
            DropAction.Move => (DragDropEffects.Move, DropImageType.Move, "%1 へ移動"),
            _ => (DragDropEffects.None, DropImageType.None, ""),
        };
        e.MessageReplacementToken = label;
    }

    /// <summary>説明に出すフォルダの名前。ドライブのルートは「C:」。</summary>
    public static string FolderLabel(string folder) =>
        FolderEnumerator.IsDriveRoot(folder)
            ? folder[..2]
            : Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
}
