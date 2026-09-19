using System.Reflection;
using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>ToolStrip の標準の動きで足りないところを補う（実機指摘）。</summary>
public static class ToolStripExtras
{
    /// <summary>「»」のボタンの左右の余白（96 dpi）。標準の 16px に足して幅 28px にする。</summary>
    private const int OverflowSide = 6;

    /// <summary>「»」のボタンの幅（96 dpi）。ドライブバーの「»」もこの幅にそろえる。</summary>
    public const int OverflowWidth = 16 + OverflowSide * 2;

    /// <summary>帯の右端の余白（96 dpi）。「»」が窓の角丸に掛かって押しにくく見にくくならないようにする。</summary>
    private const int RightMargin = 6;

    /// <summary>
    /// 入りきらない項目を回す「»」のボタンは、標準では幅 16px で右端に小さな矢印を描くだけで、押しにくく見にくい（実機指摘）。
    /// 左右に余白を足して広げ、窓の角丸に掛からないよう右に間を取り、中央に大きめの「»」を描く。
    /// 余白は DPI に合わせて項目を拡大し直すときに既定へ戻されるので、並べ直すたびに掛け直す。
    /// </summary>
    public static void WidenOverflow(ToolStrip strip)
    {
        strip.Renderer = new OverflowRenderer();
        strip.Layout += (_, _) =>
        {
            var button = strip.OverflowButton;
            var side = strip.LogicalToDeviceUnits(OverflowSide);
            var padding = new Padding(side, 0, side, 0);
            if (button.Padding != padding) button.Padding = padding;
            var margin = button.Margin with { Right = strip.LogicalToDeviceUnits(RightMargin) };
            if (button.Margin != margin) button.Margin = margin;
        };
    }

    /// <summary>標準の描き方のまま、「»」のボタンだけを描き替える。</summary>
    private sealed class OverflowRenderer : ToolStripProfessionalRenderer
    {
        /// <summary>帯の枠は描かない（既定の描き方でも見えていなかった。差し替えると角丸の枠が出る）。</summary>
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }

        protected override void OnRenderOverflowButtonBackground(ToolStripItemRenderEventArgs e)
        {
            var bounds = new System.Drawing.Rectangle(System.Drawing.Point.Empty, e.Item.Size);
            if (e.Item.Pressed || e.Item.Selected)
            {
                e.Graphics.FillRectangle(System.Drawing.SystemBrushes.ControlLight, bounds);
                e.Graphics.DrawRectangle(System.Drawing.SystemPens.ControlDark, bounds with { Width = bounds.Width - 1, Height = bounds.Height - 1 });
            }
            using var font = new System.Drawing.Font(e.ToolStrip!.Font.FontFamily, e.ToolStrip.Font.Size * 1.4f);
            TextRenderer.DrawText(e.Graphics, "»", font, bounds, System.Drawing.SystemColors.ControlText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    // ponytail: ToolStripDropDownMenu は、上下の ▲▼ で 1 段ずつ送る処理を持っているが公開していない。
    // リフレクションで呼ぶ。将来の WinForms で名前が変わったら null になり、ホイールが効かないだけで済む
    private static readonly MethodInfo? ScrollOneRow = typeof(ToolStripDropDownMenu).GetMethod(
        "ScrollInternal", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(bool)]);

    /// <summary>開いたメニューを、その項目が見えるところまで下へ送る（パンくずの ▸ の一覧で今いる経路を見せる。R-94）。</summary>
    public static void ScrollIntoView(ToolStripDropDownMenu menu, ToolStripItem item)
    {
        if (ScrollOneRow is null) return;
        object[] down = [false];
        while (item.Bounds.Bottom > menu.DisplayRectangle.Bottom && CanScroll(menu, up: false))
        {
            try { ScrollOneRow.Invoke(menu, down); }
            catch (TargetInvocationException) { return; }
        }
    }

    /// <summary>その向きに、まだ隠れた項目があるか（▲▼ が押せるか）。</summary>
    private static bool CanScroll(ToolStripDropDownMenu menu, bool up)
    {
        var shown = menu.Items.Cast<ToolStripItem>().Where(i => i.Available).ToList();
        if (shown.Count == 0) return false;
        var area = menu.DisplayRectangle;
        return up ? shown[0].Bounds.Top < area.Top : shown[^1].Bounds.Bottom > area.Bottom;
    }

    /// <summary>
    /// 項目が画面の高さに入りきらないメニューを、ホイールで送れるようにする（標準は ▲▼ を押すしかない）。
    /// 項目が多いときに使うので、1 ノッチで 3 段以上（Windows の「一度にスクロールする行数」と 3 の大きい方）送る。
    /// </summary>
    public static void EnableWheel(ToolStripDropDown dropDown)
    {
        if (ScrollOneRow is null || dropDown is not ToolStripDropDownMenu menu) return;
        menu.MouseWheel += (_, e) =>
        {
            var rows = Math.Max(3, SystemInformation.MouseWheelScrollLines) * Math.Abs(e.Delta) / 120;
            var up = e.Delta > 0;
            object[] args = [up];
            for (var i = 0; i < rows && CanScroll(menu, up); i++)
            {
                // WinForms は ▲▼ が押せるときにしか呼ばない。端を越えて送ったり、展開表示が項目を差し替えた直後に
                // 送ったりすると中で例外になる（速く回したときの実機指摘）。そうなったら、そのノッチは送るのをやめる
                try { ScrollOneRow.Invoke(menu, args); }
                catch (TargetInvocationException) { break; }
            }
        };
    }
}
