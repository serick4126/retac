using System.Windows.Forms;
using ReTAC.App;
using ReTAC.App.Rendering;
using ReTAC.Domain.Navigation;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-102: 統合設定画面の枠を作り（表示はしない）、6 ページがそれぞれ枠のページ領域に収まり、
/// ページの子コントロールがページの <see cref="Control.ClientRectangle"/> の中に収まることを確かめる。
/// テスト実行環境の DPI（通常 100%）での検査であり、150% での確認は実機で行う。
/// </summary>
public class SettingsDialogLayoutTests
{
    [Fact]
    public void 全ページが枠の中に収まる()
    {
        var settings = new AppSettings();
        var draft = SettingsDraft.From(settings, settings.ToKeyMap(), Theme.Default, new QuickAccessList());
        using var dialog = new SettingsDialog(draft, SettingsPage.Environment, @"C:\", _ => true);

        // 6 ページは UserControl、サイドバーは ListBox、下端は Button なので型で見分けられる
        var pages = dialog.Controls.OfType<UserControl>().ToList();
        Assert.Equal(6, pages.Count);

        var sidebar = dialog.Controls.OfType<ListBox>().Single();

        foreach (var page in pages)
        {
            Assert.True(page.Left >= sidebar.Right, $"{page.GetType().Name} がサイドバーと重なっている");
            Assert.True(page.Top >= 0 && page.Left >= 0, $"{page.GetType().Name} の位置が負");
            Assert.True(page.Right <= dialog.ClientSize.Width, $"{page.GetType().Name} が右にはみ出している");
            Assert.True(page.Bottom <= dialog.ClientSize.Height, $"{page.GetType().Name} が下にはみ出している");

            AssertChildrenWithin(page);
        }
    }

    private static void AssertChildrenWithin(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            Assert.True(child.Left >= 0 && child.Top >= 0,
                $"{parent.GetType().Name} の中の {child.GetType().Name} の位置が負");
            Assert.True(child.Right <= parent.ClientSize.Width && child.Bottom <= parent.ClientSize.Height,
                $"{parent.GetType().Name} の中の {child.GetType().Name} がはみ出している");
            AssertChildrenWithin(child);
        }
    }
}
