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
        if (code >= 0 && Button((int)message) is var (bit, down) && bit != 0)
        {
            // 押すのを止めたら、離すのも止める（片方だけ届くと、ハンドラー側がボタンを押したままだと思い込む）。
            // 押すのを止めていないボタンの離すは通す。境界線をドラッグしてプレビューの上で離したとき、
            // 離すを止めると SplitContainer がドラッグを終えられない
            if (down && _isOverPreview(Marshal.PtrToStructure<Point>(data)))
            {
                _blockedButtons |= bit;
                return 1;
            }
            if (!down && (_blockedButtons & bit) != 0)
            {
                _blockedButtons &= ~bit;
                return 1;
            }
        }
        return CallNextHookEx(_hook, code, message, data);
    }

    /// <summary>ボタンのビットと、押したのか（true）離したのか。ボタンでなければビットは 0。</summary>
    private static (int Bit, bool Down) Button(int message) => message switch
    {
        WM_LBUTTONDOWN or WM_LBUTTONDOWN + 2 => (1, true),
        WM_LBUTTONDOWN + 1 => (1, false),
        WM_RBUTTONDOWN or WM_RBUTTONDOWN + 2 => (2, true),
        WM_RBUTTONDOWN + 1 => (2, false),
        WM_MBUTTONDOWN or WM_MBUTTONDOWN + 2 => (4, true),
        WM_MBUTTONDOWN + 1 => (4, false),
        WM_XBUTTONDOWN or WM_XBUTTONDOWN + 2 => (8, true),
        WM_XBUTTONDOWN + 1 => (8, false),
        _ => (0, false),
    };

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207, WM_XBUTTONDOWN = 0x020B;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}
