using ReTAC.Domain.FileOps;

namespace ReTAC.Domain.Tests;

/// <summary>
/// 転送で触ってはいけない対象を外す判断（V-05）。
/// ドロップだけでなく C / M の宛先欄と Ctrl+V も同じ判定を通る。
/// </summary>
public class TransferGuardsTests
{
    [Fact]
    public void 宛先が転送元そのものなら外す()
    {
        Assert.True(TransferGuards.IsInsideOrSame(@"C:\work", @"C:\work"));
        Assert.True(TransferGuards.IsInsideOrSame(@"C:\work\", @"C:\work"));
        Assert.True(TransferGuards.IsInsideOrSame(@"C:\WORK", @"C:\work"));
    }

    [Fact]
    public void 宛先が転送元の配下なら外す()
    {
        Assert.True(TransferGuards.IsInsideOrSame(@"C:\work\sub", @"C:\work"));
        Assert.True(TransferGuards.IsInsideOrSame(@"C:\work\sub\deep", @"C:\work"));
    }

    [Fact]
    public void 名前が前方一致するだけの別フォルダは外さない()
    {
        Assert.False(TransferGuards.IsInsideOrSame(@"C:\work2", @"C:\work"));
        Assert.False(TransferGuards.IsInsideOrSame(@"C:\work-old\sub", @"C:\work"));
    }

    [Fact]
    public void 無関係なフォルダやファイルは外さない()
    {
        Assert.False(TransferGuards.IsInsideOrSame(@"D:\backup", @"C:\work"));
        Assert.False(TransferGuards.IsInsideOrSame(@"C:\other", @"C:\work\a.txt"));
        Assert.False(TransferGuards.IsInsideOrSame("", @"C:\work"));
    }

    [Fact]
    public void 普通のフォルダは辿ってよい()
    {
        var folder = Directory.CreateTempSubdirectory("retac_guard").FullName;
        try
        {
            Assert.True(TransferGuards.CanDescend(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
