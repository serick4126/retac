using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>
/// R-88: ReTAC が描くメニューの項目の上下に余白を足す。すべてのメニューで揃えるため、値はここ 1 か所に置く。
/// 値は実機で決めてコードに固定する（設定にしない）。OS が描くメニュー（シェルのメニュー）は変えられないのでそのまま。
/// </summary>
public static class MenuSpacing
{
    /// <summary>96 dpi での上下の余白。項目の高さ 22px → 28px。</summary>
    private const int Vertical = 4;

    public static void Apply(ToolStripItemCollection items, int dpi)
    {
        var v = Vertical * dpi / 96;
        foreach (ToolStripItem item in items)
        {
            if (item is ToolStripSeparator) continue;
            item.Padding = new Padding(item.Padding.Left, v, item.Padding.Right, v);
            // DropDownItems は触るだけでドロップダウンを作るので、持っているときだけ降りる
            if (item is ToolStripDropDownItem { HasDropDownItems: true } parent) Apply(parent.DropDownItems, dpi);
        }
    }
}
