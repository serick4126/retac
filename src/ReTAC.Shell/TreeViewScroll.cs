using System.Runtime.InteropServices;

namespace ReTAC.Shell;

/// <summary>
/// R-97-5: 名前空間ツリーの中の SysTreeView32 のスクロール位置を覚えて、後で戻す。
/// 縦は WM_VSCROLL の SB_THUMBPOSITION では戻らない（位置の値を渡しても動かなかった。実測）ので、
/// 今の先頭の項目から差の行数だけ辿った項目を先頭にする。項目は戻すときに取り直すので、覚えている間に
/// 消された項目のハンドルを渡すことはない。横は SB_THUMBPOSITION で戻る（描画を止めていても効く。実測）。
/// </summary>
internal static class TreeViewScroll
{
    internal readonly record struct Position(int Vertical, int Horizontal);

    internal static Position Capture(IntPtr tree) => new(GetScrollPos(tree, SB_VERT), GetScrollPos(tree, SB_HORZ));

    internal static void Restore(IntPtr tree, Position position)
    {
        var delta = position.Vertical - GetScrollPos(tree, SB_VERT);
        if (delta != 0)
        {
            var top = SendMessage(tree, TVM_GETNEXTITEM, TVGN_FIRSTVISIBLE, IntPtr.Zero);
            var step = delta > 0 ? TVGN_NEXTVISIBLE : TVGN_PREVIOUSVISIBLE;
            for (var i = 0; i < Math.Abs(delta) && top != IntPtr.Zero; i++)
            {
                var next = SendMessage(tree, TVM_GETNEXTITEM, step, top);
                if (next == IntPtr.Zero) break;
                top = next;
            }
            if (top != IntPtr.Zero) SendMessage(tree, TVM_SELECTITEM, TVGN_FIRSTVISIBLE, top);
        }
        // ponytail: SB_THUMBPOSITION の位置は 16 ビットなので、横 65535 ピクセルを超える位置はそこで止まる
        var horizontal = Math.Clamp(position.Horizontal, 0, 0xFFFF);
        SendMessage(tree, WM_HSCROLL, (IntPtr)((horizontal << 16) | SB_THUMBPOSITION), IntPtr.Zero);
    }

    private const uint TVM_GETNEXTITEM = 0x110A;
    private const uint TVM_SELECTITEM = 0x110B;
    private static readonly IntPtr TVGN_FIRSTVISIBLE = 0x5;
    private static readonly IntPtr TVGN_NEXTVISIBLE = 0x6;
    private static readonly IntPtr TVGN_PREVIOUSVISIBLE = 0x7;
    private const uint WM_HSCROLL = 0x0114;
    private const int SB_THUMBPOSITION = 4;
    private const int SB_HORZ = 0;
    private const int SB_VERT = 1;

    [DllImport("user32.dll")]
    private static extern int GetScrollPos(IntPtr hwnd, int bar);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
