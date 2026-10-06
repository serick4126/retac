using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>
/// B-04: 1 打鍵がコマンドの自前描画コントロール（ファイルリスト・ドライブバー）から IME を切り離す。
/// ImeMode だけでは効かない。WinForms が ImeMode を実際に適用するのは CanEnableIme を override した
/// TextBoxBase / ComboBox などに限られ、Control 直系のコントロールでは値を持つだけで IME コンテキストに触れない。
/// ハンドル再生成のたびに OnHandleCreated から呼ぶ。
/// </summary>
internal static class ImeBlock
{
    internal static void Detach(Control control) => ImmAssociateContext(control.Handle, IntPtr.Zero);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("imm32.dll")]
    private static extern IntPtr ImmAssociateContext(IntPtr window, IntPtr context);
}
