using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>
/// R-143 / B-04: 1 打鍵がコマンドの自前描画コントロール（ファイルリスト・ドライブバー）から IME を切り離す。
/// ImeMode だけでは効かない。WinForms が ImeMode を実際に適用するのは CanEnableIme を override した
/// TextBoxBase / ComboBox などに限られ、Control 直系のコントロールでは値を持つだけで IME コンテキストに触れない。
/// ハンドル再生成のたびに OnHandleCreated から呼ぶ。
/// </summary>
internal static class ImeBlock
{
    internal static void Detach(Control control) => ImmAssociateContext(control.Handle, IntPtr.Zero);

    /// <summary>
    /// R-143-1: 入力欄を持つダイアログに、ダイアログ専用の入力コンテキストを持たせる。
    /// 既定では UI スレッドのすべての入力欄が 1 つのコンテキストを共有するため、IME が変換中のまま固まると、
    /// 閉じて開き直したダイアログにも変換中の文字が残り、プロセスを終えるまで入力できなくなる（実機で起きた）。
    /// 専用のコンテキストはダイアログを閉じると捨てるので、固まりはそのダイアログの中で終わる。
    /// 子の窓は親のコンテキストを受け継がないので、子のハンドルが作られるたびに付ける
    /// （設定のページや表の編集欄は、ダイアログを開いた後に作られる）。
    /// </summary>
    internal static void OwnContext(Form dialog)
    {
        var context = IntPtr.Zero;
        void Associate(Control control)
        {
            if (context != IntPtr.Zero) ImmAssociateContextEx(control.Handle, context, IACE_CHILDREN);
        }
        void Track(Control control)
        {
            control.HandleCreated += (_, _) => Associate(control);
            control.ControlAdded += (_, e) =>
            {
                Track(e.Control!);
                if (e.Control!.IsHandleCreated) Associate(e.Control);
            };
            foreach (Control child in control.Controls) Track(child);
        }

        dialog.HandleCreated += (_, _) =>
        {
            context = ImmCreateContext();
            Associate(dialog);
        };
        dialog.HandleDestroyed += (_, _) =>
        {
            if (context == IntPtr.Zero) return;
            ImmAssociateContextEx(dialog.Handle, IntPtr.Zero, IACE_CHILDREN | IACE_DEFAULT);
            ImmDestroyContext(context);
            context = IntPtr.Zero;
        };
        foreach (Control child in dialog.Controls) Track(child);
        dialog.ControlAdded += (_, e) => Track(e.Control!);
    }

    private const uint IACE_CHILDREN = 0x1, IACE_DEFAULT = 0x10;

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("imm32.dll")]
    private static extern IntPtr ImmAssociateContext(IntPtr window, IntPtr context);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("imm32.dll")]
    private static extern bool ImmAssociateContextEx(IntPtr window, IntPtr context, uint flags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("imm32.dll")]
    private static extern IntPtr ImmCreateContext();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("imm32.dll")]
    private static extern bool ImmDestroyContext(IntPtr context);
}
