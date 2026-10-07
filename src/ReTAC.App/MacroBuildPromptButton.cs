using System.Drawing;
using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>
/// R-145-1: 外部ツールの設定ページとマクロの一覧に置く「マクロ構築プロンプト ▼」と案内の行。
/// コピーしたら案内の行を数秒だけ差し替える（ダイアログではステータスバーが見えないため）。
/// </summary>
internal static class MacroBuildPromptButton
{
    private const int NoticeMilliseconds = 3000;
    private const int ButtonWidth = 190;
    private const int Gap = 8;

    /// <param name="guideRight">案内の行の右端。右の操作ボタンの手前にする（重ねない）</param>
    /// <param name="anchor">ボタンの Anchor。案内の行には Right も足して、残りの幅に合わせて伸び縮みさせる</param>
    public static (Button Button, Label Guide) Attach(Control owner, Point location, int guideRight, AnchorStyles anchor)
    {
        var button = new Button { Text = "マクロ構築プロンプト ▼", Bounds = new Rectangle(location, new Size(ButtonWidth, 28)), Anchor = anchor };
        var guideLeft = location.X + ButtonWidth + Gap;
        var guide = new Label
        {
            Text = MacroBuildPromptActions.Guide,
            Bounds = new Rectangle(guideLeft, location.Y, Math.Max(0, guideRight - guideLeft), 34),
            Anchor = anchor | AnchorStyles.Right,
            AutoEllipsis = true,   // 最小の幅でも入りきらないときは末尾を省略する（全文はツールチップ）
        };
        var tips = new ToolTip();
        tips.SetToolTip(guide, MacroBuildPromptActions.Guide);

        // 1 つのタイマーを使い回す。差し替え中にもう一度コピーしたら延長し、閉じたら止める
        var timer = new System.Windows.Forms.Timer { Interval = NoticeMilliseconds };
        timer.Tick += (_, _) => { timer.Stop(); guide.Text = MacroBuildPromptActions.Guide; };

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(MacroBuildPromptActions.MenuItems(
            export: () => MacroBuildPromptActions.Export(owner),
            copy: () =>
            {
                if (!MacroBuildPromptActions.Copy(owner)) return;
                guide.Text = MacroBuildPromptActions.CopiedMessage;
                timer.Stop();
                timer.Start();
            }));
        button.Click += (_, _) => menu.Show(button, new Point(0, button.Height));
        owner.Disposed += (_, _) =>
        {
            timer.Dispose();
            menu.Dispose();
            tips.Dispose();
        };

        owner.Controls.Add(button);
        owner.Controls.Add(guide);
        return (button, guide);
    }
}
