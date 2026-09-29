using ReTAC.Shell;

namespace ReTAC.Domain.Tests;

/// <summary>静的な状態を使うので、同じコレクションのテストどうしを並列にしない。</summary>
[CollectionDefinition("ShellFileType", DisableParallelization = true)]
public class ShellFileTypeCollection;

[Collection("ShellFileType")]
public class ShellFileTypeTests : IDisposable
{
    public ShellFileTypeTests() => ShellFileType.ResetForTests();
    public void Dispose() => ShellFileType.ResetForTests();

    [Fact]
    public void 同じ拡張子の問い合わせは1回にまとめる()
    {
        var gate = new ManualResetEventSlim();
        ShellFileType.QueryOverride = key => { gate.Wait(TimeSpan.FromSeconds(5)); return "説明" + key; };
        using var done = new CountdownEvent(1);
        ShellFileType.Resolved += key => { if (key == ".abc") done.Signal(); };

        for (var i = 0; i < 1000; i++) ShellFileType.Request($@"C:\x\{i}.abc", isFolder: false);
        gate.Set();

        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, ShellFileType.QueryCount);
        Assert.True(ShellFileType.TryGetCached(@"C:\y\z.ABC", false, out var name));
        Assert.Equal("説明.abc", name);
    }

    [Fact]
    public void 問い合わせ中は空欄で待たされない()
    {
        var gate = new ManualResetEventSlim();
        ShellFileType.QueryOverride = key => { gate.Wait(TimeSpan.FromSeconds(5)); return "遅い"; };

        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 200; i++) ShellFileType.Request($@"C:\x\f.e{i}", isFolder: false);
        Assert.False(ShellFileType.TryGetCached(@"C:\x\f.e0", false, out _));
        Assert.True(watch.ElapsedMilliseconds < 500);   // UI のスレッドを止めない
        gate.Set();
    }

    [Fact]
    public void 問い合わせが例外を投げても空の説明にして続ける()
    {
        ShellFileType.QueryOverride = key => key == ".bad" ? throw new InvalidOperationException() : "ok";
        using var done = new CountdownEvent(2);
        ShellFileType.Resolved += _ => done.Signal();
        ShellFileType.Request(@"C:\a.bad", false);
        ShellFileType.Request(@"C:\a.good", false);
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(ShellFileType.TryGetCached(@"C:\a.bad", false, out var bad));
        Assert.Equal("", bad);
    }
}
