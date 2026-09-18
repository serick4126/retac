using ReTAC.App;
using ReTAC.Domain.Commands;

namespace ReTAC.Domain.Tests;

/// <summary>キー割り当ての画面に並べる分類と表示名</summary>
public class CommandLabelsTests
{
    [Fact]
    public void 元に戻すはファイル操作の先頭にある()
    {
        var first = CommandLabels.Grouped.First();
        Assert.Equal((CommandId.Undo, "ファイル操作", "元に戻す"), (first.Command, first.Category, first.Label));
    }

    [Fact]
    public void ドライブバーの表示切り替えは表示の分類にある()
    {
        var row = CommandLabels.Grouped.Single(r => r.Command == CommandId.ToggleDriveBar);
        Assert.Equal(("表示", "ドライブバーの表示切り替え"), (row.Category, row.Label));
    }

    [Fact]
    public void ドライブバーの表示は既定で有効でキーが無い設定ファイルでも有効()
    {
        Assert.True(new AppSettings().ShowDriveBar);
        Assert.True(System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{}")!.ShowDriveBar);
    }

    [Fact]
    public void アドレスバーの表示切り替えは表示の分類にある()
    {
        var row = CommandLabels.Grouped.Single(r => r.Command == CommandId.ToggleAddressBar);
        Assert.Equal(("表示", "アドレスバーの表示切り替え"), (row.Category, row.Label));
    }

    [Fact]
    public void アドレスバーの表示は既定で有効でキーが無い設定ファイルでも有効()
    {
        Assert.True(new AppSettings().ShowAddressBar);
        Assert.True(System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{}")!.ShowAddressBar);
    }

    [Fact]
    public void 分類名に移動は無くナビゲーションになっている()
    {
        Assert.DoesNotContain(CommandLabels.Grouped, row => row.Category == "移動");
        // 11 件＋インクリメンタルサーチ（R-80）
        Assert.Equal(12, CommandLabels.Grouped.Count(row => row.Category == "ナビゲーション"));
    }

    [Fact]
    public void インクリメンタルサーチはナビゲーションの末尾にある()
    {
        var navigation = CommandLabels.Grouped.Where(r => r.Category == "ナビゲーション").ToList();
        Assert.Equal((CommandId.IncrementalSearch, "インクリメンタルサーチ"), (navigation[^1].Command, navigation[^1].Label));
    }

    [Theory]
    [InlineData(CommandId.GoParent)]
    [InlineData(CommandId.DriveByNumberKey)]
    [InlineData(CommandId.GoDesktop)]
    public void フォルダを切り替えるコマンドはナビゲーションに入る(CommandId id)
    {
        Assert.Equal("ナビゲーション", CommandLabels.Grouped.Single(row => row.Command == id).Category);
    }

    [Fact]
    public void 背景メニューはコンテキストメニューの直後にある()
    {
        var rows = CommandLabels.Grouped.ToList();
        var context = rows.FindIndex(r => r.Command == CommandId.ShowContextMenu);
        Assert.Equal((CommandId.ShowFolderBackgroundMenu, "表示", "フォルダのコンテキストメニューの表示"),
            (rows[context + 1].Command, rows[context + 1].Category, rows[context + 1].Label));
    }

    [Fact]
    public void コマンド名の移動は変えない()
    {
        // 説明として意味が通るので、機能名の規則の対象外
        Assert.Equal("デスクトップへ移動", CommandLabels.Of(CommandId.GoDesktop));
        Assert.Equal("数字キーのドライブ移動", CommandLabels.Of(CommandId.DriveByNumberKey));
    }
}
