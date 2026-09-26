using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;
using ReTAC.App;
using ReTAC.App.Rendering;
using ReTAC.Domain.Navigation;

namespace ReTAC.Domain.Tests;

/// <summary>R-108-2: 「Windows の設定に従う」のライト用・ダーク用の 8 色の保存・下書き・画面への反映。</summary>
public class SystemColorSetsTests
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    private static readonly OsTheme LightOs = OsTheme.Reference(dark: false);
    private static readonly OsTheme DarkOs = OsTheme.Reference(dark: true);

    private static SettingsDraft DraftOf(AppSettings settings) =>
        SettingsDraft.From(settings, settings.ToKeyMap(), settings.ToTheme(), new QuickAccessList());

    [Fact]
    public void 変えられるのは属性5色とマーク3色()
    {
        Assert.Equal(
            ["MarkBackground", "MarkForeground", "SystemColor", "ReadOnlyColor", "HiddenColor", "CompressedColor", "EncryptedColor", "MarkStarColor"],
            ThemeSlots.SystemMode.Select(s => s.Key));
    }

    [Fact]
    public void ライト用とダーク用を保存して読み戻せ_推奨値と同じ色は保存しない()
    {
        var settings = new AppSettings();
        var draft = DraftOf(settings);
        draft.SystemLight = draft.SystemLight with { HiddenColor = Color.Orange };
        draft.SystemDark = draft.SystemDark with { MarkBackground = Color.Navy, MarkStarColor = Theme.DarkRecommended.MarkStarColor };
        draft.CommitTo(settings, new QuickAccessList());

        Assert.Equal(["HiddenColor"], settings.SystemLightColors.Keys);
        Assert.Equal(["MarkBackground"], settings.SystemDarkColors.Keys);

        var loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, Json), Json)!;
        loaded.Normalize();
        Assert.Equal(Color.Orange.ToArgb(), loaded.ToSystemTheme(dark: false).HiddenColor.ToArgb());
        Assert.Equal(Color.Navy.ToArgb(), loaded.ToSystemTheme(dark: true).MarkBackground.ToArgb());
        Assert.Equal(Theme.DarkRecommended.HiddenColor, loaded.ToSystemTheme(dark: true).HiddenColor);
    }

    [Fact]
    public void null_読めない色_8項目以外のキーは無視して推奨値にする()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""
            { "SystemDarkColors": { "HiddenColor": null, "SystemColor": "not a color", "Background": "#000000", "Bogus": "#123456", "ReadOnlyColor": "#010203" } }
            """, Json)!;
        settings.Normalize();

        var dark = settings.ToSystemTheme(dark: true);
        Assert.Equal(Theme.DarkRecommended.HiddenColor, dark.HiddenColor);
        Assert.Equal(Theme.DarkRecommended.SystemColor, dark.SystemColor);
        Assert.Equal(Theme.DarkRecommended.Background, dark.Background);
        Assert.Equal(ColorTranslator.FromHtml("#010203").ToArgb(), dark.ReadOnlyColor.ToArgb());

        // 保存し直すと、無視したキーは消える
        DraftOf(settings).CommitTo(settings, new QuickAccessList());
        Assert.Equal(["ReadOnlyColor"], settings.SystemDarkColors.Keys);
    }

    [Fact]
    public void キャンセルすると2組とも設定が変わらない()
    {
        var settings = new AppSettings { SystemLightColors = new() { ["HiddenColor"] = "#FFA500" } };
        var draft = DraftOf(settings);
        draft.SystemLight = Theme.Recommended(dark: false);
        draft.SystemDark = draft.SystemDark with { MarkBackground = Color.Navy };
        // CommitTo を呼ばない（キャンセル）

        Assert.Equal(new Dictionary<string, string> { ["HiddenColor"] = "#FFA500" }, settings.SystemLightColors);
        Assert.Empty(settings.SystemDarkColors);
    }

    [Fact]
    public void 起動時の_OS_と反対側の組を変えても画面の色は変わらない()
    {
        var settings = new AppSettings();
        var before = settings.ToScreenTheme(ColorMode.System, LightOs);

        var draft = DraftOf(settings);
        draft.SystemDark = draft.SystemDark with { HiddenColor = Color.Orange, MarkBackground = Color.Navy };
        draft.CommitTo(settings, new QuickAccessList());

        Assert.Equal(before, settings.ToScreenTheme(ColorMode.System, LightOs));
        // 起動時の側の組は、その場で画面に出る
        Assert.Equal(Color.Orange.ToArgb(), settings.ToScreenTheme(ColorMode.System, DarkOs).HiddenColor.ToArgb());
    }

    [Fact]
    public void 独自の配色で起動しているときは8色の変更が画面に出ない()
    {
        var settings = new AppSettings { ColorMode = ColorMode.Custom };
        var before = settings.ToScreenTheme(ColorMode.Custom, LightOs);
        settings.FromSystemTheme(dark: false, Theme.Default with { HiddenColor = Color.Orange });
        Assert.Equal(before, settings.ToScreenTheme(ColorMode.Custom, LightOs));
    }

    [Fact]
    public void 推奨値に戻すは選んでいる組の8色だけを戻す()
    {
        var settings = new AppSettings();
        var custom = Theme.Default with { HiddenColor = Color.Orange, FontSize = 14f };
        var draft = SettingsDraft.From(settings, settings.ToKeyMap(), custom, new QuickAccessList());
        draft.SystemLight = draft.SystemLight with { HiddenColor = Color.Red };
        draft.SystemDark = draft.SystemDark with { HiddenColor = Color.Blue };

        using var page = new ColorFontPage(draft);
        page.SelectSystemSide(dark: true);
        // 組のラジオボタンがモードのラジオボタンと同じグループだと、ここで「独自の配色」に切り替わる
        Assert.Equal(ColorMode.System, draft.ColorMode);
        page.ResetSystemColors();

        Assert.Equal(Theme.DarkRecommended.HiddenColor, draft.SystemDark.HiddenColor);
        Assert.Equal(Color.Red, draft.SystemLight.HiddenColor);
        Assert.Equal(Color.Orange, draft.Theme.HiddenColor);
        Assert.Equal(14f, draft.Theme.FontSize);
    }
}
