using System.Diagnostics;
using System.IO.Compression;
using ReTAC.Updater.Core;

namespace ReTAC.Domain.Tests.Updater;

/// <summary>
/// INV-RELEASE-EXACT-SET: リリースの検査のスクリプト（build/Test-ReleaseZip.ps1）。
/// ビルドしたアップデータの exe を zip に入れて走らせる（配布物の名前はその exe の --list-distribution から読まれる）。
/// </summary>
public sealed class ReleaseZipCheckTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ReTAC-release-check-" + Guid.NewGuid().ToString("N"));

    public ReleaseZipCheckTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ReTAC.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("リポジトリの直下が見つからない");
    }

    /// <summary>テストと同じ構成でビルドしたアップデータ。</summary>
    private static string UpdaterExe()
    {
        var configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}") ? "Release" : "Debug";
        var path = Path.Combine(RepoRoot(), "src", "ReTAC.Updater", "bin", configuration, "net48", "ReTAC.Updater.exe");
        Assert.True(File.Exists(path), $"アップデータがビルドされていない: {path}");
        return path;
    }

    private static string Version()
    {
        var info = FileVersionInfo.GetVersionInfo(UpdaterExe());
        return $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}";
    }

    /// <summary>zip を作る。exe の中身は、ReTAC.exe・ReTAC.Updater.exe ともビルドしたアップデータ（版がそろう）。</summary>
    private string Zip(IEnumerable<(string Entry, string? Source)> items)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, source) in items)
        {
            var created = zip.CreateEntry(entry);
            using var target = created.Open();
            if (source is null) target.Write([1, 2, 3], 0, 3);
            else using (var input = File.OpenRead(source)) input.CopyTo(target);
        }
        return path;
    }

    private static IEnumerable<(string, string?)> Official() =>
        Distribution.Names.Select(n => (n, n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? UpdaterExe() : (string?)null));

    private static (int Code, string Output) Run(string zip, string version)
    {
        var script = Path.Combine(RepoRoot(), "build", "Test-ReleaseZip.ps1");
        var start = new ProcessStartInfo("powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{script}\" -Zip \"{zip}\" -Version {version}")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    [Fact]
    public void 配布物の6ファイルで版がそろっていれば通る()
    {
        var (code, output) = Run(Zip(Official()), Version());
        Assert.True(code == 0, output);
    }

    [Fact]
    public void 配布物の名前に無いファイルを足すと失敗する()
    {
        var (code, output) = Run(Zip(Official().Append(("x.dll", null))), Version());
        Assert.True(code == 1, output);
    }

    [Fact]
    public void 配布物が欠けると失敗する()
    {
        var (code, output) = Run(Zip(Official().Where(i => i.Item1 != "README.md")), Version());
        Assert.True(code == 1, output);
    }

    [Fact]
    public void 版番号が違えば失敗する()
    {
        var (code, output) = Run(Zip(Official()), "9.9.9");
        Assert.True(code == 1, output);
    }

    [Fact]
    public void 版の違うexeが入っていれば失敗する()
    {
        // ReTAC.exe に別の版の exe（Windows のメモ帳）を入れる
        var notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");
        var items = Official().Select(i => i.Item1 == Protocol.ReTacExe ? (i.Item1, (string?)notepad) : i);
        var (code, output) = Run(Zip(items), Version());
        Assert.True(code == 1, output);
    }

    [Fact]
    public void 同じ名前が2つあれば失敗する()
    {
        var (code, output) = Run(Zip(Official().Append((Protocol.ReTacExe, UpdaterExe()))), Version());
        Assert.True(code == 1, output);
    }

    [Fact]
    public void 大文字小文字だけ違う名前があれば失敗する()
    {
        var (code, output) = Run(Zip(Official().Append(("readme.md", null))), Version());
        Assert.True(code == 1, output);
    }

    [Fact]
    public void フォルダを含めば失敗する()
    {
        var (code, output) = Run(Zip(Official().Append(("sub/README.md", null))), Version());
        Assert.True(code == 1, output);
    }

    [Fact]
    public void アップデータは配布物の名前を1行に1つ出す()
    {
        var start = new ProcessStartInfo(UpdaterExe(), "--list-distribution")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
        };
        using var process = Process.Start(start)!;
        var lines = process.StandardOutput.ReadToEnd().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        process.WaitForExit();
        Assert.Equal(Distribution.Names, lines);
    }
}
