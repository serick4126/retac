using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>
/// R-108-4: グループ見出しのある一覧。ダークのとき、見出しだけを自分で描く。
/// コモンコントロールは見出しをテーマの色（暗い青）で描き、暗い地ではほとんど読めない。
/// DarkMode_ItemsView などテーマを替えても見出しの色は変わらず、LVGROUPMETRICS の色もテーマ描画では無視される。
/// ライトでは何もしない（今までの見た目のまま）。
/// </summary>
public sealed class GroupedListView : ListView
{
    private const int WM_REFLECT_NOTIFY = 0x204E;
    private const int NM_CUSTOMDRAW = -12;
    private const int CDDS_PREPAINT = 0x1;
    private const int CDRF_SKIPDEFAULT = 0x4;
    private const uint LVCDI_GROUP = 1;
    private const int LVM_GETGROUPINFO = 0x1095;
    private const uint LVGF_HEADER = 0x1;

    public GroupedListView() => ShowGroups = true;

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != WM_REFLECT_NOTIFY || !Application.IsDarkModeEnabled) return;
        var draw = Marshal.PtrToStructure<NMLVCUSTOMDRAW>(m.LParam);
        if (draw.nmcd.hdr.code != NM_CUSTOMDRAW) return;

        // 見出しの通知は、項目ごとの段階ではなく描き始め（CDDS_PREPAINT）の段階に dwItemType = LVCDI_GROUP を付けて来る
        if (draw.nmcd.dwDrawStage != CDDS_PREPAINT || draw.dwItemType != LVCDI_GROUP) return;
        var bounds = draw.rcText.ToRectangle();
        if (bounds.IsEmpty) bounds = draw.nmcd.rc.ToRectangle();
        DrawGroupHeader(draw.nmcd.hdc, bounds, (int)draw.nmcd.dwItemSpec);
        m.Result = (IntPtr)CDRF_SKIPDEFAULT;
    }

    private void DrawGroupHeader(IntPtr hdc, Rectangle bounds, int groupId)
    {
        using var g = Graphics.FromHdc(hdc);
        using (var back = new SolidBrush(BackColor)) g.FillRectangle(back, bounds);
        var text = GroupHeader(groupId);
        var textBounds = Rectangle.FromLTRB(bounds.Left + LogicalToDeviceUnits(6), bounds.Top, bounds.Right, bounds.Bottom);
        TextRenderer.DrawText(g, text, Font, textBounds, SystemColors.WindowText,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        // 見出しの右に区切りの線を引く（コモンコントロールの見出しと同じ形）
        var lineLeft = textBounds.Left + TextRenderer.MeasureText(g, text, Font).Width + LogicalToDeviceUnits(4);
        var y = bounds.Top + bounds.Height / 2;
        if (lineLeft < bounds.Right) g.DrawLine(SystemPens.GrayText, lineLeft, y, bounds.Right - LogicalToDeviceUnits(4), y);
    }

    private string GroupHeader(int groupId)
    {
        const int Capacity = 256;
        var buffer = Marshal.AllocHGlobal(Capacity * sizeof(char));
        try
        {
            Marshal.WriteInt16(buffer, 0);
            var group = new LVGROUP
            {
                cbSize = (uint)Marshal.SizeOf<LVGROUP>(),
                mask = LVGF_HEADER,
                pszHeader = buffer,
                cchHeader = Capacity,
            };
            SendMessage(Handle, LVM_GETGROUPINFO, groupId, ref group);
            return Marshal.PtrToStringUni(buffer) ?? "";
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, ref LVGROUP lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NMHDR
    {
        public IntPtr hwndFrom;
        public UIntPtr idFrom;
        public int code;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NMCUSTOMDRAW
    {
        public NMHDR hdr;
        public int dwDrawStage;
        public IntPtr hdc;
        public RECT rc;
        public UIntPtr dwItemSpec;
        public uint uItemState;
        public IntPtr lItemlParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NMLVCUSTOMDRAW
    {
        public NMCUSTOMDRAW nmcd;
        public int clrText;
        public int clrTextBk;
        public int iSubItem;
        public uint dwItemType;
        public int clrFace;
        public int iIconEffect;
        public int iIconPhase;
        public int iPartId;
        public int iStateId;
        public RECT rcText;
        public uint uAlign;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LVGROUP
    {
        public uint cbSize;
        public uint mask;
        public IntPtr pszHeader;
        public int cchHeader;
        public IntPtr pszFooter;
        public int cchFooter;
        public int iGroupId;
        public uint stateMask;
        public uint state;
        public uint uAlign;
        public IntPtr pszSubtitle;
        public uint cchSubtitle;
        public IntPtr pszTask;
        public uint cchTask;
        public IntPtr pszDescriptionTop;
        public uint cchDescriptionTop;
        public IntPtr pszDescriptionBottom;
        public uint cchDescriptionBottom;
        public int iTitleImage;
        public int iExtendedImage;
        public int iFirstItem;
        public uint cItems;
        public IntPtr pszSubsetTitle;
        public uint cchSubsetTitle;
    }
}
