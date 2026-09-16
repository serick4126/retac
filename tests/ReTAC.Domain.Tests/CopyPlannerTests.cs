using System.IO;
using ReTAC.Domain.FileOps;

namespace ReTAC.Domain.Tests;

/// <summary>
/// 複写条件の自前判定（R-41-4）。
/// <b>テストは自分で作った一時フォルダの中だけを触り、終わったら消す。</b>
/// </summary>
public sealed class CopyPlannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "retac_test_" + Guid.NewGuid().ToString("N"));

    public CopyPlannerTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Dir(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string File_(string folder, string name, DateTime written)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, name);
        File.SetLastWriteTime(path, written);
        return path;
    }

    private static readonly DateTime Old = new(2020, 1, 1, 0, 0, 0);
    private static readonly DateTime New = new(2026, 1, 1, 0, 0, 0);

    [Fact]
    public void 宛先が無ければそのまま転送する()
    {
        var source = Dir("src");
        var destination = Dir("dst");
        File_(source, "a.txt", New);

        var plan = CopyPlanner.Build([Path.Combine(source, "a.txt")], destination, CopyCondition.NewerOnly);

        Assert.Single(plan.Items);
        Assert.Equal(0, plan.Skipped);
        Assert.Null(plan.Items[0].NewName);
    }

    [Fact]
    public void 新しい時に複写は古い方を渡さない()
    {
        var source = Dir("src");
        var destination = Dir("dst");
        File_(source, "old.txt", Old);
        File_(source, "new.txt", New);
        File_(destination, "old.txt", New);    // 宛先の方が新しい → 転送しない
        File_(destination, "new.txt", Old);    // 元の方が新しい → 転送する

        var plan = CopyPlanner.Build(
            [Path.Combine(source, "old.txt"), Path.Combine(source, "new.txt")], destination, CopyCondition.NewerOnly);

        Assert.Equal(["new.txt"], plan.Items.Select(i => Path.GetFileName(i.Source)));
        Assert.Equal(1, plan.Skipped);
    }

    [Fact]
    public void スキップは何も渡さず上書きは全部渡す()
    {
        var source = Dir("src");
        var destination = Dir("dst");
        File_(source, "a.txt", Old);
        File_(destination, "a.txt", New);
        string[] sources = [Path.Combine(source, "a.txt")];

        Assert.Empty(CopyPlanner.Build(sources, destination, CopyCondition.Skip).Items);
        Assert.Single(CopyPlanner.Build(sources, destination, CopyCondition.Overwrite).Items);
    }

    [Fact]
    public void 名前を変更し複写は空いている番号を付ける()
    {
        var source = Dir("src");
        var destination = Dir("dst");
        File_(source, "a.txt", Old);
        File_(destination, "a.txt", New);
        File_(destination, "a (2).txt", New);

        var plan = CopyPlanner.Build([Path.Combine(source, "a.txt")], destination, CopyCondition.RenameCopy);

        Assert.Equal("a (3).txt", plan.Items[0].NewName);
    }

    [Fact]
    public void フォルダは再帰的にたどりファイルだけを転送する()
    {
        var source = Dir("src");
        var destination = Dir("dst");
        var inner = Path.Combine(source, "inner");
        Directory.CreateDirectory(inner);
        File_(source, "a.txt", New);
        File_(inner, "b.txt", New);

        var plan = CopyPlanner.Build([source], destination, CopyCondition.NewerOnly);

        Assert.Equal(2, plan.Items.Count);
        Assert.Equal(
            [Path.Combine(destination, "src"), Path.Combine(destination, "src", "inner")],
            plan.Folders);
    }

    [Fact]
    public void 既にある入れ子でも新しいものだけを転送する()
    {
        var source = Dir("src");
        var destination = Dir("dst");
        var inner = Path.Combine(source, "inner");
        Directory.CreateDirectory(inner);
        File_(inner, "b.txt", Old);

        var existing = Path.Combine(destination, "src", "inner");
        Directory.CreateDirectory(existing);
        File_(existing, "b.txt", New);

        var plan = CopyPlanner.Build([source], destination, CopyCondition.NewerOnly);

        Assert.Empty(plan.Items);
        Assert.Equal(1, plan.Skipped);
    }

    [Fact]
    public void 宛先に重なりが無ければ衝突件数はゼロになる()
    {
        // 移動をフォルダごと OS に渡してよいかの判断に使う。数千件の移動で効く
        var source = Path.Combine(_root, "src");
        Directory.CreateDirectory(Path.Combine(source, "sub"));
        File.WriteAllText(Path.Combine(source, "a.txt"), "a");
        File.WriteAllText(Path.Combine(source, "sub", "b.txt"), "b");

        var plan = CopyPlanner.Build([source], Path.Combine(_root, "dst"), CopyCondition.NewerOnly);

        Assert.Equal(0, plan.Conflicts);
        Assert.Equal(2, plan.Items.Count);
    }

    [Fact]
    public void 重なりが一件でもあれば衝突件数に出る()
    {
        var source = Path.Combine(_root, "src2");
        var destination = Path.Combine(_root, "dst2");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(source, "a.txt"), "new");
        File.WriteAllText(Path.Combine(source, "b.txt"), "new");
        File.WriteAllText(Path.Combine(destination, "a.txt"), "old");

        var plan = CopyPlanner.Build([Path.Combine(source, "a.txt"), Path.Combine(source, "b.txt")],
                                     destination, CopyCondition.Overwrite);

        Assert.Equal(1, plan.Conflicts);
    }
}
