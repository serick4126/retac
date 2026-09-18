using System.Reflection;
using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>ToolStrip の標準の動きで足りないところを補う（実機指摘）。</summary>
public static class ToolStripExtras
{
    /// <summary>
    /// 入りきらない項目を回す「»」のボタンは標準では幅が数 px しかなく押しにくい。左右に余白を足して広げる。
    /// </summary>
    public static void WidenOverflow(ToolStrip strip)
    {
        var side = strip.LogicalToDeviceUnits(8);
        strip.OverflowButton.Padding = new Padding(side, 0, side, 0);
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
