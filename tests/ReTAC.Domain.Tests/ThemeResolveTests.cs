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

    // 起動時の OS の状態を作って渡す。今の OS の状態に左右されないことも、これで確かめられる
    private static readonly OsTheme Light = new()
    {
        Dark = false, HighContrast = false,
        Window = Color.White, WindowText = Color.Black,
        Highlight = ColorTranslator.FromHtml("#0078D7"), HighlightText = Color.White,
    };
    private static readonly OsTheme Dark = Light with
    {
        Dark = true,
        Window = ColorTranslator.FromHtml("#323232"), WindowText = Color.White,
        Highlight = ColorTranslator.FromHtml("#2864B4"), HighlightText = Color.Black,
    };
    private static readonly OsTheme HighContrast = Light with
    {
        HighContrast = true,
        Window = Color.Black, WindowText = Color.Yellow,
        Highlight = Color.Cyan, HighlightText = Color.Black,
    };

    [Fact]
    public void 独自の配色は保存された色をそのまま使う()
    {
        var stored = Theme.Default with { Background = Color.Black, HiddenColor = Color.Orange };
        Assert.Same(stored, Theme.Resolve(stored, ColorMode.Custom, Light));
    }

    [Fact]
    public void Windows_の設定に従うと背景と文字はツリーと同じ_OS_の色になりフォントは保存値を使う()
    {
        var stored = Theme.Default with { Background = Color.Black, Foreground = Color.Yellow, FontFamily = "MS Gothic", FontSize = 11f };
        var resolved = Theme.Resolve(stored, ColorMode.System, Dark);
        Assert.Equal(Dark.Window, resolved.Background);
        Assert.Equal(Dark.WindowText, resolved.Foreground);
        Assert.Equal("MS Gothic", resolved.FontFamily);
        Assert.Equal(11f, resolved.FontSize);
    }

    [Fact]
    public void ライトで起動していれば_今の_OS_がダークでもライトの色で解決する()
    {
        // 起動後に OS をダークにしてから設定を適用しても、起動時の白い地にダーク用の色を載せない
        var resolved = Theme.Resolve(Theme.Default, ColorMode.System, Light);
        Assert.Equal(Color.White, resolved.Background);
        Assert.Equal(Theme.Default.MarkBackground, resolved.MarkBackground);
        Assert.Equal(Theme.Default.HiddenColor, resolved.HiddenColor);
        Assert.Equal(Theme.Default.CursorBackground, resolved.CursorBackground);
        Assert.Equal(Color.White, resolved.CursorForeground);
    }

    [Fact]
    public void ダークで起動していればダーク用の推奨値とOSの選択色で解決する()
    {
        var resolved = Theme.Resolve(Theme.Default, ColorMode.System, Dark);
        Assert.Equal(Theme.DarkRecommended.MarkBackground, resolved.MarkBackground);
        Assert.Equal(Theme.DarkRecommended.HiddenColor, resolved.HiddenColor);
        Assert.Equal(Dark.Highlight, resolved.CursorBackground);
        Assert.Equal(Color.White, resolved.CursorForeground);
    }

    [Fact]
    public void ハイコントラストで起動していればどのモードでも_OS_の色だけで描く()
    {
        foreach (var mode in new[] { ColorMode.System, ColorMode.Custom })
        {
            var resolved = Theme.Resolve(Theme.Default with { HiddenColor = Color.Orange }, mode, HighContrast);
            Assert.Equal(HighContrast.Window, resolved.Background);
            Assert.Equal(HighContrast.WindowText, resolved.HiddenColor);
            Assert.Equal(HighContrast.Highlight, resolved.CursorBackground);
            Assert.Equal(HighContrast.HighlightText, resolved.CursorForeground);
        }
    }

    [Fact]
    public void Windows_の設定に従うときの既定に戻すは隠れている独自の配色を消さない()
    {
        var settings = new AppSettings { ColorMode = ColorMode.System };
        var custom = Theme.Default with { HiddenColor = Color.Orange, FontSize = 14f };
        var draft = SettingsDraft.From(settings, settings.ToKeyMap(), custom, new QuickAccessList());
        using var page = new ColorFontPage(draft);
        page.ResetTarget();
        Assert.Equal(Color.Orange, draft.Theme.HiddenColor);
        Assert.Equal(Theme.Default.FontSize, draft.Theme.FontSize);

        draft.ColorMode = ColorMode.Custom;
        page.ResetTarget();
        Assert.Equal(Theme.Default.HiddenColor, draft.Theme.HiddenColor);
    }

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
