using System.Text.Json;
using System.Text.Json.Serialization;
using ReTAC.Domain.Listing;

namespace ReTAC.Domain.Tests;

/// <summary>R-112-3 と Phase 16 で足した項目の既定値、範囲の検証、手で書いた JSON の正規化</summary>
public class FileViewSettingsTests
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public void 既定値は仕様書の表のとおり()
    {
        var s = new FileViewSettings();
        Assert.True(s.Common.ShowOverlays);                                                  // R-118
        Assert.False(s.List.InPanelDragDrop);                                                // R-110: 一覧だけオフ
        Assert.True(s.Details.InPanelDragDrop);
        Assert.True(s.Icons.InPanelDragDrop);
        Assert.True(s.Tiles.InPanelDragDrop);
        Assert.True(s.List.RangeSelection && s.Details.RangeSelection && s.Icons.RangeSelection && s.Tiles.RangeSelection);   // Phase 16 §10: 既定はオン
        Assert.Equal(new NameWidthSetting { Mode = NameWidthMode.ShowAll, MaxChars = 40 }, s.List.NameWidth);
        Assert.Equal(new NameWidthSetting { Mode = NameWidthMode.ShowAll, MaxChars = 40 }, s.Details.NameWidth);
        Assert.False(s.Common.HideKnownExtensions);                                          // R-01-7
        Assert.True(s.List.AlignExtension);                                                  // R-01-6
        Assert.True(s.Details.AlignExtension);
        Assert.Equal([DetailsColumn.Size, DetailsColumn.Modified, DetailsColumn.Type, DetailsColumn.Attributes],
            s.Details.Columns.Where(c => c.Visible).Select(c => c.Column));
        Assert.Equal(6, s.Details.Columns.Count);                                            // 非表示の列も並びを持つ
        Assert.False(s.Details.FitColumnsToWindow);
        Assert.Equal((48, 96, 256), (s.Icons.MediumSize, s.Icons.LargeSize, s.Icons.ExtraLargeSize));
        Assert.Equal(CheckBoxMode.HoverAndMarked, s.Icons.CheckBoxes);                       // R-116
        Assert.True(s.Icons.Thumbnails);                                                     // R-117
        Assert.True(s.Icons.FolderThumbnails);
        Assert.Equal(2, s.Icons.NameLines);                                                  // R-119
        Assert.Equal(new NameWidthSetting { Mode = NameWidthMode.MaxChars, MaxChars = 40 }, s.Icons.SmallIconWidth);
        Assert.Equal([TileInfo.Type, TileInfo.Size], s.Tiles.Info);
        Assert.Equal((48, 48), (s.Tiles.TilesSize, s.Tiles.ContentSize));
        Assert.Equal(CheckBoxMode.HoverAndMarked, s.Tiles.CheckBoxes);
        Assert.True(s.Tiles.Thumbnails);
        Assert.True(s.Tiles.FolderThumbnails);
    }

    [Fact]
    public void 欠けた欄と項目は既定値で埋まる()
    {
        var s = FileViewSettings.Normalize(JsonSerializer.Deserialize<FileViewSettings>("""{ "List": { "InPanelDragDrop": true } }""", Json));
        Assert.True(s.List.InPanelDragDrop);
        Assert.Equal(new NameWidthSetting(), s.List.NameWidth);
        Assert.Equal(new FileViewCommonSettings(), s.Common);
        Assert.False(s.Common.HideKnownExtensions);
        Assert.True(s.List.AlignExtension);       // 欠けた R-01-6 の項目は既定値
        Assert.True(s.Details.AlignExtension);
        Assert.True(s.Details.InPanelDragDrop);
        Assert.Equal(DetailsViewSettings.DefaultColumns, s.Details.Columns);
        Assert.Equal(new IconsViewSettings(), s.Icons);
        Assert.Equal(TilesViewSettings.DefaultInfo, s.Tiles.Info);
    }

    [Fact]
    public void nullの欄は既定値に戻る()
    {
        var s = FileViewSettings.Normalize(JsonSerializer.Deserialize<FileViewSettings>(
            """{ "Common": null, "List": { "NameWidth": null }, "Details": { "Columns": null }, "Tiles": { "Info": null } }""", Json));
        Assert.True(s.Common.ShowOverlays);
        Assert.Equal(NameWidthMode.ShowAll, s.List.NameWidth.Mode);
        Assert.Equal(6, s.Details.Columns.Count);
        Assert.Equal([TileInfo.Type, TileInfo.Size], s.Tiles.Info);
        Assert.Equal(new FileViewSettings().Details.Columns, s.Details.Columns);
    }

    [Theory]
    [InlineData(9, 10)]
    [InlineData(10, 10)]
    [InlineData(260, 260)]
    [InlineData(261, 260)]
    public void 最大文字数は10から260に丸める(int written, int expected)
    {
        var s = FileViewSettings.Normalize(new FileViewSettings
        {
            List = new() { NameWidth = new() { Mode = NameWidthMode.MaxChars, MaxChars = written } },
            Details = new() { NameWidth = new() { MaxChars = written } },
            Icons = new() { SmallIconWidth = new() { MaxChars = written } },
        });
        Assert.Equal(expected, s.List.NameWidth.MaxChars);
        Assert.Equal(expected, s.Details.NameWidth.MaxChars);
        Assert.Equal(expected, s.Icons.SmallIconWidth.MaxChars);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    public void 名前の行数は1から3に丸める(int written, int expected) =>
        Assert.Equal(expected, FileViewSettings.Normalize(new FileViewSettings { Icons = new() { NameLines = written } }).Icons.NameLines);

    [Fact]
    public void 候補に無いアイコンの大きさはその欄の既定値になる()
    {
        var s = FileViewSettings.Normalize(new FileViewSettings
        {
            Icons = new() { MediumSize = 50, LargeSize = 32, ExtraLargeSize = 0 },
            Tiles = new() { TilesSize = 300, ContentSize = 256 },
        });
        Assert.Equal((48, 32, 256), (s.Icons.MediumSize, s.Icons.LargeSize, s.Icons.ExtraLargeSize));   // 中≦大≦特大でなくてよい（Q18）
        Assert.Equal((48, 256), (s.Tiles.TilesSize, s.Tiles.ContentSize));
    }

    [Fact]
    public void 詳細の列は重複と知らない値を捨て欠けた列を非表示で末尾に足す()
    {
        var s = FileViewSettings.Normalize(new FileViewSettings
        {
            Details = new()
            {
                Columns =
                [
                    new() { Column = DetailsColumn.Type, Visible = true },
                    new() { Column = DetailsColumn.Type, Visible = false },
                    new() { Column = (DetailsColumn)99, Visible = true },
                    new() { Column = DetailsColumn.Size, Visible = false },
                ],
            },
        });
        Assert.Equal(
            [
                new() { Column = DetailsColumn.Type, Visible = true },
                new() { Column = DetailsColumn.Size, Visible = false },
                new() { Column = DetailsColumn.Extension, Visible = false },
                new() { Column = DetailsColumn.Modified, Visible = false },
                new() { Column = DetailsColumn.Created, Visible = false },
                new DetailsColumnSetting { Column = DetailsColumn.Attributes, Visible = false },
            ],
            s.Details.Columns);
    }

    [Fact]
    public void 並べて表示の情報は重複と知らない値を捨て候補の順に並べる()
    {
        var s = FileViewSettings.Normalize(new FileViewSettings { Tiles = new() { Info = [TileInfo.Attributes, (TileInfo)99, TileInfo.Type, TileInfo.Attributes] } });
        Assert.Equal([TileInfo.Type, TileInfo.Attributes], s.Tiles.Info);
    }

    [Fact]
    public void 知らない方式とチェックボックスの出し方は既定値になる()
    {
        var s = FileViewSettings.Normalize(new FileViewSettings
        {
            List = new() { NameWidth = new() { Mode = (NameWidthMode)9 } },
            Icons = new() { CheckBoxes = (CheckBoxMode)9 },
            Tiles = new() { CheckBoxes = (CheckBoxMode)9 },
        });
        Assert.Equal(NameWidthMode.ShowAll, s.List.NameWidth.Mode);
        Assert.Equal(CheckBoxMode.HoverAndMarked, s.Icons.CheckBoxes);
        Assert.Equal(CheckBoxMode.HoverAndMarked, s.Tiles.CheckBoxes);
    }

    /// <summary>
    /// 設定ファイルは JsonStringEnumConverter で読むので、知らない名前は JsonException になり、AppSettings.Load が設定ファイル全体を捨てる。
    /// ファイルビューの列挙は、知らない名前だけを捨てて、同じファイルのほかの値を残す（列の名前は Phase 15 以降で増えうる）
    /// </summary>
    [Fact]
    public void 知らない名前を書いた項目だけが既定値になりほかの値は残る()
    {
        var json = """
            {
              "List": { "InPanelDragDrop": true, "NameWidth": { "Mode": "Unknown", "MaxChars": 30 } },
              "Details": { "Columns": [ { "Column": "Unknown", "Visible": true }, { "Column": "Created", "Visible": true } ] },
              "Icons": { "CheckBoxes": "Sometimes", "NameLines": 3 },
              "Tiles": { "Info": [ "Unknown", "Created" ], "CheckBoxes": "Unknown" }
            }
            """;
        var s = FileViewSettings.Normalize(JsonSerializer.Deserialize<FileViewSettings>(json, Json));
        Assert.True(s.List.InPanelDragDrop);
        Assert.Equal(new NameWidthSetting { Mode = NameWidthMode.ShowAll, MaxChars = 30 }, s.List.NameWidth);
        Assert.Equal(new DetailsColumnSetting { Column = DetailsColumn.Created, Visible = true }, s.Details.Columns[0]);
        Assert.Equal(6, s.Details.Columns.Count);
        Assert.Equal(CheckBoxMode.HoverAndMarked, s.Icons.CheckBoxes);
        Assert.Equal(3, s.Icons.NameLines);
        Assert.Equal([TileInfo.Created], s.Tiles.Info);
        Assert.Equal(CheckBoxMode.HoverAndMarked, s.Tiles.CheckBoxes);
    }

    [Fact]
    public void 書き出しは名前で行い読み戻せる()
    {
        var written = JsonSerializer.Serialize(new FileViewSettings(), Json);
        Assert.Contains("\"HoverAndMarked\"", written);
        Assert.Contains("\"Type\"", written);
        var read = FileViewSettings.Normalize(JsonSerializer.Deserialize<FileViewSettings>(written, Json));
        Assert.Equal(new FileViewSettings().Details.Columns, read.Details.Columns);
        Assert.Equal(new FileViewSettings().Tiles.Info, read.Tiles.Info);
    }

    [Fact]
    public void 小アイコンの幅の方式を戻すときの既定は最大文字数()
    {
        var s = FileViewSettings.Normalize(new FileViewSettings { Icons = new() { SmallIconWidth = new() { Mode = (NameWidthMode)9 } } });
        Assert.Equal(NameWidthMode.MaxChars, s.Icons.SmallIconWidth.Mode);   // Phase 16 Q6: 一覧・詳細の既定とは違う
    }

    // ---- 列幅の保存値（R-114）----

    [Fact]
    public void 列幅は知らない列と0以下とnullを捨て上限で止める()
    {
        var normalized = DetailsColumnWidths.Normalize(new Dictionary<string, int?>
        {
            ["Name"] = 300, ["Size"] = 0, ["Modified"] = -5, ["Type"] = null, ["Unknown"] = 100, ["Attributes"] = 99999,
        });
        Assert.Equal(new Dictionary<string, int?> { ["Name"] = 300, ["Attributes"] = DetailsColumnWidths.Max }, normalized);
        Assert.Empty(DetailsColumnWidths.Normalize(null));
    }

    [Fact]
    public void 列の鍵は名前と列挙の名前() =>
        Assert.Equal(["Name", "Extension", "Size", "Modified", "Created", "Type", "Attributes"],
            new DetailsColumn?[] { null }.Concat(Enum.GetValues<DetailsColumn>().Cast<DetailsColumn?>()).Select(DetailsColumnWidths.Key));

    [Theory]
    [InlineData(96)]
    [InlineData(144)]
    [InlineData(192)]
    public void 保存値はdpiを行き来しても変わらない(int dpi)
    {
        foreach (var logical in new[] { 1, 37, 120, 333, DetailsColumnWidths.Max })
            Assert.Equal(logical, DetailsColumnWidths.ToLogical(DetailsColumnWidths.ToPixels(logical, dpi), dpi));
        Assert.Equal(180, DetailsColumnWidths.ToPixels(120, 144));
    }

    // ---- 下書きのマージ（Q33）----

    [Fact]
    public void 下書きで変えた項目だけを今の設定へ書き込む()
    {
        var baseline = new FileViewSettings();
        var draft = baseline with { List = baseline.List with { InPanelDragDrop = true } };
        // 設定画面を開いている間に、別のウィンドウの見出しで列を隠した
        var hidden = baseline.Details.Columns.Select(c => c.Column == DetailsColumn.Type ? c with { Visible = false } : c).ToList();
        var current = baseline with { Details = baseline.Details with { Columns = hidden } };

        var merged = FileViewSettings.Merge(baseline, draft, current);

        Assert.True(merged.List.InPanelDragDrop);
        Assert.False(merged.Details.Columns.Single(c => c.Column == DetailsColumn.Type).Visible);
    }

    [Fact]
    public void 両方で同じ項目を変えたら下書きが勝つ()
    {
        var baseline = new FileViewSettings();
        var draftColumns = baseline.Details.Columns.Reverse().ToList();
        var draft = baseline with { Details = baseline.Details with { Columns = draftColumns } };
        var current = baseline with { Details = baseline.Details with { Columns = baseline.Details.Columns.Skip(1).ToList() } };

        Assert.Equal(draftColumns, FileViewSettings.Merge(baseline, draft, current).Details.Columns);
    }

    [Fact]
    public void 中身が同じ列は変えていないとみなす()
    {
        var baseline = new FileViewSettings();
        var draft = baseline with { Details = baseline.Details with { Columns = baseline.Details.Columns.ToList() } };   // 別の参照・同じ中身
        var currentColumns = baseline.Details.Columns.Skip(1).ToList();
        var current = baseline with { Details = baseline.Details with { Columns = currentColumns } };

        Assert.Equal(currentColumns, FileViewSettings.Merge(baseline, draft, current).Details.Columns);
    }

    [Fact]
    public void すべての系統のすべての項目がマージの対象になる()
    {
        // 型に項目を足したときに、Merge が黙って落とさないことを確かめる。各項目を 1 つずつ下書きだけで変えて、結果に出るか
        var baseline = new FileViewSettings();
        var changed = new FileViewSettings
        {
            Common = new() { ShowOverlays = false, HideKnownExtensions = true },
            List = new() { AlignExtension = false, InPanelDragDrop = true, RangeSelection = false, NameWidth = new() { Mode = NameWidthMode.Auto, MaxChars = 11 } },
            Details = new()
            {
                InPanelDragDrop = false, RangeSelection = false, NameWidth = new() { Mode = NameWidthMode.MaxChars, MaxChars = 12 }, AlignExtension = false,
                Columns = [new() { Column = DetailsColumn.Size, Visible = false }], FitColumnsToWindow = true,
            },
            Icons = new()
            {
                InPanelDragDrop = false, RangeSelection = false, MediumSize = 32, LargeSize = 64, ExtraLargeSize = 128, CheckBoxes = CheckBoxMode.Always,
                Thumbnails = false, FolderThumbnails = false, NameLines = 3, SmallIconWidth = new() { Mode = NameWidthMode.ShowAll },
            },
            Tiles = new()
            {
                InPanelDragDrop = false, RangeSelection = false, Info = [TileInfo.Created], TilesSize = 96, ContentSize = 128, CheckBoxes = CheckBoxMode.Always,
                Thumbnails = false, FolderThumbnails = false,
            },
        };

        var merged = FileViewSettings.Merge(baseline, changed, baseline);

        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(changed), System.Text.Json.JsonSerializer.Serialize(merged));
    }

    [Fact]
    public void 範囲選択の項目はJSONから読め_欠けたら既定のオン()
    {
        var s = FileViewSettings.Normalize(JsonSerializer.Deserialize<FileViewSettings>("""{ "Icons": { "RangeSelection": false } }""", Json));
        Assert.False(s.Icons.RangeSelection);
        Assert.True(s.Details.RangeSelection);
        var back = JsonSerializer.Deserialize<FileViewSettings>(JsonSerializer.Serialize(s, Json), Json)!;
        Assert.False(back.Icons.RangeSelection);
    }
}
