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
        // 実際の中止の経路: Execute は例外を投げず false を返すだけ
        var executed = false;
        Assert.Null(MainForm.RunArchiveCopy(() => () => { executed = true; return false; }));
        Assert.True(executed);   // 陽性対照: 実行まで進んでいる
    }

    [Fact]
    public void ArchiveCopy_Run_WarnsOnlyBeforeStart()
    {
        Assert.Equal("prep", MainForm.RunArchiveCopy(() => throw new IOException("prep")));
        Assert.Null(MainForm.RunArchiveCopy(() => () => throw new IOException("run")));
        Assert.Null(MainForm.RunArchiveCopy(() => () => throw new System.Runtime.InteropServices.COMException("run")));
        Assert.Null(MainForm.RunArchiveCopy(() => () => true));
    }
}
