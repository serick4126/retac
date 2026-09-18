using ReTAC.App;

namespace ReTAC.Domain.Tests;

/// <summary>Q9: アドレスバーが最小幅を割るときだけ、ドライブバーを文字なしに縮める</summary>
public class TopRowLayoutTests
{
    [Theory]
    [InlineData(800, 300, 200, false)]   // 残り 500
    [InlineData(500, 300, 200, false)]   // 残りがちょうど最小幅
    [InlineData(499, 300, 200, true)]
    [InlineData(100, 300, 200, true)]
    public void 縮めるかは文字ありの幅で決める(int rowWidth, int fullDriveWidth, int minAddressWidth, bool expected) =>
        Assert.Equal(expected, TopRowLayout.ShouldCompact(rowWidth, fullDriveWidth, minAddressWidth));
}
