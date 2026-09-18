using System.Text.Json;
using System.Text.Json.Serialization;
using ReTAC.App;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Navigation;

namespace ReTAC.Domain.Tests;

/// <summary>Q10 / Q12: 読み込み直後の正規化と、クイックアクセスの共有</summary>
public class SettingsNormalizeTests
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public void 読み込み直後に消えたツールとグループと読めないコマンドを取り除く()
    {
        var gone = new ToolTarget(999).Serialize();
        var settings = JsonSerializer.Deserialize<AppSettings>($$"""
            {
              "QuickAccess": [
                { "Title": "", "Path": "C:\\a" },
                { "Title": "", "Path": "{{gone}}", "Kind": "Command" },
                { "Title": "g", "Path": "", "Kind": "Group" }
              ],
              "Bookmarks": { "Other": [ { "Title": "t", "Kind": "Group", "Children": [
                { "Title": "t", "Kind": "Command", "Target": "{{gone}}" },
                { "Title": "r", "Kind": "Command", "Target": "Refresh" } ] } ] }
            }
            """, Json)!;

        settings.Normalize();

        Assert.Equal([@"C:\a"], settings.QuickAccess.Select(e => e.Path));
        Assert.Equal(["Refresh"], settings.Bookmarks.Other[0].Children!.Select(b => b.Target));
        var saved = JsonSerializer.Serialize(settings, Json);
        Assert.DoesNotContain(gone, saved);
        Assert.Equal([@"C:\a"], QuickAccessHost.For(settings).Items.Select(e => e.Path));
    }

    [Fact]
    public void クイックアクセスは同じ設定なら同じ一覧を共有する()
    {
        var settings = new AppSettings();
        Assert.Same(QuickAccessHost.For(settings), QuickAccessHost.For(settings));
        Assert.NotSame(QuickAccessHost.For(settings), QuickAccessHost.For(new AppSettings()));
    }

    [Fact]
    public void ブックマークの設定の既定()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{}", Json)!;
        Assert.True(settings.ShowBookmarkBar);
        Assert.Empty(settings.Bookmarks.Bar);
        Assert.Equal(BookmarkBarStyle.IconAndText, settings.BookmarkBarStyle);
    }
}
