using System.Diagnostics;
using System.IO;
using ReTAC.App;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-126: 想定外の例外の詳細を retac.error.log に追記する。書けなくても例外を出さない。記録は同期で書き切る。
/// </summary>
public sealed class ErrorLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "retac-errlog-" + Guid.NewGuid().ToString("N"));
    private string Log => Path.Combine(_dir, ErrorLog.FileName);
    private string OldLog => Path.Combine(_dir, ErrorLog.OldFileName);
    private static readonly DateTime When = new(2026, 9, 30, 12, 34, 56);

    public ErrorLogTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } }

    private static Exception Thrown(string message, Exception? inner = null)
    {
        try { throw new InvalidOperationException(message, inner); }
        catch (Exception ex) { return ex; }
    }

    [Fact]
    public void 日時と版とOSと例外の詳細を書く()
    {
        var path = ErrorLog.Write(Thrown("boom"), "ui", _dir, When, null);

        Assert.Equal(Log, path);
        var text = File.ReadAllText(Log);
        Assert.Contains("2026-09-30 12:34:56", text);
        Assert.Contains("ui", text);
        Assert.Contains("ReTAC ", text);
        Assert.Contains(Environment.OSVersion.ToString(), text);
        Assert.Contains("System.InvalidOperationException", text);
        Assert.Contains("boom", text);
        Assert.Contains(nameof(Thrown), text);   // スタック
    }

    [Fact]
    public void 内側の例外も書く()
    {
        ErrorLog.Write(Thrown("outer", Thrown("inner-cause")), "ui", _dir, When, null);
        ErrorLog.Write(new AggregateException(Thrown("first"), Thrown("second")), "task", _dir, When, null);

        var text = File.ReadAllText(Log);
        Assert.Contains("inner-cause", text);
        Assert.Contains("first", text);
        Assert.Contains("second", text);
    }

    [Fact]
    public void 追記になり_前の記録は消えない()
    {
        ErrorLog.Write(Thrown("one"), "ui", _dir, When, null);
        ErrorLog.Write(Thrown("two"), "ui", _dir, When, null);

        var text = File.ReadAllText(Log);
        Assert.True(text.IndexOf("one", StringComparison.Ordinal) < text.IndexOf("two", StringComparison.Ordinal));
    }

    [Fact]
    public void 上限を超えていたら古い方へ置き換えてから書き_前からあった古い方は捨てる()
    {
        File.WriteAllText(OldLog, "very-old");
        File.WriteAllText(Log, new string('x', (int)ErrorLog.MaxBytes + 1));

        ErrorLog.Write(Thrown("fresh"), "ui", _dir, When, null);

        Assert.StartsWith("xxx", File.ReadAllText(OldLog));
        Assert.DoesNotContain("very-old", File.ReadAllText(OldLog));
        var text = File.ReadAllText(Log);
        Assert.Contains("fresh", text);
        Assert.DoesNotContain("xxx", text);
    }

    [Fact]
    public void 上限以下なら置き換えない()
    {
        File.WriteAllText(Log, "small");
        ErrorLog.Write(Thrown("fresh"), "ui", _dir, When, null);
        Assert.False(File.Exists(OldLog));
        Assert.StartsWith("small", File.ReadAllText(Log));
    }

    [Fact]
    public void 書けないときは_nullを返し_例外を出さない()
    {
        // フォルダの名前の所にファイルを置き、その中へは書けないようにする
        var blocked = Path.Combine(_dir, "blocked");
        File.WriteAllText(blocked, "");
        var waits = new List<int>();

        var path = ErrorLog.Write(Thrown("boom"), "ui", blocked, When, waits.Add);

        Assert.Null(path);
        Assert.Equal(AppSettings.WriteAttempts - 1, waits.Count);   // R-55-3 と同じ回数でやり直してから諦める
    }

    [Fact]
    public void 一時的な失敗なら_やり直して書き切る()
    {
        // 最初の 1 回だけ、ほかのプロセスが開いている状態にする
        File.WriteAllText(Log, "");
        var held = new FileStream(Log, FileMode.Open, FileAccess.Read, FileShare.None);
        var waits = 0;

        var path = ErrorLog.Write(Thrown("boom"), "ui", _dir, When, _ => { waits++; held.Dispose(); });

        Assert.Equal(Log, path);
        Assert.Equal(1, waits);
        Assert.Contains("boom", File.ReadAllText(Log));
    }

    [Fact]
    public void 記録できたときだけ_メッセージに場所を添える()
    {
        var ex = Thrown("boom");
        Assert.Equal("boom", ErrorLog.Message(ex, null));
        Assert.Equal($"boom{Environment.NewLine}記録: C:\\x\\retac.error.log", ErrorLog.Message(ex, @"C:\x\retac.error.log"));
    }

    [Fact]
    public void 同じ種類とメッセージの例外は1回だけ記録する()
    {
        ErrorLog.ResetOnce();
        Assert.Equal(Log, ErrorLog.WriteOnce(Thrown("same"), "once-a", _dir, When));
        Assert.Null(ErrorLog.WriteOnce(Thrown("same"), "once-a", _dir, When));
        Assert.Equal(Log, ErrorLog.WriteOnce(Thrown("other"), "once-a", _dir, When));   // メッセージが違えば記録する

        var text = File.ReadAllText(Log);
        Assert.Equal(2, text.Split("==== ").Length - 1);
    }

    [Fact]
    public void 一度だけの記録は_種類が違っても上限まで()
    {
        ErrorLog.ResetOnce();
        for (var i = 0; i < ErrorLog.OnceLimit + 10; i++) ErrorLog.WriteOnce(Thrown($"distinct-{i}"), "once-b", _dir, When);

        Assert.Equal(ErrorLog.OnceLimit, File.ReadAllText(Log).Split("==== ").Length - 1);
    }

    [Fact]
    public async Task 待たれないタスクの普通のファイルの失敗は記録しない_想定外の型は記録する()
    {
        ErrorLog.ResetOnce();
        ErrorLog.FolderOverride = _dir;
        try
        {
            await ErrorLog.IgnoreFileSystemFailure(Task.FromException(new IOException("disconnected")));
            await ErrorLog.IgnoreFileSystemFailure(Task.FromException(new UnauthorizedAccessException("denied")));
            Assert.False(File.Exists(Log));

            await ErrorLog.IgnoreFileSystemFailure(Task.FromException(new InvalidOperationException("unexpected-kind")));
            Assert.Contains("unexpected-kind", File.ReadAllText(Log));
        }
        finally { ErrorLog.FolderOverride = null; }
    }

#if DEBUG
    /// <summary>
    /// 書き切りの確認（統合）。子プロセスを、画面以外のスレッドの例外で異常終了させ、終わってから記録を読む。
    /// --throw は開発用のビルドにだけある。テストのプロセスは落とさない。
    /// </summary>
    [Theory]
    [InlineData("background", "retac-throw-background")]
    [InlineData("task", "retac-throw-task")]
    public void 子プロセスの例外が_終了のあとに最後まで記録されている(string kind, string message)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "ReTAC.exe");
        Assert.True(File.Exists(exe), exe);
        using var process = Process.Start(new ProcessStartInfo(exe) { ArgumentList = { "--throw", kind, _dir }, UseShellExecute = false })!;
        Assert.True(process.WaitForExit(30_000));

        var text = File.ReadAllText(Log);
        Assert.Contains(message, text);
        Assert.Contains("System.InvalidOperationException", text);
        Assert.EndsWith(Environment.NewLine + Environment.NewLine, text);   // 記録の末尾まで書かれている
        if (kind == "background") Assert.NotEqual(0, process.ExitCode);      // 画面以外のスレッドの例外は止められない
    }
#endif
}
