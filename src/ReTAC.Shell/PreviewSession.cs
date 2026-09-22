using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace ReTAC.Shell;

/// <summary>
/// R-99: Windows に登録されたプレビューハンドラー 1 件の表示。要求 1 件ごとに専用のバックグラウンド STA を 1 本持ち、
/// 作成・初期化・表示・解放をすべてその STA で行う（別スレッドから同期して解放しない）。
/// ハンドラーは CLSCTX_LOCAL_SERVER で共有の Prevhost.exe に作る。応答しないハンドラーがあっても
/// その STA が止まるだけで、UI と新しい要求は止まらない。CoCancelCall と Prevhost.exe の強制終了は使わない
/// （共有プロセスを不安定にする。技術ゲートで RPC_E_DISCONNECTED を観測した）。
/// </summary>
public sealed class PreviewSession
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private IPreviewHandler? _handler;
    private object? _source;   // 初期化に渡した IStream / IShellItem。解放まで持つ
    private volatile bool _busy;

    private PreviewSession()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "ReTAC preview" };   // 応答しないワーカーが終了を止めない
        _thread.SetApartmentState(ApartmentState.STA);
    }

    /// <summary>
    /// R-99（Q90）: 表示専用にするハンドラー。Microsoft の PDF のハンドラー（WebView2 で描く）は、ReTAC に置くと
    /// プレビューの中をクリックして WebView2 にフォーカスが入った時点で入力が止まり、ReTAC ごと操作できなくなった
    /// （エクスプローラーでは起きない。エクスプローラーは非公開の仕組みで低い整合性レベルの Prevhost に作っている。
    /// site・呼ぶスレッド・描画先の表示状態を変えても直らなかった）。ホイールでのスクロールは固まらないので、
    /// マウスのボタンを届けず、フォーカスも渡さずに、見ることとホイールだけにする。
    /// </summary>
    public static bool IsViewOnly(Guid clsid) => clsid == new Guid("3A84F9C2-6164-485C-A7D9-4B27F8AC009E");

    /// <summary>R-99: 拡張子に登録されたプレビューハンドラーの CLSID。無ければ null（「プレビューできません」。再試行は出さない）。</summary>
    public static Guid? FindHandler(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Length == 0) return null;
        var buffer = new char[64];
        var length = (uint)buffer.Length;
        var hr = AssocQueryString(ASSOCF_INIT_DEFAULTTOSTAR | ASSOCF_NOTRUNCATE, ASSOCSTR_SHELLEXTENSION,
                                  extension, IID_IPreviewHandlerText, buffer, ref length);
        return hr == 0 && Guid.TryParse(new string(buffer, 0, (int)Math.Max(0, length - 1)), out var clsid) ? clsid : null;
    }

    /// <summary>
    /// 専用の STA で読み込みを始めて、すぐ戻る。completed は読み込みの成否で 1 回だけ、その STA から呼ぶ。
    /// tabPressed はハンドラーが Tab / Shift+Tab を返してきたとき（Q22）に、COM の呼び出し元のスレッドから呼ぶ。
    /// </summary>
    /// <param name="host">ハンドラーの窓を置く子ウィンドウ。UI スレッドで作ったもの</param>
    public static PreviewSession Start(Guid clsid, string path, IntPtr host, Size size, Action<bool> completed, Action tabPressed)
    {
        var session = new PreviewSession();
        session._queue.Add(() => completed(session.Load(clsid, path, host, size, new PreviewFrame(tabPressed))));
        session._thread.Start();
        return session;
    }

    public void SetSize(Size size) => Post(() =>
    {
        var rect = new RECT { Right = size.Width, Bottom = size.Height };
        _handler?.SetRect(ref rect);
    });

    /// <summary>利用者がクリック・Tab でプレビューへ入ったときだけ呼ぶ（Q12: 表示の更新ではフォーカスを奪わない）。</summary>
    public void Focus() => Post(() => _handler?.SetFocus());

    /// <summary>
    /// 解放を頼む。今の呼び出しが戻った後に、所有 STA が Unload → site 解除 → COM 解放を行い、スレッドを終える。
    /// released は解放が済んだ後に、その STA から呼ぶ。止まっているハンドラーなら呼ばれない。
    /// </summary>
    public void Release(Action? released = null)
    {
        lock (_queue)
        {
            if (_queue.IsAddingCompleted) return;
            _queue.Add(() =>
            {
                ReleaseCore();
                released?.Invoke();
            });
            _queue.CompleteAdding();
        }
    }

    /// <summary>呼び出し中でなければ、解放が済んでスレッドが終わるのを待つ（終了時）。応答しないワーカーは待たない。</summary>
    public void WaitIfIdle(int milliseconds)
    {
        if (!_busy) _thread.Join(milliseconds);
    }

    private void Post(Action action)
    {
        lock (_queue)
        {
            if (!_queue.IsAddingCompleted) _queue.Add(action);
        }
    }

    private void Run()
    {
        // STA の待ちは CLR が COM のメッセージを回すので、ハンドラーからの呼び出し（site）も受けられる
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            _busy = true;
            try { action(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            finally { _busy = false; }
        }
    }

    private bool Load(Guid clsid, string path, IntPtr host, Size size, PreviewFrame frame)
    {
        try
        {
            var iid = IID_IUnknown;
            Marshal.ThrowExceptionForHR(CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_LOCAL_SERVER, ref iid, out var instance));
            _handler = (IPreviewHandler)instance;
            // Stream → Item → File の順で、ハンドラーが持つものを使う（技術ゲート）
            switch (instance)
            {
                case IInitializeWithStream withStream:
                    Marshal.ThrowExceptionForHR(SHCreateStreamOnFileEx(path, STGM_READ | STGM_SHARE_DENY_NONE, 0, false, IntPtr.Zero, out var stream));
                    _source = stream;
                    withStream.Initialize(stream, STGM_READ);
                    break;
                case IInitializeWithItem withItem:
                    var itemIid = IID_IShellItem;
                    Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, IntPtr.Zero, ref itemIid, out var item));
                    _source = item;
                    withItem.Initialize(item, STGM_READ);
                    break;
                case IInitializeWithFile withFile:
                    withFile.Initialize(path, STGM_READ);
                    break;
                default:
                    ReleaseCore();
                    return false;
            }
            (instance as IObjectWithSite)?.SetSite(frame);
            var rect = new RECT { Right = size.Width, Bottom = size.Height };
            _handler.SetWindow(host, ref rect);
            _handler.DoPreview();
            return true;
        }
        catch (Exception ex)   // 第三者のハンドラーの境界。何が来ても失敗として返さないと、画面が「読み込んでいます」のまま残る
        {
            System.Diagnostics.Debug.WriteLine(ex);
            ReleaseCore();
            return false;
        }
    }

    private void ReleaseCore()
    {
        if (_handler is { } handler)
        {
            _handler = null;
            try { handler.Unload(); } catch (COMException) { }
            try { (handler as IObjectWithSite)?.SetSite(null); } catch (COMException) { }
            Marshal.FinalReleaseComObject(handler);
        }
        if (_source is { } source)
        {
            _source = null;
            Marshal.FinalReleaseComObject(source);   // ファイルを掴んだままにしない
        }
    }

    // ---- 相互運用（Windows SDK: shobjidl.h / propsys.h / ocidl.h / shlwapi.h） ----

    private const string IID_IPreviewHandlerText = "{8895b1c6-b41f-4c1c-a562-0d564250836f}";
    private static readonly Guid IID_IUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid IID_IShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    private const uint CLSCTX_LOCAL_SERVER = 0x4;
    private const uint STGM_READ = 0x0, STGM_SHARE_DENY_NONE = 0x40;
    private const uint ASSOCF_INIT_DEFAULTTOSTAR = 0x4, ASSOCF_NOTRUNCATE = 0x20;
    private const int ASSOCSTR_SHELLEXTENSION = 16;

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid,
                                               [MarshalAs(UnmanagedType.IUnknown)] out object instance);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int AssocQueryString(uint flags, int str, string assoc, string extra, [Out] char[] output, ref uint length);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateStreamOnFileEx(string file, uint mode, uint attributes, [MarshalAs(UnmanagedType.Bool)] bool create,
                                                     IntPtr template, out IStream stream);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid,
                                                          [MarshalAs(UnmanagedType.IUnknown)] out object item);
}

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left, Top, Right, Bottom;
}

[StructLayout(LayoutKind.Sequential)]
public struct MSG
{
    public IntPtr Hwnd;
    public uint Message;
    public IntPtr WParam, LParam;
    public uint Time;
    public int X, Y;
}

[StructLayout(LayoutKind.Sequential)]
public struct PREVIEWHANDLERFRAMEINFO
{
    public IntPtr Accelerators;
    public uint AcceleratorCount;
}

[ComImport, Guid("8895b1c6-b41f-4c1c-a562-0d564250836f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPreviewHandler
{
    void SetWindow(IntPtr hwnd, ref RECT rect);
    void SetRect(ref RECT rect);
    void DoPreview();
    void Unload();
    void SetFocus();
    void QueryFocus(out IntPtr hwnd);
    [PreserveSig] int TranslateAccelerator(ref MSG msg);
}

[ComImport, Guid("fec87aaf-35f9-447a-adb7-20234491401a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IPreviewHandlerFrame
{
    [PreserveSig] int GetWindowContext(out PREVIEWHANDLERFRAMEINFO info);
    [PreserveSig] int TranslateAccelerator(ref MSG msg);
}

[ComImport, Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IInitializeWithStream
{
    void Initialize(IStream stream, uint mode);
}

[ComImport, Guid("7f73be3f-fb79-493c-a6c7-7ee14e245841"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IInitializeWithItem
{
    void Initialize([MarshalAs(UnmanagedType.IUnknown)] object item, uint mode);
}

[ComImport, Guid("b7d14566-0509-4cce-a71f-0a554233bd9b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IInitializeWithFile
{
    void Initialize([MarshalAs(UnmanagedType.LPWStr)] string path, uint mode);
}

[ComImport, Guid("fc4801a3-2ba9-11cf-a229-00aa003d7352"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IObjectWithSite
{
    void SetSite([MarshalAs(UnmanagedType.IUnknown)] object? site);
    void GetSite(ref Guid iid, out IntPtr site);
}

/// <summary>
/// R-99 / Q22 / Q62: ハンドラーに渡す site。Tab / Shift+Tab だけを受け取り（ファイルリストへ戻す）、ほかのキーはハンドラーに任せる。
/// Prevhost.exe から呼ばれるので public にする（internal だと COM に見えず落ちる）。
/// </summary>
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class PreviewFrame(Action tabPressed) : IPreviewHandlerFrame
{
    private const uint WM_KEYDOWN = 0x0100;
    private const int VK_TAB = 0x09;

    /// <summary>ハンドラーはこの表にあるキーだけを TranslateAccelerator で返してくるので、Tab と Shift+Tab を載せる。</summary>
    private static readonly IntPtr s_accelerators = CreateTabAccelerators();

    public int GetWindowContext(out PREVIEWHANDLERFRAMEINFO info)
    {
        info = new PREVIEWHANDLERFRAMEINFO { Accelerators = s_accelerators, AcceleratorCount = s_accelerators == IntPtr.Zero ? 0u : 2u };
        return 0;
    }

    public int TranslateAccelerator(ref MSG msg)
    {
        if (msg.Message != WM_KEYDOWN || (int)msg.WParam != VK_TAB) return 1;   // S_FALSE: ハンドラー自身が処理する
        tabPressed();
        return 0;
    }

    private static IntPtr CreateTabAccelerators()
    {
        const byte FVIRTKEY = 0x01, FSHIFT = 0x04;
        ACCEL[] table = [new() { Flags = FVIRTKEY, Key = VK_TAB, Command = 1 }, new() { Flags = FVIRTKEY | FSHIFT, Key = VK_TAB, Command = 2 }];
        return CreateAcceleratorTable(table, table.Length);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ACCEL
    {
        public byte Flags;
        public ushort Key;
        public ushort Command;
    }

    [DllImport("user32.dll", EntryPoint = "CreateAcceleratorTableW")]
    private static extern IntPtr CreateAcceleratorTable([In] ACCEL[] accelerators, int count);
}
