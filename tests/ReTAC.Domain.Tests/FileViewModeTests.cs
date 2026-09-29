using ReTAC.Domain.Listing;

namespace ReTAC.Domain.Tests;

public class FileViewModeTests
{
    [Fact]
    public void 作ったモードはCtrlホイールの段の順に並ぶ() =>
        Assert.Equal([FileViewMode.List, FileViewMode.Details], FileViewModes.Built);

    [Theory]
    [InlineData(FileViewMode.List, FileViewMode.List)]
    [InlineData(FileViewMode.Details, FileViewMode.Details)]
    [InlineData(FileViewMode.ExtraLargeIcons, FileViewMode.List)]   // まだ作っていない
    [InlineData(FileViewMode.Content, FileViewMode.List)]
    [InlineData((FileViewMode)int.MinValue, FileViewMode.List)]     // 知らない値（LenientEnumConverter の Unknown）
    public void 作っていないモードと知らない値は一覧に直す(FileViewMode input, FileViewMode expected) =>
        Assert.Equal(expected, FileViewModes.Normalize(input));

    [Theory]
    [InlineData(FileViewMode.Details, 1, FileViewMode.List)]    // 奥へ回すと上の段
    [InlineData(FileViewMode.List, -1, FileViewMode.Details)]   // 手前へ回すと下の段
    [InlineData(FileViewMode.List, 1, FileViewMode.List)]       // 上の端で止まる
    [InlineData(FileViewMode.Details, -3, FileViewMode.Details)] // 下の端で止まる
    [InlineData(FileViewMode.Details, 0, FileViewMode.Details)]
    public void ホイールの段は両端で止まる(FileViewMode current, int notches, FileViewMode expected) =>
        Assert.Equal(expected, FileViewModes.Step(current, notches));
}
