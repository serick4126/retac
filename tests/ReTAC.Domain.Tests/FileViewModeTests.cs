using ReTAC.Domain.Listing;

namespace ReTAC.Domain.Tests;

public class FileViewModeTests
{
    [Fact]
    public void 三点零点零では8つのモードすべてが列挙の順で切り替えの段に入る()
    {
        Assert.Equal(Enum.GetValues<FileViewMode>(), FileViewModes.Built);
        Assert.Equal([FileViewMode.ExtraLargeIcons, FileViewMode.LargeIcons, FileViewMode.MediumIcons, FileViewMode.SmallIcons,
            FileViewMode.List, FileViewMode.Details, FileViewMode.Tiles, FileViewMode.Content], FileViewModes.Built);
    }

    [Theory]
    [InlineData(FileViewMode.List, FileViewMode.List)]
    [InlineData(FileViewMode.Details, FileViewMode.Details)]
    [InlineData(FileViewMode.SmallIcons, FileViewMode.SmallIcons)]
    [InlineData(FileViewMode.Tiles, FileViewMode.Tiles)]
    [InlineData(FileViewMode.Content, FileViewMode.Content)]
    [InlineData((FileViewMode)int.MinValue, FileViewMode.List)]     // 知らない値（LenientEnumConverter の Unknown）
    public void 知らない値だけを一覧に直す(FileViewMode input, FileViewMode expected) =>
        Assert.Equal(expected, FileViewModes.Normalize(input));

    [Theory]
    [InlineData(FileViewMode.Details, 1, FileViewMode.List)]    // 奥へ回すと上の段
    [InlineData(FileViewMode.List, -1, FileViewMode.Details)]   // 手前へ回すと下の段
    [InlineData(FileViewMode.Details, -1, FileViewMode.Tiles)]
    [InlineData(FileViewMode.Tiles, -1, FileViewMode.Content)]
    [InlineData(FileViewMode.Content, 1, FileViewMode.Tiles)]
    [InlineData(FileViewMode.ExtraLargeIcons, 1, FileViewMode.ExtraLargeIcons)] // 上の端で止まる
    [InlineData(FileViewMode.Content, -3, FileViewMode.Content)] // 下の端で止まる
    [InlineData(FileViewMode.Details, -5, FileViewMode.Content)]
    [InlineData(FileViewMode.Details, 0, FileViewMode.Details)]
    public void ホイールの段は両端で止まる(FileViewMode current, int notches, FileViewMode expected) =>
        Assert.Equal(expected, FileViewModes.Step(current, notches));
}
