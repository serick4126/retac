using System.Drawing;
using System.Windows.Forms;
using ReTAC.App;
using ReTAC.App.Rendering;
using ReTAC.Domain.Navigation;

namespace ReTAC.Domain.Tests;

/// <summary>R-108: ファイルリストに渡す配色の解決と、配色モードの保存。</summary>
public class ThemeResolveTests
{
    [Fact]
    public void 既定のモードは_Windows_の設定に従う() => Assert.Equal(ColorMode.System, new AppSettings().ColorMode);

    [Fact]
    public void 独自の配色は保存された色をそのまま使う()
    {
        if (SystemInformation.HighContrast) return;   // ハイコントラストでは常に OS の色（R-108-3）
        var stored = Theme.Default with { Background = Color.Black, HiddenColor = Color.Orange };
        Assert.Same(stored, Theme.Resolve(stored, ColorMode.Custom));
    }

    [Fact]
    public void Windows_の設定に従うと背景と文字はツリーと同じ_OS_の色になりフォントは保存値を使う()
    {
        var stored = Theme.Default with { Background = Color.Black, Foreground = Color.Yellow, FontFamily = "MS Gothic", FontSize = 11f };
        var resolved = Theme.Resolve(stored, ColorMode.System);
        Assert.Equal(SystemColors.Window, resolved.Background);
        Assert.Equal(SystemColors.WindowText, resolved.Foreground);
        Assert.Equal("MS Gothic", resolved.FontFamily);
        Assert.Equal(11f, resolved.FontSize);
    }

    [Theory]
    [InlineData("#2864B4", "#000000", "#FFFFFF")]   // ダークの組。OS の黒では比 3.6 しかない
    [InlineData("#0078D7", "#FFFFFF", "#FFFFFF")]   // ライトの組は読めるのでそのまま（黒の 4.6 に僅差で負けても変えない）
    [InlineData("#FFD700", "#FFFFFF", "#000000")]   // 明るいアクセントカラーでは黒のほうが読める
    public void カーソルの文字は_OS_の組が読めればそれを使い読めなければ白黒の読めるほう(string background, string osText, string expected) =>
        Assert.Equal(ColorTranslator.FromHtml(expected).ToArgb(),
                     Theme.CursorTextOn(ColorTranslator.FromHtml(background), ColorTranslator.FromHtml(osText)).ToArgb());

    [Fact]
    public void 配色モードは下書きを経て保存される()
    {
        var settings = new AppSettings();
        var draft = SettingsDraft.From(settings, settings.ToKeyMap(), settings.ToTheme(), new QuickAccessList());
        Assert.Equal(ColorMode.System, draft.ColorMode);
        draft.ColorMode = ColorMode.Custom;
        Assert.Equal(ColorMode.System, settings.ColorMode);   // 確定までは変えない（R-102-3）
        draft.CommitTo(settings, new QuickAccessList());
        Assert.Equal(ColorMode.Custom, settings.ColorMode);
    }
}
