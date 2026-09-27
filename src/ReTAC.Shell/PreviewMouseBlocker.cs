using System.Drawing;
using System.Runtime.InteropServices;

namespace ReTAC.Shell;

/// <summary>
/// R-99（Q90）: 表示専用のプレビュー（PreviewSession.IsViewOnly）の上で、マウスのボタンをハンドラーへ届けない。
/// ボタンで WebView2 にフォーカスが入ると ReTAC ごと入力が止まるため。ホイールは止めない（固まらずにスクロールできる）。
/// プレビューの窓は Prevhost.exe の中にあって ReTAC には届かないので、表示している間だけ低レベルのマウスフックで止める。
/// </summary>
public sealed class PreviewMouseBlocker : IDisposable
{
    private readonly HookProc _proc;   // フックの間は GC に回収させない
    private readonly Func<Point, bool> _isOverPreview;
    private IntPtr _hook;
    private int _blockedButtons;   // 押すのを止めたボタン（ビット）。離すのはこれだけ止める

    /// <param name="isOverPreview">画面上の位置がプレビューの上か。フックの中で呼ぶので軽くすること</param>
    public PreviewMouseBlocker(Func<Point, bool> isOverPreview)
    {
        _isOverPreview = isOverPreview;
        _proc = OnMouse;
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr OnMouse(int code, IntPtr message, IntPtr data)
    {
        // 位置は MSLLHOOKSTRUCT の先頭、どの X ボタンかは 8 バイト目からの mouseData の上位ワードにある。
        // フックは移動も含めて全部の通知を通るので、ボタンの通知でなければ判定の関数を作る前に抜ける
        if (code >= 0 && (int)message is >= WM_LBUTTONDOWN and <= WM_XBUTTONDOWN + 2 && (int)message != WM_MOUSEWHEEL
            && ShouldBlock(ref _blockedButtons, (int)message, (uint)Marshal.ReadInt32(data, 8),
                () => _isOverPreview(Marshal.PtrToStructure<Point>(data))))
            return 1;
        return CallNextHookEx(_hook, code, message, data);
    }

    /// <summary>
    /// そのボタンの通知を止めるか。押すのを止めたボタンは <paramref name="blocked"/> に覚え、離すのも止める
    /// （片方だけ届くと、ハンドラー側がボタンを押したままだと思い込む）。押すのを止めていないボタンの離すは通す。
    /// 境界線をドラッグしてプレビューの上で離したとき、離すを止めると SplitContainer がドラッグを終えられない。
    /// </summary>
    internal static bool ShouldBlock(ref int blocked, int message, uint mouseData, Func<bool> isOverPreview)
    {
        var (bit, down) = Button(message, mouseData);
        if (bit == 0) return false;
        if (down)
        {
            if (!isOverPreview()) return false;
            blocked |= bit;
            return true;
        }
        if ((blocked & bit) == 0) return false;
        blocked &= ~bit;
        return true;
    }

    /// <summary>ボタンのビットと、押したのか（true）離したのか。ボタンでなければビットは 0。</summary>
    private static (int Bit, bool Down) Button(int message, uint mouseData) => message switch
    {
        WM_LBUTTONDOWN or WM_LBUTTONDOWN + 2 => (1, true),
        WM_LBUTTONDOWN + 1 => (1, false),
        WM_RBUTTONDOWN or WM_RBUTTONDOWN + 2 => (2, true),
        WM_RBUTTONDOWN + 1 => (2, false),
        WM_MBUTTONDOWN or WM_MBUTTONDOWN + 2 => (4, true),
        WM_MBUTTONDOWN + 1 => (4, false),
        // X ボタンは 2 つが同じ通知で来る。分けて覚えないと、片方を止めたまま他方の離すまで止めてしまう
        WM_XBUTTONDOWN or WM_XBUTTONDOWN + 2 => (XButtonBit(mouseData), true),
        WM_XBUTTONDOWN + 1 => (XButtonBit(mouseData), false),
        _ => (0, false),
    };

    private static int XButtonBit(uint mouseData) => (mouseData >> 16) switch
    {
        XBUTTON1 => 8,
        XBUTTON2 => 16,
        _ => 0,
    };

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207, WM_XBUTTONDOWN = 0x020B, WM_MOUSEWHEEL = 0x020A;
    private const uint XBUTTON1 = 1, XBUTTON2 = 2;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}
