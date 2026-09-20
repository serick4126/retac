using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>R-96: Phase 10 の段階実装中だけ、未実装ビューの領域内に表示する案内。</summary>
public sealed class UnavailableLeftPanelView : UserControl
{
    public UnavailableLeftPanelView()
    {
        Controls.Add(new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = "このビューは Phase 10 の後続段で実装します",
            TextAlign = ContentAlignment.MiddleCenter,
        });
    }
}
