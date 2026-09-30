using ReTAC.App;
using ReTAC.Domain.Listing;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-112-2: 設定画面の「ファイルビュー」ページ。部品の変更が下書きの該当する系統だけを差し替え、
/// 共有の設定の列を書き換えないこと（INV-SETTINGS-DRAFT）を、表示せずに確かめる。
/// </summary>
public class FileViewPageTests
{
    private static (AppSettings Settings, SettingsDraft Draft) Baseline()
    {
        var settings = new AppSettings();
        return (settings, SettingsDraft.From(settings, settings.ToKeyMap(), settings.ToTheme(), settings.ToQuickAccess()));
    }

    [Fact]
    public void 左の一覧は5つの系統の順で共通を選んで開く()
    {
        var (_, draft) = Baseline();
        using var page = new FileViewPage(draft);

        Assert.Equal(["共通", "一覧", "詳細", "アイコン", "並べて表示・コンテンツ"], page.Groups.Items.Cast<string>());
        Assert.Equal(0, page.Groups.SelectedIndex);
    }

    [Fact]
    public void 一覧のドラッグアンドドロップは一覧の系統だけを変える()
    {
        var (_, draft) = Baseline();
        var before = draft.FileViews;
        using var page = new FileViewPage(draft);

        page.ListDragDrop.Checked = true;

        Assert.True(draft.FileViews.List.InPanelDragDrop);
        Assert.Same(before.Common, draft.FileViews.Common);
        Assert.Same(before.Details, draft.FileViews.Details);
        Assert.Same(before.Icons, draft.FileViews.Icons);
        Assert.Same(before.Tiles, draft.FileViews.Tiles);
        Assert.Same(before.List.NameWidth, draft.FileViews.List.NameWidth);
    }

    [Fact]
    public void 詳細の列を上へ動かすと下書きの並びだけが入れ替わる()
    {
        var (settings, draft) = Baseline();
        var original = settings.FileViews.Details.Columns.ToList();
        using var page = new FileViewPage(draft);

        page.Groups.SelectedIndex = 2;
        page.DetailsColumns.SelectedIndex = 1;
        page.MoveColumnUp.PerformClick();

        Assert.Equal([original[1], original[0], .. original.Skip(2)], draft.FileViews.Details.Columns);
        Assert.Equal(original, settings.FileViews.Details.Columns);
        Assert.Equal(0, page.DetailsColumns.SelectedIndex);
    }

    public static TheoryData<string> Phase16Controls =>
        ["Overlays", "CtrlWheel", "IconsCheckBoxes", "IconsThumbnails", "IconsFolderThumbnails", "NameLines", "SmallIconWidth", "SmallIconChars",
         "TilesCheckBoxes", "TilesThumbnails", "TilesFolderThumbnails"];

    /// <summary>R-116〜R-119: Phase 16 の設定の部品は、下書きの自分の系統の欄だけを変える。</summary>
    [Theory]
    [MemberData(nameof(Phase16Controls))]
    public void Phase16の設定の部品は自分の系統だけを変える(string control)
    {
        var (_, draft) = Baseline();
        var before = draft.FileViews;
        using var page = new FileViewPage(draft);
        switch (control)
        {
            case "Overlays": page.Overlays.Checked = false; break;
            case "CtrlWheel": page.CtrlWheel.Checked = false; break;
            case "IconsCheckBoxes": page.IconsCheckBoxes.SelectedIndex = 1; break;
            case "IconsThumbnails": page.IconsThumbnails.Checked = false; break;
            case "IconsFolderThumbnails": page.IconsFolderThumbnails.Checked = false; break;
            case "NameLines": page.NameLines.Value = 3; break;
            case "SmallIconWidth": page.SmallIconWidth.SelectedIndex = 0; break;
            case "SmallIconChars": page.SmallIconChars.Value = 20; break;
            case "TilesCheckBoxes": page.TilesCheckBoxes.SelectedIndex = 1; break;
            case "TilesThumbnails": page.TilesThumbnails.Checked = false; break;
            case "TilesFolderThumbnails": page.TilesFolderThumbnails.Checked = false; break;
        }
        var after = draft.FileViews;
        var (changed, group) = control switch
        {
            "Overlays" => (!after.Common.ShowOverlays, "Common"),
            "CtrlWheel" => (!after.Common.CtrlWheelSwitchesView, "Common"),
            "IconsCheckBoxes" => (after.Icons.CheckBoxes == CheckBoxMode.Always, "Icons"),
            "IconsThumbnails" => (!after.Icons.Thumbnails, "Icons"),
            "IconsFolderThumbnails" => (!after.Icons.FolderThumbnails, "Icons"),
            "NameLines" => (after.Icons.NameLines == 3, "Icons"),
            "SmallIconWidth" => (after.Icons.SmallIconWidth.Mode == NameWidthMode.ShowAll, "Icons"),
            // 最大文字数だけが変わり、方式（既定は最大文字数）もほかのアイコンの項目も変わらない
            "SmallIconChars" => (after.Icons.SmallIconWidth == before.Icons.SmallIconWidth with { MaxChars = 20 }
                                 && after.Icons with { SmallIconWidth = before.Icons.SmallIconWidth } == before.Icons, "Icons"),
            "TilesCheckBoxes" => (after.Tiles.CheckBoxes == CheckBoxMode.Always, "Tiles"),
            "TilesThumbnails" => (!after.Tiles.Thumbnails, "Tiles"),
            _ => (!after.Tiles.FolderThumbnails, "Tiles"),
        };
        Assert.True(changed, control);
        if (group != "Common") Assert.Same(before.Common, after.Common);
        if (group != "Icons") Assert.Same(before.Icons, after.Icons);
        if (group != "Tiles") Assert.Same(before.Tiles, after.Tiles);
        Assert.Same(before.List, after.List);
        Assert.Same(before.Details, after.Details);
    }

    [Fact]
    public void 範囲選択の部品は各系統の項目だけを変える()
    {
        // R-112-1: 一覧・詳細・アイコン・並べて表示のそれぞれが自分の RangeSelection だけを持つ
        var (_, draft) = Baseline();
        using var page = new FileViewPage(draft);
        Assert.All(new[] { page.ListRange, page.DetailsRange, page.IconsRange, page.TilesRange }, box => Assert.True(box.Checked));

        page.DetailsRange.Checked = false;
        Assert.False(draft.FileViews.Details.RangeSelection);
        Assert.True(draft.FileViews.List.RangeSelection);
        Assert.True(draft.FileViews.Icons.RangeSelection);
        Assert.True(draft.FileViews.Tiles.RangeSelection);

        page.ListRange.Checked = false;
        page.IconsRange.Checked = false;
        page.TilesRange.Checked = false;
        Assert.False(draft.FileViews.List.RangeSelection);
        Assert.False(draft.FileViews.Icons.RangeSelection);
        Assert.False(draft.FileViews.Tiles.RangeSelection);
    }
}
