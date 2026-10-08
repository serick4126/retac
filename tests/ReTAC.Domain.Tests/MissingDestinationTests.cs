using ReTAC.App;

namespace ReTAC.Domain.Tests;

/// <summary>R-62 / R-146-1: 存在しない宛先の選択肢と、差分の選択肢（R-41-6）。書庫の中の項目では、別名複写も差分も出さない（複写先が書庫の中で書き込めず、衝突は Windows の確認画面）。</summary>
public class MissingDestinationTests
{
    [Fact]
    public void MissingDestination_CopyOffersRenameCopy() =>
        Assert.Equal(["フォルダを新しく作成する(&C)", "別の名前に変えて複写する(&R)"], MainForm.MissingDestinationChoices(moving: false, renameAllowed: true));

    [Fact]
    public void MissingDestination_ArchiveItemHasNoRenameCopy() =>
        Assert.Equal(["フォルダを新しく作成する(&C)"], MainForm.MissingDestinationChoices(moving: false, renameAllowed: false));

    [Fact]
    public void MissingDestination_MoveHasNoRenameCopy() =>
        Assert.Equal(["フォルダを新しく作成する(&C)"], MainForm.MissingDestinationChoices(moving: true, renameAllowed: true));

    [Fact]
    public void DifferentialOption_NotOfferedForArchiveItems() => Assert.False(MainForm.DifferentialOptionAvailable(virtualSource: true));

    [Fact]
    public void DifferentialOption_OfferedForPaths() => Assert.True(MainForm.DifferentialOptionAvailable(virtualSource: false));   // 陽性対照
}
