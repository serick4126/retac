using System.IO;
using ReTAC.App;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-55-3 / V-07: 設定の書き込みの一時的な失敗（直前に書いたファイルをウイルス対策などが開いている間の「アクセス拒否」）は、
/// 少し待ってやり直す。続けて保存したとき（Ctrl+ホイールでの表示モードの切り替えなど）に、書き込める実行ディレクトリを
/// 「書き込めない」と見なして保存先を移さない。
/// </summary>
public class AppSettingsWriteTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "retac-write-" + Guid.NewGuid().ToString("N"));
    private string Target => Path.Combine(_dir, "retac.settings.json");

    public AppSettingsWriteTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(IOException))]
    public void 一時的に失敗しても待ってやり直し_書き切る(Type failure)
    {
        var (calls, waits) = (0, new List<int>());
        AppSettings.WriteAtomic(Target, "{}", (temp, path) =>
        {
            if (++calls <= 2) throw (Exception)Activator.CreateInstance(failure)!;
            File.Move(temp, path, overwrite: true);
        }, waits.Add);
        Assert.Equal(3, calls);
        Assert.Equal(2, waits.Count);
        Assert.Equal("{}", File.ReadAllText(Target));
    }

    [Fact]
    public void 失敗が続けば回数の上限で諦めて例外を出す()
    {
        var (calls, waits) = (0, new List<int>());
        File.WriteAllText(Target + ".tmp", "neighbor");   // 決まった名前の一時ファイルを使っていたころに壊していた、同名の隣のファイル
        Assert.Throws<UnauthorizedAccessException>(() => AppSettings.WriteAtomic(Target, "{}",
            (_, _) => { calls++; throw new UnauthorizedAccessException(); }, waits.Add));
        Assert.Equal(AppSettings.WriteAttempts, calls);
        Assert.Equal(AppSettings.WriteAttempts - 1, waits.Count);   // 最後の失敗の後は待たない
        Assert.False(File.Exists(Target));
        // 失敗した回ごとの一時ファイルは片付け、隣のファイルは触らない（SafeFileWrite）
        Assert.Equal("neighbor", File.ReadAllText(Target + ".tmp"));
        Assert.Equal(new[] { Target + ".tmp" }, Directory.GetFiles(Path.GetDirectoryName(Target)!));
    }

    [Fact]
    public void 最初から通れば待たない()
    {
        var waits = new List<int>();
        AppSettings.WriteAtomic(Target, "{\"a\":1}", (temp, path) => File.Move(temp, path, overwrite: true), waits.Add);
        Assert.Empty(waits);
        Assert.Equal("{\"a\":1}", File.ReadAllText(Target));
        Assert.False(File.Exists(Target + ".tmp"));
    }

    [Fact]
    public void 一時的な失敗でない例外はやり直さない()
    {
        var calls = 0;
        Assert.Throws<InvalidOperationException>(() => AppSettings.WriteAtomic(Target, "{}",
            (_, _) => { calls++; throw new InvalidOperationException(); }, _ => { }));
        Assert.Equal(1, calls);
    }
}
