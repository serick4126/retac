using System.IO;
using System.Text.Json;
using ReTAC.App;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-68: 設定「複数選択の時外部ツールの連続起動はしない」は廃止した。古い設定ファイルにキーが残っていても、
/// 読み捨てるだけで、ほかの設定は失われない（移行のコードは書かない。INV-NO-SETTINGS-MIGRATION）。
/// </summary>
public class SuppressMultipleRemovedTests
{
    [Fact]
    public void 古いキーを含む設定を読んでも_ほかの設定は読める()
    {
        var old = """{ "Resident": true, "SuppressMultipleToolLaunch": true, "FontSize": 20 }""";

        var settings = JsonSerializer.Deserialize<AppSettings>(old, AppSettings.Json)!;

        Assert.True(settings.Resident);
        Assert.Equal(20f, settings.FontSize);
    }

    [Fact]
    public void 書き出した設定にキーが無く_型にも残っていない()
    {
        var json = JsonSerializer.Serialize(new AppSettings(), AppSettings.Json);
        Assert.DoesNotContain("SuppressMultipleToolLaunch", json);
        Assert.Null(typeof(AppSettings).GetProperty("SuppressMultipleToolLaunch"));
        Assert.Null(typeof(SettingsDraft).GetProperty("SuppressMultipleToolLaunch"));
    }

    [Fact]
    public void 動作環境のページにチェックボックスが無い()
    {
        using var environment = new EnvironmentPage(new SettingsDraft());
        Assert.DoesNotContain(environment.Controls.OfType<System.Windows.Forms.CheckBox>(), box => box.Text.Contains("連続起動"));
    }

    [Theory]
    [InlineData("EXTERNAL_TOOLS.md")]
    [InlineData("README.md")]
    public void 公開している説明の文書が_廃止した設定を案内していない(string file)
    {
        // CHANGELOG.md は調べない（廃止したことを、この名前で書く）
        var text = File.ReadAllText(Path.Combine(SchemaManifest.RepoRoot(), file));
        Assert.DoesNotContain("連続起動はしない", text);
    }
}
