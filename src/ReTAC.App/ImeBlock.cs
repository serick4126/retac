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
        // 追跡を始めた時点でハンドルがある欄（作ってから足した欄）は、HandleCreated が二度と起きないのでその場で付ける
        void Track(Control control)
        {
            control.HandleCreated += (_, _) => Associate(control);
            control.ControlAdded += (_, e) => Track(e.Control!);
            if (control.IsHandleCreated) Associate(control);
            foreach (Control child in control.Controls) Track(child);
        }

        dialog.HandleCreated += (_, _) =>
        {
            context = ImmCreateContext();
            Associate(dialog);
        };
        var carry = new CarryOver();
        dialog.HandleDestroyed += (_, _) =>
        {
            carry.HandleDestroyed();
            if (context == IntPtr.Zero) return;
            ImmAssociateContextEx(dialog.Handle, IntPtr.Zero, IACE_CHILDREN | IACE_DEFAULT);
            ImmDestroyContext(context);
            context = IntPtr.Zero;
        };
        foreach (Control child in dialog.Controls) Track(child);
        dialog.ControlAdded += (_, e) => Track(e.Control!);

        // R-143-1: 直前のダイアログの IME の状態（オン・オフと入力モード）を引き継ぐ。
        // アクティブになった直後は IME が新しいコンテキストを初期化して上書きするので、待ち行列に回してから書く
        dialog.Activated += (_, _) =>
        {
            if (carry.Activated() is { } ticket)
                dialog.BeginInvoke(() => { if (carry.Due(ticket) is { } state && Tsf.Write(state)) carry.Written(); });
        };
        // ダイアログがアクティブでなくなると WinForms が IME の状態を戻すので、戻される前に覚える
        dialog.Deactivate += (_, _) => carry.Deactivated(Tsf.Read());
        dialog.FormClosing += (_, _) => carry.Closing(dialog.ContainsFocus, Tsf.Read());
        dialog.FormClosed += (_, _) => carry.Closed();
    }

    /// <summary>直前にアクティブでなくなったダイアログの IME の状態。まだ無ければ null（IME に任せる）。UI スレッドごと。</summary>
    [ThreadStatic] internal static ImeState? s_last;

    /// <summary>
    /// R-143-1: 1 つのダイアログの、IME の状態の引き継ぎ。
    /// 書き戻しは予約した後もダイアログがアクティブなままのときだけ行う。予約の後にアクティブでなくなった・閉じた・
    /// ハンドルを作り直したときに書くと、移った先（別のダイアログや主ウィンドウ）の状態を書き換えてしまう。
    /// 書き戻す前の状態は新しいコンテキストの初期値なので覚えない。書き戻せなかったら次のアクティブ化でやり直す。
    /// </summary>
    private sealed class CarryOver
    {
        private bool _active, _applied, _closed;
        private int _lastTicket;
        private int? _pending;

        /// <returns>書き戻しを予約するなら、その予約の番号</returns>
        internal int? Activated()
        {
            _active = true;
            if (_applied || _closed) return null;
            if (s_last is null) { _applied = true; return null; }   // 引き継ぐ元が無い。今の状態がこのダイアログの状態
            return _pending = ++_lastTicket;
        }

        /// <returns>今書き戻す状態。予約が取り消されていれば null</returns>
        internal ImeState? Due(int ticket)
        {
            if (_pending != ticket || !_active) return null;
            _pending = null;
            return s_last;
        }

        /// <summary>書き戻せた。書けなかったときは呼ばないので、次のアクティブ化でやり直す。</summary>
        internal void Written() => _applied = true;

        internal void Deactivated(ImeState? now)
        {
            _active = false;
            _pending = null;
            Remember(now);
        }

        /// <param name="hasFocus">閉じ始めにまだフォーカスがあるか（無ければ WinForms が状態を戻した後）</param>
        internal void Closing(bool hasFocus, ImeState? now)
        {
            if (hasFocus) Remember(now);
        }

        internal void Closed()
        {
            _closed = true;
            _active = false;
            _pending = null;
        }

        /// <summary>作り直したハンドルには新しいコンテキストが付くので、次のアクティブ化で書き戻し直す。</summary>
        internal void HandleDestroyed()
        {
            _pending = null;
            _applied = false;
        }

        private void Remember(ImeState? now)
        {
            if (_applied && !_closed && now is { } state) s_last = state;
        }
    }

    internal readonly record struct ImeState(bool Open, int Conversion);

    /// <summary>
    /// TSF のキーボードの状態（GUID_COMPARTMENT_KEYBOARD_OPENCLOSE と入力モード）。
    /// TSF が使えない環境では読み書きに失敗して null を返し、何もしない（IME に任せたままになる）。
    /// </summary>
    internal static class Tsf
    {
        private static readonly Guid s_openClose = new("58273aad-01bb-4164-95c6-755ba0b5162d");
        private static readonly Guid s_conversion = new("ccf05dd8-4a87-11d7-a6e2-00065b84435c");
        // TSF のスレッドマネージャーはスレッドごと。別のスレッドで作ったものを使うと、そのスレッドが終わった後に
        // InvalidComObjectException になる（テストの STA のスレッドで起きた）
        [ThreadStatic] private static ITfThreadMgr? s_manager;
        [ThreadStatic] private static uint s_client;

        internal static ImeState? Read()
        {
            try
            {
                return Get(s_openClose) is { } open && Get(s_conversion) is { } conversion ? new ImeState(open != 0, conversion) : null;
            }
            catch (Exception e) when (Unavailable(e)) { return null; }
        }

        /// <returns>書けたか</returns>
        internal static bool Write(ImeState state)
        {
            try
            {
                Set(s_openClose, state.Open ? 1 : 0);
                Set(s_conversion, state.Conversion);
                return true;
            }
            catch (Exception e) when (Unavailable(e)) { return false; }
        }

        /// <summary>TSF が無い・型が合わない・COM のオブジェクトが使えない。どれも何もしないで IME に任せる。</summary>
        private static bool Unavailable(Exception e) => e is COMException or InvalidCastException or InvalidComObjectException;

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
            Marshal.ThrowExceptionForHR(Compartment(id).SetValue(s_client, ref boxed));   // PreserveSig なので失敗は戻り値で返る
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
