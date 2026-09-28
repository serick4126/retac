using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ReTAC.Updater.Core;

namespace ReTAC.Domain.Tests.Updater;

/// <summary>R-109-1 / R-109-4: インストール先の正規化・排他・準備フォルダ。</summary>
public sealed class UpdaterLockAndFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ReTAC-updater-test-" + Guid.NewGuid().ToString("N"));

    public UpdaterLockAndFolderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        // ジャンクションを先に外す（中をたどって消さないように）
        foreach (var dir in Directory.GetDirectories(_root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) Directory.Delete(dir);
        Directory.Delete(_root, recursive: true);
    }

    private string Folder(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Junction(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(start)!;
        p.WaitForExit();
        Assert.True(Directory.Exists(link), "ジャンクションを作れなかった");
    }

    // ---- 正規化と名前 ----

    [Fact]
    public void 表記が違っても同じフォルダなら同じ名前になる()
    {
        var install = Folder("Install Folder");
        var link = Path.Combine(_root, "link");
        Junction(link, install);

        var names = new[]
        {
            install,
            install + Path.DirectorySeparatorChar,
            install.ToUpperInvariant(),
            link,
            ShortPath(install),
        }.Select(p => InstallFolder.ReplaceLockName(InstallFolder.Normalize(p)!)).Distinct().ToList();

        Assert.Single(names);
    }

    [Fact]
    public void 別のフォルダは別の名前になる()
    {
        var a = InstallFolder.Normalize(Folder("a"))!;
        var b = InstallFolder.Normalize(Folder("b"))!;
        Assert.NotEqual(InstallFolder.ReplaceLockName(a), InstallFolder.ReplaceLockName(b));
        Assert.NotEqual(InstallFolder.UiLockName(a), InstallFolder.ReplaceLockName(a));
    }

    [Fact]
    public void 名前はGlobalにあり実行をまたいで同じ値になる()
    {
        // String.GetHashCode は実行ごとに変わる。SHA-256 なら決まった値になる
        Assert.StartsWith(@"Global\ReTAC.Updater.Replace.", InstallFolder.ReplaceLockName(@"\\?\Volume{x}\ReTAC"));
        // 大文字にそろえてからハッシュを取る（"HELLO" の SHA-256）
        Assert.Equal("3733CD977FF8EB18B987357E22CED99F46097F31ECB239E878AE63760E83E4D5", InstallFolder.Hash("hello"));
        Assert.Equal(InstallFolder.Hash("HELLO"), InstallFolder.Hash("hello"));
    }

    [Fact]
    public void 無いフォルダは正規化できない() => Assert.Null(InstallFolder.Normalize(Path.Combine(_root, "none")));

    // ---- 排他 ----

    private static string LockName() => @"Global\ReTAC.Updater.Test." + Guid.NewGuid().ToString("N");

    private static T OnOtherThread<T>(Func<T> body)
    {
        T result = default!;
        var thread = new Thread(() => result = body());
        thread.Start();
        thread.Join();
        return result;
    }

    [Fact]
    public void ほかのスレッドが持っている間は取れない()
    {
        var name = LockName();
        using var held = NamedLock.TryAcquire(name);
        Assert.NotNull(held);
        Assert.Null(OnOtherThread(() => NamedLock.TryAcquire(name)));
        Assert.False(OnOtherThread(() => NamedLock.IsFree(name)));
    }

    [Fact]
    public void 放棄された排他は取れたものとして扱う()
    {
        var name = LockName();
        // 取ったまま手放さずにスレッドを終える（所有するスレッドが終わった状態）
        OnOtherThread(() => { var abandoned = NamedLock.TryAcquire(name); return abandoned is not null; });

        using var taken = NamedLock.TryAcquire(name);
        Assert.NotNull(taken);
    }

    [Fact]
    public void 確かめの後はハンドルを持ち続けない()
    {
        var name = LockName();
        Assert.True(NamedLock.IsFree(name));
        // 確かめたスレッドが持ち続けていれば、ほかのスレッドは取れない
        Assert.True(OnOtherThread(() => { using var l = NamedLock.TryAcquire(name); return l is not null; }));
    }

    [Fact]
    public void ほかのプロセスが必要最小限の権限で開いて取れる()
    {
        var name = LockName();
        var created = NamedLock.TryAcquire(name);   // アクセス権を付けて作る
        Assert.NotNull(created);

        // ミューテックスを消さないためのハンドル。作った本人も通常のコンストラクター（FullControl を求める）では開けないので、
        // 必要最小限の権限で開く
        using var anchor = System.Threading.MutexAcl.OpenExisting(name,
            System.Security.AccessControl.MutexRights.Synchronize | System.Security.AccessControl.MutexRights.Modify);
        created!.Dispose();

        // 別プロセス（PowerShell 5 = .NET Framework）が Synchronize | Modify だけで開いて取る
        var script = "$m=[System.Threading.Mutex]::OpenExisting('" + name + "',[System.Security.AccessControl.MutexRights]'Synchronize, Modify');"
                   + "if($m.WaitOne(0)){$m.ReleaseMutex();'OK'}else{'BUSY'}";
        Assert.Equal("OK", RunPowerShell(script));
    }

    private static string RunPowerShell(string script)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var start = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " + encoded)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var p = Process.Start(start)!;
        var output = p.StandardOutput.ReadToEnd().Trim();
        var error = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(error.Length == 0, error);
        return output;
    }

    // ---- 準備フォルダ ----

    [Fact]
    public void 片付けは今回書き出したファイルだけを消す()
    {
        var install = Folder("install");
        var staging = StagingFolder.Create(install);
        File.WriteAllText(staging.PathFor("README.md"), "ours");
        File.WriteAllText(Path.Combine(staging.FullPath, "user.txt"), "user");   // 後から置かれたもの

        staging.Cleanup();

        Assert.False(File.Exists(Path.Combine(staging.FullPath, "README.md")));
        Assert.True(File.Exists(Path.Combine(staging.FullPath, "user.txt")));
        Assert.True(Directory.Exists(staging.FullPath));   // 空でないので残る
    }

    [Fact]
    public void 空になった準備フォルダは消える()
    {
        var install = Folder("install");
        var staging = StagingFolder.Create(install);
        File.WriteAllText(staging.PathFor(Protocol.ReTacExe), "x");
        staging.Cleanup();
        Assert.False(Directory.Exists(staging.FullPath));
    }

    [Fact]
    public void 前回の準備フォルダには触れず名前だけ知らせる()
    {
        var install = Folder("install");
        var leftover = Path.Combine(install, StagingFolder.Prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(leftover);
        File.WriteAllText(Path.Combine(leftover, "README.md"), "user");

        var staging = StagingFolder.Create(install);
        File.WriteAllText(staging.PathFor("README.md"), "ours");
        var found = StagingFolder.FindLeftovers(install, staging);
        staging.Cleanup();

        Assert.Equal([Path.GetFileName(leftover)], found);
        Assert.Equal("user", File.ReadAllText(Path.Combine(leftover, "README.md")));
    }

    [Fact]
    public void 準備フォルダがジャンクションに差し替えられていたら中をたどらない()
    {
        var install = Folder("install");
        var elsewhere = Folder("elsewhere");
        File.WriteAllText(Path.Combine(elsewhere, "README.md"), "user");

        var staging = StagingFolder.Create(install);
        File.WriteAllText(staging.PathFor("README.md"), "ours");
        File.Delete(Path.Combine(staging.FullPath, "README.md"));
        Directory.Delete(staging.FullPath);
        Junction(staging.FullPath, elsewhere);

        staging.Cleanup();

        Assert.Equal("user", File.ReadAllText(Path.Combine(elsewhere, "README.md")));
    }

    [Fact]
    public void 準備フォルダの中のジャンクションには触れない()
    {
        var install = Folder("install");
        var elsewhere = Folder("elsewhere");
        File.WriteAllText(Path.Combine(elsewhere, "keep.txt"), "user");

        var staging = StagingFolder.Create(install);
        var planted = staging.PathFor("README.md");
        Junction(planted, elsewhere);   // 書き出すはずの名前にジャンクションが置かれた

        staging.Cleanup();

        Assert.True(Directory.Exists(planted));
        Assert.Equal("user", File.ReadAllText(Path.Combine(elsewhere, "keep.txt")));
    }

    [Fact]
    public void 準備フォルダは毎回別の名前で作る()
    {
        var install = Folder("install");
        var a = StagingFolder.Create(install);
        var b = StagingFolder.Create(install);
        Assert.NotEqual(a.FullPath, b.FullPath);
        Assert.StartsWith(StagingFolder.Prefix, Path.GetFileName(a.FullPath));
    }

    private static string ShortPath(string path)
    {
        var buffer = new StringBuilder(1024);
        return GetShortPathName(path, buffer, buffer.Capacity) == 0 ? path : buffer.ToString();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetShortPathNameW")]
    private static extern int GetShortPathName(string path, StringBuilder shortPath, int length);
}
