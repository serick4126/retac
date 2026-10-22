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

        // R-143-1: 直前のダイアログの IME の状態（オン・オフと入力モード）を引き継ぐ。
        // 新しいコンテキストの始まりを IME に任せると、オンで確定して閉じるたびにオンとオフが交互に出た（実機）。
        // IMM（ImmSetOpenStatus）で決めても Copilot Keyboard などの TSF の IME は従わないので、TSF の状態を読み書きする。
        // アクティブになった直後は IME が新しいコンテキストを初期化して上書きするので、待ち行列に回してから決める。
        // 入力の誤りの知らせから戻ったときは決め直さない
        var applied = false;
        dialog.Activated += (_, _) =>
        {
            if (applied) return;
            applied = true;
            if (s_last is { } last) dialog.BeginInvoke(() => Tsf.Write(last));
        };
        // ダイアログがアクティブでなくなると WinForms が IME の状態を戻すので、戻される前に覚える。
        // 閉じ始めはまだフォーカスがあるときだけ、閉じ終わった後は覚えない
        var closed = false;
        dialog.Deactivate += (_, _) => { if (!closed) s_last = Tsf.Read() ?? s_last; };
        dialog.FormClosing += (_, _) => { if (dialog.ContainsFocus) s_last = Tsf.Read() ?? s_last; };
        dialog.FormClosed += (_, _) => closed = true;
    }

    /// <summary>最後に閉じたダイアログの IME の状態。まだ無ければ null（IME に任せる）。</summary>
    private static ImeState? s_last;

    private readonly record struct ImeState(bool Open, int Conversion);

    /// <summary>
    /// TSF のキーボードの状態（GUID_COMPARTMENT_KEYBOARD_OPENCLOSE と入力モード）。
    /// TSF が使えない環境では読み書きに失敗して null を返し、何もしない（IME に任せたままになる）。
    /// </summary>
    private static class Tsf
    {
        private static readonly Guid s_openClose = new("58273aad-01bb-4164-95c6-755ba0b5162d");
        private static readonly Guid s_conversion = new("ccf05dd8-4a87-11d7-a6e2-00065b84435c");
        private static ITfThreadMgr? s_manager;
        private static uint s_client;

        internal static ImeState? Read()
        {
            try
            {
                return Get(s_openClose) is { } open && Get(s_conversion) is { } conversion ? new ImeState(open != 0, conversion) : null;
            }
            catch (Exception e) when (e is COMException or InvalidCastException) { return null; }
        }

        internal static void Write(ImeState state)
        {
            try
            {
                Set(s_openClose, state.Open ? 1 : 0);
                Set(s_conversion, state.Conversion);
            }
            catch (Exception e) when (e is COMException or InvalidCastException) { }
        }

        private static ITfCompartment Compartment(Guid id)
        {
            if (s_manager is null)
            {
                // CLSID_TF_ThreadMgr。UI スレッドで作ると、そのスレッドの既存のスレッドマネージャーが返る
                s_manager = (ITfThreadMgr)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("529a9e6b-6587-4f23-ab9e-9c7d683e3c50"))!)!;
                s_manager.Activate(out s_client);
            }
            ((ITfCompartmentMgr)s_manager).GetCompartment(ref id, out var compartment);
            return compartment;
        }

        private static int? Get(Guid id) => Compartment(id).GetValue(out var value) == 0 && value is int i ? i : null;

        private static void Set(Guid id, int value)
        {
            object boxed = value;
            Compartment(id).SetValue(s_client, ref boxed);
        }

        [ComImport, Guid("aa80e801-2021-11d2-93e0-0060b067b86e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITfThreadMgr
        {
            void Activate(out uint clientId);
            void Deactivate();
            void CreateDocumentMgr(out IntPtr documentManager);
            void EnumDocumentMgrs(out IntPtr enumerator);
            void GetFocus(out IntPtr documentManager);
            void SetFocus(IntPtr documentManager);
            void AssociateFocus(IntPtr window, IntPtr newDocumentManager, out IntPtr previous);
            void IsThreadFocus([MarshalAs(UnmanagedType.Bool)] out bool focus);
            void GetFunctionProvider(ref Guid clsid, out IntPtr provider);
            void EnumFunctionProviders(out IntPtr enumerator);
            void GetGlobalCompartment(out ITfCompartmentMgr manager);
        }

        [ComImport, Guid("7dcf57ac-18ad-438b-824d-979bffb74b7c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITfCompartmentMgr
        {
            void GetCompartment(ref Guid id, out ITfCompartment compartment);
            void ClearCompartment(uint clientId, ref Guid id);
            void EnumCompartments(out IntPtr enumerator);
        }

        [ComImport, Guid("bb08f7a9-607a-4384-8623-056892b64371"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITfCompartment
        {
            [PreserveSig] int SetValue(uint clientId, ref object value);
            [PreserveSig] int GetValue(out object value);
        }
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
