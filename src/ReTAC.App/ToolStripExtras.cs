using System.Reflection;
using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>ToolStrip の標準の動きで足りないところを補う（実機指摘）。</summary>
public static class ToolStripExtras
{
    /// <summary>「»」のボタンの幅（96 dpi）。ドライブバーの「»」もこの幅にそろえる。</summary>
    public const int OverflowWidth = 24;

    /// <summary>
    /// 入りきらない項目を回す「»」のボタンは標準では幅が数 px しかなく押しにくい。
    /// Padding では広がらない（幅を自分で決める）ので、大きさを固定する。高さは帯に合わせ続ける。
    /// </summary>
    public static void WidenOverflow(ToolStrip strip)
    {
        var button = strip.OverflowButton;
        button.AutoSize = false;
        void Fit()
        {
            var size = new System.Drawing.Size(strip.LogicalToDeviceUnits(OverflowWidth), strip.DisplayRectangle.Height);
            if (button.Size != size) button.Size = size;
        }
        Fit();
        strip.SizeChanged += (_, _) => Fit();
        strip.DpiChangedAfterParent += (_, _) => Fit();
    }

    // ponytail: ToolStripDropDownMenu は、上下の ▲▼ で 1 段ずつ送る処理を持っているが公開していない。
    // リフレクションで呼ぶ。将来の WinForms で名前が変わったら null になり、ホイールが効かないだけで済む
    private static readonly MethodInfo? ScrollOneRow = typeof(ToolStripDropDownMenu).GetMethod(
        "ScrollInternal", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(bool)]);

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
            object[] up = [e.Delta > 0];
            for (var i = 0; i < rows; i++) ScrollOneRow.Invoke(menu, up);
        };
    }
}
