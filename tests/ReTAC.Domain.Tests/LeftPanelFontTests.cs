using System.Text.Json;
using System.Windows.Forms;
using ReTAC.App;
using ReTAC.App.Rendering;

namespace ReTAC.Domain.Tests;

/// <summary>R-101: 左パネルのフォントは、ファイル一覧のフォントとは別に持つ。</summary>
public class LeftPanelFontTests
{
    [Fact]
    public void 初回と欠けたJSONでメイリオ12ptになる()
    {
        foreach (var settings in new[] { new AppSettings(), JsonSerializer.Deserialize<AppSettings>("{}")! })
        {
            Assert.Null(settings.LeftPanelFontFamily);
            Assert.Null(settings.LeftPanelFontSize);

            var theme = settings.ToTheme();
            Assert.Equal("メイリオ", theme.LeftPanelFontFamily);
            Assert.Equal(12f, theme.LeftPanelFontSize);
        }
    }

    [Fact]
    public void 一覧のフォントを変えても左パネルは変わらない()
    {
        var theme = Theme.Default with { FontFamily = "Consolas", FontSize = 20f };

        Assert.Equal("メイリオ", theme.LeftPanelFontFamily);
        Assert.Equal(12f, theme.LeftPanelFontSize);
    }

    [Fact]
    public void 既定と同じなら設定ファイルへ書かない()
    {
        var settings = new AppSettings();
        settings.FromTheme(Theme.Default);

        Assert.Null(settings.LeftPanelFontFamily);
        Assert.Null(settings.LeftPanelFontSize);
    }

    [Fact]
    public void 変えた値は往復する()
    {
        var settings = new AppSettings();
        settings.FromTheme(Theme.Default with { LeftPanelFontFamily = "Yu Gothic UI", LeftPanelFontSize = 10.5f });

        Assert.Equal("Yu Gothic UI", settings.LeftPanelFontFamily);
        Assert.Equal(10.5f, settings.LeftPanelFontSize);

        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!.ToTheme();
        Assert.Equal("Yu Gothic UI", restored.LeftPanelFontFamily);
        Assert.Equal(10.5f, restored.LeftPanelFontSize);
    }

    /// <summary>
    /// R-101: 一覧と左パネルを同じ大きさにすると、プレビューが赤い × になっていた。
    /// Control.Font は値の等しい Font を代入しても差し替えず前の実体を持ち続けるので、
    /// 「前のもの」として捨てると、まだ描画に使われているフォントを壊す。
    /// </summary>
    [Fact]
    public void 一覧と同じ大きさにしてもプレビューのフォントが壊れない()
    {
        var theme = Theme.Default with { LeftPanelFontFamily = "メイリオ", LeftPanelFontSize = Theme.Default.FontSize };
        using var dialog = new ColorFontDialog(theme);
        var targets = dialog.Controls.OfType<ListBox>().Single();

        targets.SelectedIndex = 1;   // 左パネル（一覧と同じ メイリオ 16pt）
        targets.SelectedIndex = 0;   // ファイル一覧
        targets.SelectedIndex = 1;

        // 捨てたフォントを使っていると、ここで ArgumentException("Parameter is not valid") になる
        var preview = dialog.Controls.OfType<Control>().Single(c => c.GetType().Name == "PreviewBox");
        Assert.True(preview.Font.Height > 0);
    }
}
