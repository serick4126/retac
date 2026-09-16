using ReTAC.Shell;

namespace ReTAC.Domain.Tests;

/// <summary>F-03 / 予定 §1.4 A-1・A-4: 実行ファイルの解決と、コンソールか GUI かの判定</summary>
public class ExecutableTests
{
    private static readonly string System32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
    private static readonly string Cmd = Path.Combine(System32, "cmd.exe");

    [Fact]
    public void 名前だけならPATHから探す()
    {
        var found = ExecutableResolver.Resolve("cmd.exe", $@"C:\no-such-dir;{System32}", ".COM;.EXE", _ => null);
        Assert.Equal(Cmd, found, ignoreCase: true);
    }

    [Fact]
    public void 拡張子を省いてもPATHEXTの拡張子で探す()
    {
        Assert.Equal(Cmd, ExecutableResolver.Resolve("cmd", System32, ".COM;.EXE", _ => null), ignoreCase: true);
    }

    [Fact]
    public void PATHに無ければAppPathsを使う()
    {
        var dir = Directory.CreateTempSubdirectory("retac-test-");
        try
        {
            var tool = Path.Combine(dir.FullName, "retac-test-tool.exe");
            File.WriteAllText(tool, "");
            var found = ExecutableResolver.Resolve("retac-test-tool.exe", "", ".EXE",
                name => name.Equals("retac-test-tool.exe", StringComparison.OrdinalIgnoreCase) ? tool : null);
            Assert.Equal(tool, found);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void 見つからなければnull()
    {
        Assert.Null(ExecutableResolver.Resolve("no-such-tool-retac.exe", System32, ".EXE", _ => null));
        Assert.Null(ExecutableResolver.Resolve("", System32, ".EXE", _ => null));
    }

    [Fact]
    public void パス付きなら在るかどうかだけを見る()
    {
        Func<string, string?> never = _ => throw new InvalidOperationException("App Paths を引いてはいけない");
        Assert.Equal(Cmd, ExecutableResolver.Resolve(Cmd, "", ".EXE", never), ignoreCase: true);
        Assert.Null(ExecutableResolver.Resolve(@"C:\no-such-dir\x.exe", "", ".EXE", never));
    }

    [Theory]
    [InlineData("a.exe", true)]
    [InlineData("a.BAT", true)]
    [InlineData("a.cmd", true)]
    [InlineData("script.py", false)]
    [InlineData("build.ps1", false)]
    [InlineData("noext", false)]
    public void 実行できる種類かはPATHEXTで決める(string path, bool expected)
    {
        Assert.Equal(expected, ExecutableResolver.IsExecutableType(path, ".COM;.EXE;.BAT;.CMD"));
    }

    [Fact]
    public void cmdはコンソールでexplorerはGUI()
    {
        Assert.Equal(ExecutableKind.Console, ExecutableKinds.Of(Cmd));
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        Assert.Equal(ExecutableKind.Gui, ExecutableKinds.Of(explorer));
    }

    [Fact]
    public void 実行ファイルでなければ判定できない()
    {
        var bat = Path.Combine(Path.GetTempPath(), $"retac-test-{Guid.NewGuid():N}.bat");
        File.WriteAllText(bat, "@echo off");
        try
        {
            Assert.Equal(ExecutableKind.Unknown, ExecutableKinds.Of(bat));
        }
        finally
        {
            File.Delete(bat);
        }
        Assert.Equal(ExecutableKind.Unknown, ExecutableKinds.Of(@"C:\no-such-dir\x.exe"));
    }

    [Fact]
    public void 初期登録のツールはどのWindowsでも解決できる()
    {
        // B-05: 設定ファイルが無い環境でも最低限動く、という既定値の前提そのもの。
        // ここが解けないと初回起動の利用者は E を押しても何も起きない
        foreach (var tool in ReTAC.Domain.Tools.DefaultExternalTools.Create())
        {
            var resolved = ExecutableResolver.Resolve(tool.Path);
            Assert.NotNull(resolved);
            Assert.True(File.Exists(resolved), $"{tool.Name}: {tool.Path} → {resolved}");
        }
    }
}
