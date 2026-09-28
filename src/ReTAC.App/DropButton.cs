using System.Windows.Forms;
using ReTAC.Shell;

namespace ReTAC.App;

/// <summary>
/// R-111-2 / T4: ReTAC の受け口が右ボタンのドラッグを覚える入口。受け口を足すときはここを通す（INV-RIGHT-DROP-SAME-ROUTE）。
/// </summary>
internal static class DropButton
{
    public static void Enter(DragEventArgs e) => DragButtonState.Enter((uint)e.KeyState);
    public static void Over(DragEventArgs e) => DragButtonState.Over((uint)e.KeyState);
    public static void Leave() => DragButtonState.Reset();

    /// <summary>
    /// 落としたときの処理を包む。右ボタンのドロップでは、ReTAC が自分で転送するので、ドラッグ元へ None を返して元のファイルを消させない（T6）。
    /// 後始末は finally で行う。処理が例外を投げても、印が次のドラッグへ残らず、直前の効果を返さない。
    /// </summary>
    public static void Drop(DragEventArgs e, Action handle)
    {
        var right = DragButtonState.Right;
        try { handle(); }
        finally
        {
            if (right) e.Effect = DragDropEffects.None;
            DragButtonState.Reset();
        }
    }
}
