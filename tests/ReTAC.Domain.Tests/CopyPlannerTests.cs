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

    public void Dispose()
    {
        // ジャンクションは、再帰の削除では消せない（拒否される）。先にリンクだけを消す（先のフォルダの中身には触れない）
        foreach (var link in new DirectoryInfo(_root).EnumerateDirectories("*", SearchOption.AllDirectories)
                     .Where(d => d.Attributes.HasFlag(FileAttributes.ReparsePoint)).ToList())
            link.Delete();
        Directory.Delete(_root, recursive: true);
    }

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
    public void 宛先に無いフォルダはフォルダごと1件にする()
    {
        var source = Dir("src");
        var destination = Dir("dst");
        var inner = Path.Combine(source, "inner");
        Directory.CreateDirectory(inner);
        File_(source, "a.txt", New);
        File_(inner, "b.txt", New);

        var plan = CopyPlanner.Build([source], destination, CopyCondition.NewerOnly);

        var item = Assert.Single(plan.Items);
        Assert.True(item.WholeFolder);
        Assert.Equal(source, item.Source);
        Assert.Equal(destination, item.DestinationFolder);
        Assert.Equal(Path.Combine(destination, "src"), item.Target);
        Assert.Equal(DestinationState.Absent, item.Expected);
        Assert.Empty(plan.Folders);
        Assert.Equal(0, plan.Conflicts);
    }

    [Fact]
    public void 宛先にあるフォルダは中をたどり_無い入れ子だけフォルダごとにする()
    {
        var source = Dir("src");
        var destination = Dir("dst");
        Directory.CreateDirectory(Path.Combine(source, "old"));
        Directory.CreateDirectory(Path.Combine(source, "fresh"));
        File_(source, "a.txt", New);
        File_(Path.Combine(source, "old"), "b.txt", New);
        File_(Path.Combine(source, "fresh"), "c.txt", New);
        Directory.CreateDirectory(Path.Combine(destination, "src", "old"));

        var plan = CopyPlanner.Build([source], destination, CopyCondition.NewerOnly);

        Assert.Equal(
            [Path.Combine(destination, "src"), Path.Combine(destination, "src", "old")],
            plan.Folders.Select(f => f.FullPath));
        Assert.All(plan.Folders, f => Assert.Equal(DestinationKind.Folder, f.Expected.Kind));
        // 中をたどった転送元を持つ（移動のあとの後片付けは、これだけを対象にする）
        Assert.Equal([source, Path.Combine(source, "old")], plan.Folders.Select(f => f.Source));
        Assert.Equal(
            ["a.txt", "b.txt", "fresh"],
            plan.Items.Select(i => Path.GetFileName(i.Source)).Order());
        Assert.True(plan.Items.Single(i => Path.GetFileName(i.Source) == "fresh").WholeFolder);
        Assert.False(plan.Items.Single(i => Path.GetFileName(i.Source) == "a.txt").WholeFolder);
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
    public void 宛先のフォルダが無ければ作るフォルダに入り_衝突件数はゼロ()
    {
        // 移動をフォルダごと OS に渡してよいかの判断に使う。数千件の移動で効く
        var source = Path.Combine(_root, "src");
        Directory.CreateDirectory(Path.Combine(source, "sub"));
        File.WriteAllText(Path.Combine(source, "a.txt"), "a");
        File.WriteAllText(Path.Combine(source, "sub", "b.txt"), "b");

        var plan = CopyPlanner.Build([source], Path.Combine(_root, "dst"), CopyCondition.NewerOnly);

        Assert.Equal(0, plan.Conflicts);
        var item = Assert.Single(plan.Items);
        Assert.True(item.WholeFolder);
        // 宛先のフォルダが無ければ、フォルダごと渡す前に作る（OS は無いフォルダを宛先にできない）
        var folder = Assert.Single(plan.Folders);
        Assert.Equal(Path.Combine(_root, "dst"), folder.FullPath);
        Assert.Equal(DestinationKind.Absent, folder.Expected.Kind);
        Assert.Null(folder.Source);
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

    /// <summary>ジャンクションは管理者権限なしで作れる（シンボリックリンクは権限が要る）。</summary>
    private static void Junction(string link, string target)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false })!;
        process.WaitForExit();
        Assert.True(new DirectoryInfo(link).Attributes.HasFlag(FileAttributes.ReparsePoint));
    }

    [Fact]
    public void 配下にリンクがあるフォルダはフォルダごと渡さず_リンクの中も渡さない()
    {
        var source = Dir("src");
        var destination = Dir("dst");
        var outside = Dir("outside");
        File_(outside, "secret.txt", New);
        Directory.CreateDirectory(Path.Combine(source, "sub"));
        File_(source, "a.txt", New);
        File_(Path.Combine(source, "sub"), "b.txt", New);
        Junction(Path.Combine(source, "sub", "link"), outside);

        var plan = CopyPlanner.Build([source], destination, CopyCondition.NewerOnly);

        Assert.DoesNotContain(plan.Items, i => i.WholeFolder);
        Assert.Equal(["a.txt", "b.txt"], plan.Items.Select(i => Path.GetFileName(i.Source)).Order());
        // リンクの所には空のフォルダを作るだけ（今までと同じ）。中はたどらず、後片付けの対象にもしない
        var link = plan.Folders.Single(f => f.FullPath == Path.Combine(destination, "src", "sub", "link"));
        Assert.Null(link.Source);
        Assert.DoesNotContain(plan.Items, i => i.Source.EndsWith("secret.txt"));
    }

    [Fact]
    public void リンクの無い枝は_リンクのある枝の隣でもフォルダごと渡す()
    {
        var source = Dir("src");
        var destination = Dir("dst");
        var outside = Dir("outside");
        Directory.CreateDirectory(Path.Combine(source, "clean"));
        File_(Path.Combine(source, "clean"), "c.txt", New);
        Junction(Path.Combine(source, "link"), outside);

        var plan = CopyPlanner.Build([source], destination, CopyCondition.NewerOnly);

        var clean = Assert.Single(plan.Items);
        Assert.True(clean.WholeFolder);
        Assert.Equal(Path.Combine(source, "clean"), clean.Source);
    }

    [Fact]
    public void 転送元のフォルダと同じ名前のファイルが宛先にあれば_フォルダごと渡さない()
    {
        var source = Dir("src");
        var destination = Dir("dst");
        File_(source, "a.txt", New);
        File_(destination, "src", New);   // フォルダ src と同じ名前のファイル

        var plan = CopyPlanner.Build([source], destination, CopyCondition.NewerOnly);

        Assert.DoesNotContain(plan.Items, i => i.WholeFolder);
        var folder = Assert.Single(plan.Folders);
        Assert.Equal(DestinationKind.File, folder.Expected.Kind);
    }

    [Fact]
    public void 転送元のファイルと同じ名前のフォルダが宛先にあっても衝突にせず_宛先の状態はフォルダ()
    {
        var source = Dir("src");
        var destination = Dir("dst");
        File_(source, "x", New);
        Directory.CreateDirectory(Path.Combine(destination, "x"));

        var plan = CopyPlanner.Build([Path.Combine(source, "x")], destination, CopyCondition.NewerOnly);

        var item = Assert.Single(plan.Items);
        Assert.False(item.WholeFolder);
        Assert.Equal(DestinationKind.Folder, item.Expected.Kind);
        Assert.Equal(0, plan.Conflicts);
    }

    [Fact]
    public void 上書きする項目は宛先のファイルの日時とサイズを持ち_別名の項目は無い状態を持つ()
    {
        var source = Dir("src");
        var destination = Dir("dst");
        File_(source, "a.txt", New);
        var existing = File_(destination, "a.txt", Old);

        var overwrite = Assert.Single(CopyPlanner.Build([Path.Combine(source, "a.txt")], destination, CopyCondition.Overwrite).Items);
        Assert.Equal(DestinationState.Of(existing), overwrite.Expected);   // 計画の時点と、直に取った状態が同じ値になる
        Assert.Equal(DestinationKind.File, overwrite.Expected.Kind);

        var renamed = Assert.Single(CopyPlanner.Build([Path.Combine(source, "a.txt")], destination, CopyCondition.RenameCopy).Items);
        Assert.Equal("a (2).txt", renamed.NewName);
        Assert.Equal(Path.Combine(destination, "a (2).txt"), renamed.Target);
        Assert.Equal(DestinationState.Absent, renamed.Expected);
    }

    [Fact]
    public void 宛先の今の状態を種類ごとに返す()
    {
        var folder = Dir("state");
        var file = File_(folder, "f (1) [x].txt", New);   // 一覧の名前の指定で特別に読まれる文字が無いこと
        Assert.Equal(DestinationState.Absent, DestinationState.Of(Path.Combine(folder, "none")));
        Assert.Equal(DestinationState.Absent, DestinationState.Of(Path.Combine(folder, "none", "deeper")));   // 親も無い
        Assert.Equal(DestinationState.Folder, DestinationState.Of(folder));
        Assert.Equal(DestinationState.Folder, DestinationState.Of(folder + Path.DirectorySeparatorChar));
        var state = DestinationState.Of(file);
        Assert.Equal(DestinationKind.File, state.Kind);
        Assert.Equal(new FileInfo(file).Length, state.Size);
        Assert.Equal(File.GetLastWriteTimeUtc(file), state.WriteTimeUtc);
    }

    [Fact]
    public void 隠しファイルとシステムファイルも宛先にあるものとして見る()
    {
        var source = Dir("src");
        var destination = Dir("dst");
        File_(source, "h.txt", New);
        var hidden = File_(destination, "h.txt", Old);
        File.SetAttributes(hidden, FileAttributes.Hidden | FileAttributes.System);

        var plan = CopyPlanner.Build([Path.Combine(source, "h.txt")], destination, CopyCondition.Skip);

        Assert.Equal(1, plan.Conflicts);
        Assert.Equal(DestinationKind.File, DestinationState.Of(hidden).Kind);
        File.SetAttributes(hidden, FileAttributes.Normal);   // 後始末で消せるように
    }

    [Fact]
    public void 宛先がフォルダでなければ_無いものとして扱わず例外を出す()
    {
        // 「無い」と「読めない」を分ける。読めない宛先を「無い」として計画に入れると、あとで「転送先が変わった」と誤って知らせる
        var source = Dir("src");
        File_(source, "a.txt", New);
        var notFolder = File_(_root, "not-a-folder", New);

        Assert.ThrowsAny<IOException>(() => CopyPlanner.Build([Path.Combine(source, "a.txt")], notFolder, CopyCondition.NewerOnly));
    }

    [Fact]
    public void 別のフォルダの同じ名前のファイルは_後の方を衝突として判定する()
    {
        var (first, second, destination) = (Dir("first"), Dir("second"), Dir("dst"));
        var older = File_(first, "a.txt", Old);
        var newer = File_(second, "a.txt", New);
        var conflicts = new List<CopyPlanner.Conflict>();

        var plan = CopyPlanner.Build([older, newer], destination, c => { conflicts.Add(c); return CopyCondition.NewerOnly; });

        // 1 件目はそのまま、2 件目は「1 件目を転送したあとの宛先」との衝突。新しいので上書きする
        Assert.Equal([older, newer], plan.Items.Select(i => i.Source));
        var conflict = Assert.Single(conflicts);
        Assert.Equal(newer, conflict.Source);
        Assert.Equal(Path.Combine(destination, "a.txt"), conflict.Destination);
        Assert.Equal(Old, conflict.DestinationTime);
        // 上書きの項目が持つ宛先の状態は、1 件目を転送したあとの状態（1 件目の日時とサイズ）
        Assert.Equal(DestinationState.OfFile(new FileInfo(older)), plan.Items[1].Expected);

        // 古い方が後なら、転送しない
        var reversed = CopyPlanner.Build([newer, older], destination, CopyCondition.NewerOnly);
        Assert.Equal([newer], reversed.Items.Select(i => i.Source));
        Assert.Equal(1, reversed.Skipped);
    }

    [Fact]
    public void 別名の宛先が_ほかの項目の宛先と重ならない()
    {
        var (source, destination) = (Dir("src"), Dir("dst"));
        var a = File_(source, "a.txt", New);
        var a2 = File_(source, "a (2).txt", New);
        File_(destination, "a.txt", Old);

        var plan = CopyPlanner.Build([a, a2], destination, CopyCondition.RenameCopy);

        Assert.Equal(2, plan.Items.Count);
        Assert.Equal(plan.Items.Count, plan.Items.Select(i => i.Target).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(Path.Combine(destination, "a (2).txt"), plan.Items[0].Target);      // a.txt の別名
        Assert.Equal(Path.Combine(destination, "a (2) (2).txt"), plan.Items[1].Target);  // 転送元の a (2).txt は、その別名と衝突して別名になる
    }

    [Theory]
    [InlineData(@"C:\a\b", @"C:\x", true)]
    [InlineData(@"c:\a", @"C:\x", true)]
    [InlineData(@"C:\a", @"D:\x", false)]
    [InlineData(@"\\srv\share\a", @"\\srv\share\b", true)]
    [InlineData(@"\\srv\share\a", @"\\srv\other\b", false)]
    [InlineData(@"C:\a", @"\\srv\share\b", false)]
    public void 同じドライブかどうかは_ルートで決める(string source, string destination, bool expected) =>
        Assert.Equal(expected, CopyPlanner.SameVolume(source, destination));

    [Fact]
    public void 同じドライブの中の移動は_配下にリンクがあっても_フォルダ自身がリンクでも_フォルダごと渡す()
    {
        // 名前の付け替えで済むので、OS はリンクの先をたどらない（今までの動作を保つ）
        var source = Dir("src");
        var destination = Dir("dst");
        var outside = Dir("outside");
        Directory.CreateDirectory(Path.Combine(source, "pack"));
        File_(Path.Combine(source, "pack"), "a.txt", New);
        Junction(Path.Combine(source, "pack", "link"), outside);
        Junction(Path.Combine(source, "toplink"), outside);

        var plan = CopyPlanner.Build([Path.Combine(source, "pack"), Path.Combine(source, "toplink")], destination,
            CopyCondition.NewerOnly, moving: true);

        Assert.Equal(2, plan.Items.Count);
        Assert.All(plan.Items, i => Assert.True(i.WholeFolder));
        Assert.Empty(plan.Folders);

        // コピーなら、リンクのあるフォルダは 1 件ずつ、リンクそのものは空のフォルダだけ
        var copy = CopyPlanner.Build([Path.Combine(source, "pack"), Path.Combine(source, "toplink")], destination, CopyCondition.NewerOnly);
        Assert.DoesNotContain(copy.Items, i => i.WholeFolder);
    }

    [Fact]
    public void 別のドライブへの移動は_リンクがあればコピーと同じく1件ずつ渡す()
    {
        // ドライブをまたぐ移動は「コピーして消す」になり、OS がリンクの先をコピーするおそれがある
        var source = Dir("src");
        var outside = Dir("outside");
        Directory.CreateDirectory(Path.Combine(source, "pack"));
        File_(Path.Combine(source, "pack"), "a.txt", New);
        Junction(Path.Combine(source, "pack", "link"), outside);
        // 使われていないドライブ文字を宛先にする（実際には書かない。計画を作るだけ）
        var used = Environment.GetLogicalDrives().Select(d => char.ToUpperInvariant(d[0])).ToHashSet();
        var free = Enumerable.Range('D', 23).Select(c => (char)c).First(c => !used.Contains(c));
        var destination = $@"{free}:\dst";

        var plan = CopyPlanner.Build([Path.Combine(source, "pack")], destination, CopyCondition.NewerOnly, moving: true);

        Assert.DoesNotContain(plan.Items, i => i.WholeFolder);
        Assert.Equal(["a.txt"], plan.Items.Select(i => Path.GetFileName(i.Source)));
        Assert.Null(plan.Folders.Single(f => f.FullPath == Path.Combine(destination, "pack", "link")).Source);
    }

    [Fact]
    public void たどったフォルダの数を知らせる()
    {
        var source = Dir("src");
        var destination = Dir("dst");
        Directory.CreateDirectory(Path.Combine(destination, "src"));
        Directory.CreateDirectory(Path.Combine(source, "sub"));
        var counts = new List<int>();

        CopyPlanner.Build([source], destination, CopyCondition.NewerOnly, counts.Add);

        Assert.NotEmpty(counts);
        Assert.Equal(counts.Order(), counts);   // 増える一方
    }
}
