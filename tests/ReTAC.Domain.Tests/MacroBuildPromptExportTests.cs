using System.Text;
using ReTAC.App;

namespace ReTAC.Domain.Tests;

/// <summary>R-145-1: エクスポートの書き込み。保存先と隣のファイルを、成功時も失敗時も壊さない。</summary>
public sealed class MacroBuildPromptExportTests : IDisposable
{
    private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retac-p24-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Success_ReplacesTarget_KeepsNeighbor_LeavesNoTemp()
    {
        var target = Path.Combine(_dir, "prompt.md");
        File.WriteAllText(target, "old");
        File.WriteAllText(target + ".tmp", "neighbor");   // 利用者の同名のファイル

        SafeFileWrite.WriteReplacing(target, "新しい");

        Assert.Equal("新しい", File.ReadAllText(target, Encoding.UTF8));
        Assert.False(File.ReadAllBytes(target).AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));   // BOM なし
        Assert.Equal("neighbor", File.ReadAllText(target + ".tmp"));
        Assert.Equal(new[] { "prompt.md", "prompt.md.tmp" }, Directory.GetFiles(_dir).Select(Path.GetFileName).Order());
    }

    // コードレビュー（2026-10-07）: 一時ファイル名に保存先の名前を足すと、有効な長い名前で上限を超えて失敗した
    [Fact]
    public void LongValidName_IsWritten_KeepsNeighbor_LeavesNoTemp()
    {
        var name = new string('長', 230) + ".md";   // 233 文字。1 つの名前の上限（255）以内
        var target = Path.Combine(_dir, name);
        File.WriteAllText(target, "old");
        File.WriteAllText(Path.Combine(_dir, "neighbor.tmp"), "neighbor");

        SafeFileWrite.WriteReplacing(target, "新しい");

        Assert.Equal("新しい", File.ReadAllText(target, Encoding.UTF8));
        Assert.Equal("neighbor", File.ReadAllText(Path.Combine(_dir, "neighbor.tmp")));
        Assert.Equal(new[] { "neighbor.tmp", name }.Order(), Directory.GetFiles(_dir).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void Failure_KeepsTargetAndNeighbor_LeavesNoTemp()
    {
        // 保存先が同名のフォルダだと置き換え（File.Move）が失敗する
        var target = Path.Combine(_dir, "prompt.md");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "inside.txt"), "keep");
        File.WriteAllText(target + ".tmp", "neighbor");

        var error = Record.Exception(() => SafeFileWrite.WriteReplacing(target, "新しい"));

        Assert.True(error is IOException or UnauthorizedAccessException, error?.GetType().Name);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(target, "inside.txt")));
        Assert.Equal("neighbor", File.ReadAllText(target + ".tmp"));
        Assert.Equal(new[] { "prompt.md.tmp" }, Directory.GetFiles(_dir).Select(Path.GetFileName));
    }
}
