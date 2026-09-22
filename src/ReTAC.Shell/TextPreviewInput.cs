using System.Drawing;
using System.Runtime.InteropServices;

namespace ReTAC.Shell;

/// <summary>
/// R-99（利用者の要望）: テキストのプレビューを、読むためだけの表示として操作できるようにする。
/// プレビューの窓は Prevhost.exe の中にあり、キーも右クリックも ReTAC には届かない。
/// site（IPreviewHandlerFrame）でキーを受け取ったと返しても、TXT のハンドラーは同じキーでキャレットを動かし、
/// 端では警告音を鳴らした（一時ログで確認）。そこでテキストを表示している間だけ低レベルのフックで先に受け取る。
/// - 移動キー: テキストの窓にフォーカスがあるときだけ、窓へ渡さずに表示だけをスクロールする（キャレットは動かない・警告音も無い）
/// - 右クリック: 位置を知らせるだけ。コピー・すべて選択は窓へメッセージで頼む
/// </summary>
public sealed class TextPreviewInput : IDisposable
{
    private readonly HookProc _keyProc, _mouseProc;   // フックの間は GC に回収させない
    private readonly Func<IntPtr, bool> _isTextWindow;
    private readonly Action<Point> _rightButtonUp;
    private IntPtr _keyHook, _mouseHook;

    /// <param name="isTextWindow">フォーカスのある窓がテキストのプレビューの窓か</param>
    /// <param name="rightButtonUp">右ボタンを離した画面上の位置。フックを付けたスレッド（UI スレッド）から呼ぶ</param>
    public TextPreviewInput(Func<IntPtr, bool> isTextWindow, Action<Point> rightButtonUp)
    {
        _isTextWindow = isTextWindow;
        _rightButtonUp = rightButtonUp;
        _keyProc = OnKey;
        _mouseProc = OnMouse;
        var module = GetModuleHandle(null);
        _keyHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyProc, module, 0);
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, module, 0);
    }

    public void Dispose()
    {
        if (_keyHook != IntPtr.Zero) UnhookWindowsHookEx(_keyHook);
        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
        _keyHook = _mouseHook = IntPtr.Zero;
    }

    /// <summary>移動キー → スクロール。Shift 付き（選択の広げ方）と Alt 付きは窓に任せる。Ctrl+Home / Ctrl+End は Home / End と同じ。</summary>
    private static readonly Dictionary<int, (uint Message, int Code)> s_scrolls = new()
    {
        [0x26] = (WM_VSCROLL, 0),   // ↑ SB_LINEUP
        [0x28] = (WM_VSCROLL, 1),   // ↓ SB_LINEDOWN
        [0x21] = (WM_VSCROLL, 2),   // PageUp SB_PAGEUP
        [0x22] = (WM_VSCROLL, 3),   // PageDown SB_PAGEDOWN
        [0x24] = (WM_VSCROLL, 6),   // Home SB_TOP
        [0x23] = (WM_VSCROLL, 7),   // End SB_BOTTOM
    };

    private IntPtr OnKey(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && message is WM_KEYDOWN && s_scrolls.TryGetValue(Marshal.ReadInt32(data), out var scroll)
            && !Down(VK_SHIFT) && !Down(VK_MENU) && FocusedWindow() is var focus && focus != IntPtr.Zero && _isTextWindow(focus))
        {
            // 端でさらに頼まない。フックの中で待たないよう PostMessage で頼む
            if (!AtEnd(focus, forward: scroll.Code is 1 or 3 or 7)) PostMessage(focus, scroll.Message, scroll.Code, IntPtr.Zero);
            return 1;   // 窓へは渡さない
        }
        return CallNextHookEx(_keyHook, code, message, data);
    }

    private IntPtr OnMouse(int code, IntPtr message, IntPtr data)
    {
        // 横取りはしない。ここで時間をかけるとマウス全体が遅くなるので、位置を渡すだけにする
        if (code >= 0 && message == WM_RBUTTONUP) _rightButtonUp(Marshal.PtrToStructure<Point>(data));
        return CallNextHookEx(_mouseHook, code, message, data);
    }

    /// <summary>前面の窓のスレッドでフォーカスを持つ窓。Prevhost の窓は ReTAC の子なので、ReTAC が前面のときに取れる。</summary>
    private static IntPtr FocusedWindow()
    {
        var info = new GUITHREADINFO { Size = Marshal.SizeOf<GUITHREADINFO>() };
        var thread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        return GetGUIThreadInfo(thread, ref info) ? info.Focus : IntPtr.Zero;
    }

    /// <summary>その向きにもう動けないか。スクロールバーが無い（全部見えている）ときも端とみなす。最後の位置は Max - Page + 1。</summary>
    private static bool AtEnd(IntPtr hwnd, bool forward)
    {
        var info = new SCROLLINFO { Size = (uint)Marshal.SizeOf<SCROLLINFO>(), Mask = SIF_ALL };
        if (!GetScrollInfo(hwnd, SB_VERT, ref info)) return true;
        return forward ? info.Pos + Math.Max(1, (int)info.Page) >= info.Max : info.Pos <= info.Min;
    }

    private static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    /// <summary>window が parent の子孫か（Prevhost の窓は ReTAC の子ホストの下に付く）。</summary>
    public static bool IsInside(IntPtr parent, IntPtr window) => IsChild(parent, window);

    /// <summary>その位置にある窓（Prevhost の中のテキストの窓）。</summary>
    public static IntPtr WindowAt(Point screen) => WindowFromPoint(screen);

    /// <summary>選んでいる範囲をクリップボードへ。応答しない窓で待たされないよう上限を付ける。</summary>
    public static void Copy(IntPtr window) => SendMessageTimeout(window, WM_COPY, 0, 0, SMTO_ABORTIFHUNG, 1000, out _);

    public static void SelectAll(IntPtr window) => SendMessageTimeout(window, EM_SETSEL, 0, -1, SMTO_ABORTIFHUNG, 1000, out _);

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    private const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14, SB_VERT = 1, VK_SHIFT = 0x10, VK_MENU = 0x12;
    private const nint WM_KEYDOWN = 0x0100, WM_RBUTTONUP = 0x0205;
    private const uint WM_VSCROLL = 0x0115, WM_COPY = 0x0301, EM_SETSEL = 0x00B1, SMTO_ABORTIFHUNG = 0x0002, SIF_ALL = 0x17;

    [StructLayout(LayoutKind.Sequential)]
    private struct SCROLLINFO
    {
        public uint Size, Mask;
        public int Min, Max;
        public uint Page;
        public int Pos, TrackPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int Size, Flags;
        public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public Rectangle CaretRect;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    [DllImport("user32.dll")]
    private static extern bool IsChild(IntPtr parent, IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, nint wParam, nint lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint message, nint wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetScrollInfo(IntPtr hwnd, int bar, ref SCROLLINFO info);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);
}
