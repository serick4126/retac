using ReTAC.App;
using ReTAC.Domain.Startup;

namespace ReTAC.Domain.Tests;

/// <summary>R-26-2: 起動引数のパス</summary>
public class StartupPathTests
{
    private const string Cwd = @"C:\work";

    [Theory]
    [InlineData("C:\"", @"C:\")]
    [InlineData("C:", @"C:\")]
    [InlineData(@"  D:\a  ", @"D:\a")]
    [InlineData("\"D:\\a b\"", @"D:\a b")]
    [InlineData("sub", @"C:\work\sub")]
    [InlineData(@"..\x", @"C:\x")]
    [InlineData(@"\\server\share", @"\\server\share")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Normalize_Table(string? raw, string? expected) =>
        Assert.Equal(expected, StartupPath.Normalize(raw, Cwd));

    [Fact]
    public void Classify_Table()
    {
        Func<string, bool> dir = p => p == @"C:\d";
        Func<string, bool> file = p => p == @"C:\d\a.txt";
        Assert.Equal(new StartupPathResult(StartupPathKind.Folder, @"C:\d", null, "r"), StartupPath.Classify("r", @"C:\d", dir, file));
        Assert.Equal(new StartupPathResult(StartupPathKind.File, @"C:\d", "a.txt", "r"), StartupPath.Classify("r", @"C:\d\a.txt", dir, file));
        Assert.Equal(StartupPathKind.Invalid, StartupPath.Classify("r", @"C:\none", dir, file).Kind);
        Assert.Equal(StartupPathKind.None, StartupPath.Classify("", null, dir, file).Kind);
    }

    [Fact]
    public async Task Probe_HangingCheckTimesOut()
    {
        using var gate = new ManualResetEventSlim();
        var probe = StartupPathProbe.ClassifyAsync(@"\hang\x", @"\hang\x", TimeSpan.FromMilliseconds(100),
            _ => { gate.Wait(); return true; }, _ => false);
        Assert.False(probe.IsCompleted);   // 呼び出し元を止めない
        var result = await probe;
        gate.Set();
        Assert.Equal(StartupPathKind.Invalid, result.Kind);
        Assert.Equal(@"\hang\x", result.Raw);
    }
}
