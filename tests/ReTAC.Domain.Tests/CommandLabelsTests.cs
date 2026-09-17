using ReTAC.App;
using ReTAC.Domain.Commands;

namespace ReTAC.Domain.Tests;

/// <summary>キー割り当ての画面に並べる分類と表示名</summary>
public class CommandLabelsTests
{
    [Fact]
    public void 分類名に移動は無くナビゲーションになっている()
    {
        Assert.DoesNotContain(CommandLabels.Grouped, row => row.Category == "移動");
        Assert.Equal(11, CommandLabels.Grouped.Count(row => row.Category == "ナビゲーション"));
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
    public void コマンド名の移動は変えない()
    {
        // 説明として意味が通るので、機能名の規則の対象外
        Assert.Equal("デスクトップへ移動", CommandLabels.Of(CommandId.GoDesktop));
        Assert.Equal("数字キーのドライブ移動", CommandLabels.Of(CommandId.DriveByNumberKey));
    }
}
