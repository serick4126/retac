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

    // R-146-1: 書庫の項目のコピーの失敗。Windows の画面が出る前の失敗だけ ReTAC が警告する
    public static TheoryData<Exception> Failures => new()
    {
        new IOException("x"), new System.Runtime.InteropServices.COMException("x"),
        new UnauthorizedAccessException("x"), new InvalidOperationException("x"),
    };

    [Theory, MemberData(nameof(Failures))]
    public void ArchiveCopy_FailureBeforeStart_Warns(Exception ex) =>
        Assert.True(MainForm.ShouldWarnArchiveCopy(MainForm.ArchiveCopyPhase.Preparing, ex));

    // 実行中の失敗（Windows の処理の失敗・パスワードの中止や誤り・一部成功の後の失敗。どれも例外からは区別が付かない）
    [Theory, MemberData(nameof(Failures))]
    public void ArchiveCopy_FailureWhileRunning_DoesNotWarn(Exception ex) =>
        Assert.False(MainForm.ShouldWarnArchiveCopy(MainForm.ArchiveCopyPhase.Running, ex));

    [Fact]
    public void ArchiveCopy_UserCancel_DoesNotWarn()
    {
        // 利用者の中止は Execute が false を返すだけで例外にならない。例外が来ても実行中なら警告しない
        Assert.False(MainForm.ShouldWarnArchiveCopy(MainForm.ArchiveCopyPhase.Running, new OperationCanceledException()));
        // 陽性対照: 予期しない種類の例外は、段階にかかわらず対象外（catch に入らず、そのまま外へ出る）
        Assert.False(MainForm.ShouldWarnArchiveCopy(MainForm.ArchiveCopyPhase.Preparing, new OperationCanceledException()));
    }
}
