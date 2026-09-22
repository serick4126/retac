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
        // 押すのも離すのも止める（片方だけ届くと、ハンドラー側がボタンを押したままだと思い込む）
        if (code >= 0 && (int)message is >= WM_LBUTTONDOWN and <= WM_MBUTTONDBLCLK or >= WM_XBUTTONDOWN and <= WM_XBUTTONDBLCLK
            && _isOverPreview(Marshal.PtrToStructure<Point>(data)))
            return 1;
        return CallNextHookEx(_hook, code, message, data);
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201, WM_MBUTTONDBLCLK = 0x0209, WM_XBUTTONDOWN = 0x020B, WM_XBUTTONDBLCLK = 0x020D;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}
