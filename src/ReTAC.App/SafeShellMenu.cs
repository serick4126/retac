using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ReTAC.Shell;

namespace ReTAC.App;

/// <summary>
/// V-04: シェルのメニューは、サードパーティのシェル拡張（WinRAR など）が ReTAC のプロセス内で走る。
/// 拡張が投げてきたものを ReTAC の落ちる理由にしない。6 章: エラーを提示し、本体は動き続ける。
/// シェルのメニューを出すところは、すべてここを通す。
/// </summary>
internal static class SafeShellMenu
{
    /// <returns>メニューの結果。失敗したら取り消し扱い</returns>
    public static ContextMenuResult Run(IWin32Window owner, Func<ContextMenuResult> show)
    {
        try { return show(); }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(owner, ex.Message, "ReTAC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return ContextMenuResult.Cancelled;
        }
    }

    public static void Run(IWin32Window owner, Action show) =>
        Run(owner, () => { show(); return ContextMenuResult.Cancelled; });
}
