using System.Drawing;
using System.Runtime.InteropServices;
using ReTAC.Domain.FileOps;

namespace ReTAC.Shell;

/// <summary>
/// R-111-2: 右ボタンで落としたときのメニュー。ドロップの処理の中で同期して選ばせる（ドラッグ元はメニューを閉じるまで待つ）。
/// ContextMenuStrip は閉じるのを待てないので、シェルの右クリックのメニューと同じ Win32 のメニューにする。
/// </summary>
public static class DropMenu
{
    private const int CopyId = 1, MoveId = 2, LinkId = 3, CancelId = 4;
    private const uint MF_GRAYED = 0x1, MF_SEPARATOR = 0x800, TPM_RIGHTBUTTON = 0x2, TPM_RETURNCMD = 0x100;

    public static DropChoice Show(IntPtr owner, DropMenuModel model, Point screenPoint)
    {
        var menu = CreatePopupMenu();
        try
        {
            AppendMenu(menu, model.CopyEnabled ? 0 : MF_GRAYED, CopyId, "ここにコピー(&C)");
            AppendMenu(menu, model.MoveEnabled ? 0 : MF_GRAYED, MoveId, "ここに移動(&M)");
            AppendMenu(menu, model.LinkEnabled ? 0 : MF_GRAYED, LinkId, "ショートカットをここに作成(&S)");
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, 0, CancelId, "キャンセル");
            // 左ボタンでは何も起きない所（別のドライブで移動だけ・リンクだけ）は太字なし
            if (model.Default != DropAction.None) SetMenuDefaultItem(menu, model.Default == DropAction.Copy ? CopyId : MoveId, 0);
            // 前面にしないと、メニューの外をクリックしても閉じない（外のアプリから落とされたとき ReTAC は背面にいる）
            SetForegroundWindow(owner);
            return TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, screenPoint.X, screenPoint.Y, owner, IntPtr.Zero) switch
            {
                CopyId => DropChoice.Copy,
                MoveId => DropChoice.Move,
                LinkId => DropChoice.Link,
                _ => DropChoice.Cancel,
            };
        }
        finally { DestroyMenu(menu); }
    }

    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, nint id, string? text);
    [DllImport("user32.dll")] private static extern bool SetMenuDefaultItem(IntPtr menu, int item, uint byPosition);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
}
