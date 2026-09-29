using ReTAC.Domain.Listing;

namespace ReTAC.Domain.Tests;

public class FileViewModeTests
{
    [Fact]
    public void Phase16で作ったモードは切り替えの段に入る()
    {
        Assert.Equal([FileViewMode.ExtraLargeIcons, FileViewMode.LargeIcons, FileViewMode.MediumIcons, FileViewMode.SmallIcons,
            FileViewMode.List, FileViewMode.Details], FileViewModes.Built);
        Assert.Equal(FileViewMode.SmallIcons, FileViewModes.Step(FileViewMode.List, 1));
        Assert.Equal(FileViewMode.ExtraLargeIcons, FileViewModes.Step(FileViewMode.ExtraLargeIcons, 1));
        Assert.Equal(FileViewMode.List, FileViewModes.Normalize(FileViewMode.Tiles));
    }

    [Theory]
    [InlineData(FileViewMode.List, FileViewMode.List)]
    [InlineData(FileViewMode.Details, FileViewMode.Details)]
    [InlineData(FileViewMode.SmallIcons, FileViewMode.SmallIcons)]
    [InlineData(FileViewMode.Tiles, FileViewMode.List)]             // まだ作っていない
    [InlineData(FileViewMode.Content, FileViewMode.List)]
    [InlineData((FileViewMode)int.MinValue, FileViewMode.List)]     // 知らない値（LenientEnumConverter の Unknown）
    public void 作っていないモードと知らない値は一覧に直す(FileViewMode input, FileViewMode expected) =>
        Assert.Equal(expected, FileViewModes.Normalize(input));

    [Theory]
    [InlineData(FileViewMode.Details, 1, FileViewMode.List)]    // 奥へ回すと上の段
    [InlineData(FileViewMode.List, -1, FileViewMode.Details)]   // 手前へ回すと下の段
    [InlineData(FileViewMode.ExtraLargeIcons, 1, FileViewMode.ExtraLargeIcons)] // 上の端で止まる
    [InlineData(FileViewMode.Details, -3, FileViewMode.Details)] // 下の端で止まる
    [InlineData(FileViewMode.Details, 0, FileViewMode.Details)]
    public void ホイールの段は両端で止まる(FileViewMode current, int notches, FileViewMode expected) =>
        Assert.Equal(expected, FileViewModes.Step(current, notches));
}
