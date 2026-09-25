using System.Text.Json;
using System.Text.Json.Serialization;
using ReTAC.App;
using ReTAC.Domain.Commands;
using ReTAC.Domain.Navigation;
using ReTAC.Domain.Tools;

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
    public void 明示的な_null_は既定値に戻して起動を止めない()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""
            { "QuickAccess": null, "ExternalTools": null, "Bookmarks": null, "KeyBindings": null,
              "HiddenDrives": null, "FolderHistory": null, "DriveFolders": null, "Colors": null }
            """, Json)!;

        settings.Normalize();

        Assert.Empty(settings.QuickAccess);
        Assert.Equal(DefaultExternalTools.Create().Count, settings.ExternalTools.Count);
        Assert.Empty(settings.Bookmarks.Bar);
        Assert.NotNull(settings.ToKeyMap());
        Assert.NotNull(settings.ToTheme());
        Assert.Empty(settings.ToHiddenDrives());
        Assert.Empty(settings.ToFolderHistory().Recent);
    }

    [Fact]
    public void 要素や欄の_null_と欠けた欄と入れ子を混ぜても正規化できる()
    {
        var gone = new ToolTarget(999).Serialize();
        var settings = JsonSerializer.Deserialize<AppSettings>($$"""
            {
              "QuickAccess": [ null, { "Title": null, "Path": "C:\\a" }, { "Path": null } ],
              "ExternalTools": [ null, { "Id": 1, "Name": null, "Path": null } ],
              "KeyBindings": { "C": null },
              "Bookmarks": { "Bar": null, "Other": [ null,
                { "Title": null, "Kind": "Folder", "Target": "C:\\x" },
                { "Title": "t", "Kind": "Command", "Target": null },
                { "Title": "NoSuchCommand", "Kind": "Command", "Target": "NoSuchCommand" },
                { "Title": "g", "Kind": "Group", "Children": [ null,
                  { "Title": "t", "Kind": "Command", "Target": "{{gone}}" },
                  { "Title": "r", "Kind": "Command", "Target": "Refresh" } ] } ] }
            }
            """, Json)!;

        settings.Normalize();

        Assert.Equal(["", @"C:\a"], settings.QuickAccess.Select(e => e.Path).Order());
        Assert.Equal("", settings.ExternalTools.Single().Name);
        Assert.Empty(settings.Bookmarks.Bar);
        Assert.Equal([BookmarkKind.Folder, BookmarkKind.Group], settings.Bookmarks.Other.Select(b => b.Kind));
        Assert.Equal("", settings.Bookmarks.Other[0].Title);
        Assert.Equal(["Refresh"], settings.Bookmarks.Other[1].Children!.Select(b => b.Target));
        Assert.NotNull(settings.ToKeyMap());
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
        // B-22: Bookmarks の欄が無ければ初期の 6 件をバーに置く
        var settings = JsonSerializer.Deserialize<AppSettings>("{}", Json)!;
        Assert.True(settings.ShowBookmarkBar);
        Assert.Equal(6, settings.Bookmarks.Bar.Count);
        Assert.Empty(settings.Bookmarks.Other);
        Assert.Equal(BookmarkBarStyle.IconAndText, settings.BookmarkBarStyle);
    }

    [Fact]
    public void Bookmarksの欄が空で入っていれば増やさない()
    {
        // B-22: 利用者がブックマークを全部消した状態を、初期の 6 件で埋め戻さない（移行コードは書かない）
        var settings = JsonSerializer.Deserialize<AppSettings>("""
            { "Bookmarks": { "Bar": [], "Other": [] } }
            """, Json)!;
        Assert.Empty(settings.Bookmarks.Bar);
        Assert.Empty(settings.Bookmarks.Other);
    }
}
