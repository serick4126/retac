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
}
