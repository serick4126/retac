using System.Text.Json;
using ReTAC.Domain.Listing;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-116〜R-119 / R-118: Phase 16 の本体が使う設定の項目は Phase 14 で先に作ってある。
/// Phase 16 の設定の項目は Phase 14 で先に作ってあり、既定値も仕様の表のまま（実機確認で足した RangeSelection だけが例外）。
/// </summary>
public class Phase16SettingsGateTests
{
    [Fact]
    public void 既定値は仕様の表のとおり()
    {
        var views = new FileViewSettings();
        Assert.True(views.Common.ShowOverlays);
        Assert.Equal(CheckBoxMode.HoverAndMarked, views.Icons.CheckBoxes);
        Assert.True(views.Icons.Thumbnails);
        Assert.True(views.Icons.FolderThumbnails);
        Assert.Equal(2, views.Icons.NameLines);
        Assert.Equal(NameWidthMode.MaxChars, views.Icons.SmallIconWidth.Mode);
        Assert.Equal(40, views.Icons.SmallIconWidth.MaxChars);
        Assert.Equal(CheckBoxMode.HoverAndMarked, views.Tiles.CheckBoxes);
        Assert.True(views.Tiles.Thumbnails);
        Assert.True(views.Tiles.FolderThumbnails);
    }

    [Fact]
    public void マニフェストの設定に項目がそろっている()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(SchemaManifest.RepoRoot(), "schema", "manifest.json")));
        var types = doc.RootElement.GetProperty("types");
        string[] Fields(string type) => types.GetProperty(type).GetProperty("fields").EnumerateArray()
            .Select(f => f.GetProperty("name").GetString()!).ToArray();
        Assert.Contains("ShowOverlays", Fields("ReTAC.Domain.Listing.FileViewCommonSettings"));
        Assert.Superset(new HashSet<string> { "CheckBoxes", "Thumbnails", "FolderThumbnails", "NameLines", "SmallIconWidth" },
            Fields("ReTAC.Domain.Listing.IconsViewSettings").ToHashSet());
        Assert.Superset(new HashSet<string> { "CheckBoxes", "Thumbnails", "FolderThumbnails" },
            Fields("ReTAC.Domain.Listing.TilesViewSettings").ToHashSet());
    }
}
