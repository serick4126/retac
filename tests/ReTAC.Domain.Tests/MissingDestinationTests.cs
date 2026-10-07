using ReTAC.App;

namespace ReTAC.Domain.Tests;

/// <summary>R-62 / R-146-1 / S5: 存在しない宛先の選択肢。取り出した項目では別名複写を出さない（結果が一時フォルダにでき、片付けで消える）。</summary>
public class MissingDestinationTests
{
    [Fact]
    public void MissingDestination_CopyOffersRenameCopy() =>
        Assert.Equal(["フォルダを新しく作成する(&C)", "別の名前に変えて複写する(&R)"], MainForm.MissingDestinationChoices(moving: false, renameAllowed: true));

    [Fact]
    public void MissingDestination_ExtractedHasNoRenameCopy() =>
        Assert.Equal(["フォルダを新しく作成する(&C)"], MainForm.MissingDestinationChoices(moving: false, renameAllowed: false));

    [Fact]
    public void MissingDestination_MoveHasNoRenameCopy() =>
        Assert.Equal(["フォルダを新しく作成する(&C)"], MainForm.MissingDestinationChoices(moving: true, renameAllowed: true));
}
