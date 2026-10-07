using System.IO.Compression;

namespace ReTAC.Domain.Tests;

/// <summary>
/// 実機調査の作業場所（ProbeWorkspace）が、既にあるフォルダ・ファイルを消さない・上書きしないこと（S4。2026-10-07 の 2 度目のインシデントの再発防止）。
/// </summary>
public class ProbeWorkspaceTests
{
    [Fact]
    public void Create_ExistingFolderIsUntouched() => Sta.WithTempFolder(root =>
    {
        var existing = Directory.CreateDirectory(Path.Combine(root, "vprobe-same")).FullName;
        File.WriteAllText(Path.Combine(existing, "precious.txt"), "!");
        Directory.CreateDirectory(Path.Combine(existing, "abs-target"));
        File.WriteAllText(Path.Combine(existing, "abs-target", "escape-abs1.txt"), "keep");

        Assert.Null(ProbeWorkspace.Create(root, "vprobe-same"));

        // 中身も数も変わらない（見張りの名前と同じファイルも上書きしない）
        Assert.Equal("!", File.ReadAllText(Path.Combine(existing, "precious.txt")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(existing, "abs-target", "escape-abs1.txt")));
        // precious.txt・abs-target（フォルダ）・abs-target\escape-abs1.txt の 3 件のまま。何も足していない
        Assert.Equal(3, Directory.GetFileSystemEntries(existing, "*", SearchOption.AllDirectories).Length);
    });

    [Fact]
    public void Create_LeavesSiblingsUntouched() => Sta.WithTempFolder(root =>
    {
        var sibling = Directory.CreateDirectory(Path.Combine(root, "vprobe-data", "abs-target")).FullName;
        File.WriteAllText(Path.Combine(sibling, "escape-abs1.txt"), "keep");
        var before = OutsideWriteCheck.Snapshot([root], Path.Combine(root, "nothing"));

        var workspace = ProbeWorkspace.Create(root);
        Assert.NotNull(workspace);
        workspace!.NewRun();

        // 作業場所の外（ほかのフォルダ）は、中身も含めて変わらない
        Assert.Equal("PASS", OutsideWriteCheck.Judge(before,
            OutsideWriteCheck.Snapshot([root], Path.Combine(root, "nothing")).Where(p => !p.Key.StartsWith(workspace.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase)));
    });

    [Fact]
    public void CreateNew_RefusesExistingFile() => Sta.WithTempFolder(root =>
    {
        var path = Path.Combine(root, "sentinel.txt");
        File.WriteAllText(path, "keep");
        Assert.Throws<IOException>(() => ProbeWorkspace.CreateNew(path, "original"));
        Assert.Equal("keep", File.ReadAllText(path));
    });

    [Fact]
    public void Create_EvilNamesAreIntendedAndPointInside() => Sta.WithTempFolder(root =>
    {
        var workspace = ProbeWorkspace.Create(root)!;
        var entries = ProbeWorkspace.EvilEntries(workspace.AbsTarget);
        using (var check = ZipFile.OpenRead(workspace.EvilZip))
            Assert.Equal(entries, check.Entries.Select(e => e.FullName));
        // 絶対パスの 2 つは本当の絶対パスで、作業場所の中を指す
        foreach (var absolute in entries[5..])
        {
            Assert.True(Path.IsPathFullyQualified(absolute), absolute);
            Assert.StartsWith(workspace.Root + Path.DirectorySeparatorChar, Path.GetFullPath(absolute), StringComparison.OrdinalIgnoreCase);
        }
        Assert.All(workspace.AbsSentinels(), s => Assert.Equal("original", File.ReadAllText(s)));
    });

    [Fact]
    public void NewRun_IsFreshEachTime() => Sta.WithTempFolder(root =>
    {
        var workspace = ProbeWorkspace.Create(root)!;
        var first = workspace.NewRun()!;
        var second = workspace.NewRun()!;
        Assert.NotEqual(first.Root, second.Root);
        Assert.Empty(Directory.GetFileSystemEntries(second.Output));
        Assert.All(second.Sentinels, s => Assert.Equal("original", File.ReadAllText(s)));
        // 見張りはすべて取り出し先の外
        Assert.All(second.Sentinels, s => Assert.False(s.StartsWith(second.Output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)));
    });
}
