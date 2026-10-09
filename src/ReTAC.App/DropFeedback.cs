using System.IO;
using System.Windows.Forms;
using ReTAC.Domain.FileOps;
using ReTAC.Domain.Listing;
using ReTAC.Domain.Navigation;
using ReTAC.Shell;

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
    /// <param name="mode">R-140: ブックマークのフォルダの指定。ブックマーク以外の受け口は Standard。右ボタンのドラッグでは使わない</param>
    public static void Apply(DragEventArgs e, string? destination, string label, BookmarkDropMode mode = BookmarkDropMode.Standard) =>
        Apply(e, destination, label, mode, DragButtonState.Right);

    /// <summary>右ボタンの状態を引数で受ける版（ハンドル無しのテスト用）。</summary>
    internal static void Apply(DragEventArgs e, string? destination, string label, BookmarkDropMode mode, bool right)
    {
        if (destination is { Length: > 0 } && DropData.HasVirtual(e.Data))
        {
            // R-146-1 / S1: 書庫の中などの項目はコピーだけ（修飾キー・指定・ボタンにかかわらない）。移動を返すとドラッグ元が書庫から消す
            var copy = DropRules.DecideVirtual(e.AllowedEffect.HasFlag(DragDropEffects.Copy)) == DropAction.Copy;
            (e.Effect, e.DropImageType, e.Message) = copy
                ? (DragDropEffects.Copy, DropImageType.Copy, right ? "" : "%1 へコピー")
                : (DragDropEffects.None, DropImageType.None, "");
            e.MessageReplacementToken = label;
            return;
        }

        var action = DropAction.None;
        var files = destination is { Length: > 0 } ? e.Data?.GetData(DataFormats.FileDrop) as string[] : null;
        // 判定は先頭の項目で行う（エクスプローラーと同じ）。実際の転送は項目ごとに判定する
        if (files is { Length: > 0 })
        {
            // R-111-2 / R-140: 右ボタンは指定を使わない（ドロップ後のメニューの既定と同じ標準の判定にそろえる）
            action = LeftDecision(files, destination!, e.KeyState, e.AllowedEffect, right ? BookmarkDropMode.Standard : mode);
        }

        (e.Effect, e.DropImageType, e.Message) = action switch
        {
            DropAction.Copy => (DragDropEffects.Copy, DropImageType.Copy, "%1 へコピー"),
            DropAction.Move => (DragDropEffects.Move, DropImageType.Move, "%1 へ移動"),
            _ => (DragDropEffects.None, DropImageType.None, ""),
        };
        e.MessageReplacementToken = label;

        // R-111-2: 右ボタンのドラッグでは、左ボタンなら何も起きない所（別のドライブで移動だけ・リンクだけ）でも、
        // メニューが出る所なら落とせる効果を返す。None のままだと OLE は Drop を呼ばず、メニューが出ない。
        // 説明は出さない（何をするかはメニューで選ぶ）
        if (action == DropAction.None && right && files is { Length: > 0 }
            && DropRouting.Menu(files[0], destination!, e.AllowedEffect.HasFlag(DragDropEffects.Copy),
                e.AllowedEffect.HasFlag(DragDropEffects.Move), e.AllowedEffect.HasFlag(DragDropEffects.Link)) is { } model)
        {
            (e.Effect, e.DropImageType) = DropRouting.RightDragEffect(model) switch
            {
                DropChoice.Copy => (DragDropEffects.Copy, DropImageType.Copy),
                DropChoice.Move => (DragDropEffects.Move, DropImageType.Move),
                _ => (DragDropEffects.Link, DropImageType.Link),
            };
        }
    }

    /// <summary>左ボタンの判定（ハンドル無しで確かめられるよう切り出してある）。R-140: mode が Standard なら従来どおり。</summary>
    internal static DropAction LeftDecision(string[] files, string destination, int keyState, DragDropEffects allowed, BookmarkDropMode mode) =>
        DropRules.DecideFixed(mode, files[0], destination, (keyState & CtrlKey) != 0, (keyState & ShiftKey) != 0,
            copyAllowed: allowed.HasFlag(DragDropEffects.Copy), moveAllowed: allowed.HasFlag(DragDropEffects.Move));

    /// <summary>
    /// ドロップの時点の修飾キー。ToolStrip の受け口でも使えるよう KeyState から読む。
    /// 転送のダイアログは後に回すので、そのときの ModifierKeys では利用者がもうキーを離している。
    /// </summary>
    public static (bool Ctrl, bool Shift) Modifiers(DragEventArgs e) =>
        ((e.KeyState & CtrlKey) != 0, (e.KeyState & ShiftKey) != 0);

    /// <summary>説明に出すフォルダの名前。ドライブのルートは「C:」。</summary>
    public static string FolderLabel(string folder) =>
        FolderEnumerator.IsDriveRoot(folder)
            ? folder[..2]
            : Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
}
