using System.Diagnostics;
using System.IO.Compression;
using ReTAC.Updater.Core;

namespace ReTAC.Domain.Tests.Updater;

/// <summary>
/// INV-RELEASE-EXACT-SET: リリースの検査のスクリプト（build/Test-ReleaseZip.ps1）。
/// 配布物の 6 つの名前は、ここに固定で書いたものを正解とし、アップデータの一覧とスクリプトの一覧の両方をこれと突き合わせる
/// （どちらかから名前が抜けても、もう一方から作った zip で検査が通ってしまわないように）。
/// </summary>
public sealed class ReleaseZipCheckTests : IDisposable
{
    /// <summary>仕様の配布物の 6 ファイル（正解）。</summary>
    private static readonly string[] SixNames =
    [
        "ReTAC.exe",
        "ReTAC.Updater.exe",
        "LICENSE",
        "README.md",
        "DOTNET-LICENSE.txt",
        "DOTNET-ThirdPartyNotices.txt",
    ];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ReTAC-release-check-" + Guid.NewGuid().ToString("N"));

    public ReleaseZipCheckTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ReTAC.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("リポジトリの直下が見つからない");
    }

    private static string Script() => Path.Combine(RepoRoot(), "build", "Test-ReleaseZip.ps1");

    /// <summary>テストと同じ構成でビルドしたアップデータ。版の入った exe として zip に入れる。</summary>
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
            using var target = zip.CreateEntry(entry).Open();
            if (source is null) target.Write([1, 2, 3], 0, 3);
            else using (var input = File.OpenRead(source)) input.CopyTo(target);
        }
        return path;
    }

    /// <summary>正解の 6 つの名前から作る（アップデータやスクリプトの一覧からは作らない）。</summary>
    private static IEnumerable<(string, string?)> Official() =>
        SixNames.Select(n => (n, n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? UpdaterExe() : (string?)null));

    private static (int Code, string Output) Run(string arguments)
    {
        var start = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{Script()}\" {arguments}")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    private static (int Code, string Output) Check(string zip, string version) => Run($"-Zip \"{zip}\" -Version {version}");

    // ---- 名前の一覧（どこかから抜けたら落ちる）----

    [Fact]
    public void アップデータの一覧は配布物の6ファイルと一致する() => Assert.Equal(SixNames, Distribution.Names);

    [Fact]
    public void スクリプトの一覧は配布物の6ファイルと一致する()
    {
        var (code, output) = Run("-PrintNames");
        Assert.Equal(0, code);
        Assert.Equal(SixNames, output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
    }

    // ---- 検査 ----

    [Fact]
    public void 配布物の6ファイルで版がそろっていれば通る()
    {
        var (code, output) = Check(Zip(Official()), Version());
        Assert.True(code == 0, output);
    }

    [Fact]
    public void 配布物の名前に無いファイルを足すと失敗する()
    {
        var (code, output) = Check(Zip(Official().Append(("x.dll", null))), Version());
        Assert.True(code == 1, output);
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("LICENSE")]
    [InlineData("ReTAC.Updater.exe")]
    public void 配布物が欠けると失敗する(string missing)
    {
        var (code, output) = Check(Zip(Official().Where(i => i.Item1 != missing)), Version());
        Assert.True(code == 1, output);
    }

    [Fact]
    public void 版番号が違えば失敗する()
    {
        var (code, output) = Check(Zip(Official()), "9.9.9");
        Assert.True(code == 1, output);
    }

    [Fact]
    public void 版の違うexeが入っていれば失敗する()
    {
        // ReTAC.exe に別の版の exe（Windows のメモ帳）を入れる
        var notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");
        var items = Official().Select(i => i.Item1 == Protocol.ReTacExe ? (i.Item1, (string?)notepad) : i);
        var (code, output) = Check(Zip(items), Version());
        Assert.True(code == 1, output);
    }

    [Fact]
    public void 同じ名前が2つあれば失敗する()
    {
        var (code, output) = Check(Zip(Official().Append((Protocol.ReTacExe, UpdaterExe()))), Version());
        Assert.True(code == 1, output);
    }

    [Fact]
    public void 大文字小文字だけ違う名前があれば失敗する()
    {
        var (code, output) = Check(Zip(Official().Append(("readme.md", null))), Version());
        Assert.True(code == 1, output);
    }

    [Fact]
    public void フォルダを含めば失敗する()
    {
        var (code, output) = Check(Zip(Official().Append(("sub/README.md", null))), Version());
        Assert.True(code == 1, output);
    }

    // ---- 副作用が無いこと ----

    [Fact]
    public void 検査してもzipの中のexeを起動しない()
    {
        // 起動すれば画面を開くアップデータを入れた zip。版を違えて失格にする（失格の zip の検査で、中の exe が動かないこと）
        static int Running() => Process.GetProcessesByName("ReTAC.Updater").Length;
        var before = Running();

        var (code, output) = Check(Zip(Official()), "9.9.9");
        Thread.Sleep(500);

        Assert.True(code == 1, output);
        Assert.Equal(before, Running());
    }

    [Fact]
    public void スクリプトはexeの起動もプロセスの照会もしない()
    {
        // 権限の無い環境（WMI を読めないなど）でも正しい zip を検査できるように、そうした呼び出しを使わない
        var text = File.ReadAllText(Script());
        foreach (var forbidden in new[] { "Get-CimInstance", "Get-WmiObject", "Start-Process", "Process]::Start", "Invoke-Item", "& $" })
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
    }
}
