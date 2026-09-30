using System.IO;
using ReTAC.App;
using ReTAC.Domain.FileOps;
using ReTAC.Shell;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-125: 計画の後に宛先が変わっていたら、その項目から先を転送しない。本物の OS の転送（IFileOperation）で確かめる。
/// 元に戻すの記録（R-84）が、実際に転送した項目だけを持ち、上書きした項目にだけ上書きの印が付くことも確かめる。
/// <b>テストは自分で作った一時フォルダの中だけを触り、終わったら消す。</b>
/// </summary>
public sealed class TransferExecutionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "retac_exec_" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Old = new(2020, 1, 1), New = new(2026, 1, 1), Newer = new(2026, 6, 1);

    public TransferExecutionTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }

    private string Dir(string name) { var p = Path.Combine(_root, name); Directory.CreateDirectory(p); return p; }

    private static string Write(string folder, string name, string text, DateTime written)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, text);
        File.SetLastWriteTime(path, written);
        return path;
    }

    private static void Junction(string link, string target)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false })!;
        process.WaitForExit();
    }

    /// <param name="Changed">計画の後に変わっていた宛先（作るフォルダ・転送する項目のどちらでも）</param>
    /// <param name="Record">元に戻すの記録（転送が 0 件で、作ったフォルダも無ければ null）</param>
    /// <param name="Removed">後片付けで消した転送元のフォルダ</param>
    private sealed record Outcome(bool Completed, string? Changed, UndoRecord? Record, IReadOnlyList<string> Removed);

    /// <summary>計画 →（宛先を変える）→ 実行。本番の MainForm.ExecuteTransfer と同じ順を通る。</summary>
    private static Outcome Run(CopyPlan plan, bool moving, Action? changeDestination = null)
    {
        changeDestination?.Invoke();
        var recorder = new UndoRecorder();
        var removed = new List<string>();
        (bool Completed, string? Changed) result = default;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var operation = new ShellFileOperation(IntPtr.Zero, silentOverwrite: true, noUi: true);
                var changedFolder = TransferExecution.Register(plan, moving, operation, recorder);
                var completed = operation.Execute();
                recorder.AddResults(operation.Results);
                var changed = changedFolder ?? operation.ChangedDestination;
                TransferExecution.Finish(plan, moving, changed, f => { recorder.AddRemovedFolder(f); removed.Add(f); });
                result = (completed && changedFolder is null, changed);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)));
        Assert.Null(failure);
        var history = new UndoHistory();
        recorder.Commit(history);
        return new Outcome(result.Completed, result.Changed, history.Peek(), removed);
    }

    private static UndoItem? ItemFor(UndoRecord? record, string after) =>
        record?.Items.FirstOrDefault(i => string.Equals(i.After, after, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void 宛先が変わっていなければ計画どおりに転送し_上書きした項目にだけ印が付く()
    {
        var (source, destination) = (Dir("src"), Dir("dst"));
        Write(source, "new.txt", "new", New);
        Write(source, "over.txt", "source", New);
        Write(destination, "over.txt", "old", Old);
        Directory.CreateDirectory(Path.Combine(source, "sub"));
        Write(Path.Combine(source, "sub"), "deep.txt", "deep", New);
        var plan = CopyPlanner.Build([Path.Combine(source, "new.txt"), Path.Combine(source, "over.txt"), Path.Combine(source, "sub")],
            destination, CopyCondition.NewerOnly);

        var outcome = Run(plan, moving: false);

        Assert.True(outcome.Completed);
        Assert.Null(outcome.Changed);
        Assert.Equal("new", File.ReadAllText(Path.Combine(destination, "new.txt")));
        Assert.Equal("source", File.ReadAllText(Path.Combine(destination, "over.txt")));
        Assert.Equal("deep", File.ReadAllText(Path.Combine(destination, "sub", "deep.txt")));   // フォルダごと渡した分
        // R-84: 上書きした項目にだけ上書きの印（元に戻すで、上書きされた側を失わないための印）
        Assert.False(ItemFor(outcome.Record, Path.Combine(destination, "new.txt"))!.Overwrote);
        Assert.True(ItemFor(outcome.Record, Path.Combine(destination, "over.txt"))!.Overwrote);
        // フォルダごとコピーした分は、フォルダが記録に載る（元に戻すで、フォルダごと消せる）
        var folder = ItemFor(outcome.Record, Path.Combine(destination, "sub"));
        Assert.NotNull(folder);
        Assert.True(folder!.Stamp.IsFolder);
        Assert.False(folder.Overwrote);
    }

    [Fact]
    public void 途中の項目の宛先が変わっていたら_そこで止まり_転送した分だけが記録に載る()
    {
        var (source, destination) = (Dir("src"), Dir("dst"));
        Write(source, "a.txt", "source-a", New);
        Write(source, "b.txt", "source-b", New);
        Write(source, "c.txt", "source-c", New);
        var plan = CopyPlanner.Build(
            [Path.Combine(source, "a.txt"), Path.Combine(source, "b.txt"), Path.Combine(source, "c.txt")], destination, CopyCondition.NewerOnly);

        var outcome = Run(plan, moving: false, () => Write(destination, "b.txt", "appeared", Newer));

        Assert.False(outcome.Completed);
        Assert.Equal(Path.Combine(destination, "b.txt"), outcome.Changed);
        Assert.Equal("source-a", File.ReadAllText(Path.Combine(destination, "a.txt")));   // 1 件目は転送済み
        Assert.Equal("appeared", File.ReadAllText(Path.Combine(destination, "b.txt")));   // 2 件目は上書きしない
        Assert.False(File.Exists(Path.Combine(destination, "c.txt")));                    // 3 件目は転送しない
        // 記録に載るのは、実際に転送した 1 件目だけ
        Assert.NotNull(ItemFor(outcome.Record, Path.Combine(destination, "a.txt")));
        Assert.Null(ItemFor(outcome.Record, Path.Combine(destination, "b.txt")));
        Assert.Null(ItemFor(outcome.Record, Path.Combine(destination, "c.txt")));
    }

    [Fact]
    public void ファイルの宛先に同じ名前のフォルダが現れたら転送しない()
    {
        var (source, destination) = (Dir("src"), Dir("dst"));
        Write(source, "a.txt", "source", New);
        var plan = CopyPlanner.Build([Path.Combine(source, "a.txt")], destination, CopyCondition.NewerOnly);

        var outcome = Run(plan, moving: false, () => Directory.CreateDirectory(Path.Combine(destination, "a.txt")));

        Assert.False(outcome.Completed);
        Assert.Equal(Path.Combine(destination, "a.txt"), outcome.Changed);
        Assert.True(Directory.Exists(Path.Combine(destination, "a.txt")));
        Assert.Null(outcome.Record);
    }

    [Fact]
    public void フォルダごと渡す所に同じ名前のフォルダが現れたら転送しない()
    {
        var (source, destination) = (Dir("src"), Dir("dst"));
        Directory.CreateDirectory(Path.Combine(source, "pack"));
        Write(Path.Combine(source, "pack"), "x.txt", "source", New);
        var plan = CopyPlanner.Build([Path.Combine(source, "pack")], destination, CopyCondition.NewerOnly);
        Assert.True(Assert.Single(plan.Items).WholeFolder);

        var outcome = Run(plan, moving: false, () =>
        {
            Directory.CreateDirectory(Path.Combine(destination, "pack"));
            Write(Path.Combine(destination, "pack"), "x.txt", "appeared", Newer);
        });

        Assert.False(outcome.Completed);
        Assert.Equal(Path.Combine(destination, "pack"), outcome.Changed);
        Assert.Equal("appeared", File.ReadAllText(Path.Combine(destination, "pack", "x.txt")));
        Assert.Null(outcome.Record);
    }

    [Theory]
    [InlineData("replace")]   // さらに新しいファイルへ置き換える
    [InlineData("size")]      // 日時はそのまま、サイズだけ変える
    [InlineData("delete")]    // 消す
    [InlineData("folder")]    // フォルダに置き換える
    public void 上書きすると決めたファイルが変わっていたら上書きしない(string change)
    {
        var (source, destination) = (Dir("src"), Dir("dst"));
        Write(source, "a.txt", "source", New);
        var target = Write(destination, "a.txt", "old", Old);
        var plan = CopyPlanner.Build([Path.Combine(source, "a.txt")], destination, CopyCondition.NewerOnly);
        Assert.Equal(DestinationKind.File, Assert.Single(plan.Items).Expected.Kind);

        var outcome = Run(plan, moving: false, () =>
        {
            switch (change)
            {
                case "replace": Write(destination, "a.txt", "newer", Newer); break;
                case "size": File.WriteAllText(target, "old-but-longer"); File.SetLastWriteTime(target, Old); break;
                case "delete": File.Delete(target); break;
                case "folder": File.Delete(target); Directory.CreateDirectory(target); break;
            }
        });

        Assert.False(outcome.Completed);
        Assert.Equal(target, outcome.Changed);
        if (change == "replace") Assert.Equal("newer", File.ReadAllText(target));
        if (change == "size") Assert.Equal("old-but-longer", File.ReadAllText(target));
        if (change == "delete") Assert.False(File.Exists(target));
        if (change == "folder") Assert.True(Directory.Exists(target));
        Assert.Null(outcome.Record);
    }

    [Fact]
    public void 別名の宛先がふさがっていたら転送しない()
    {
        var (source, destination) = (Dir("src"), Dir("dst"));
        Write(source, "a.txt", "source", New);
        Write(destination, "a.txt", "old", Old);
        var plan = CopyPlanner.Build([Path.Combine(source, "a.txt")], destination, CopyCondition.RenameCopy);
        Assert.Equal("a (2).txt", Assert.Single(plan.Items).NewName);

        var outcome = Run(plan, moving: false, () => Write(destination, "a (2).txt", "appeared", Newer));

        Assert.False(outcome.Completed);
        Assert.Equal(Path.Combine(destination, "a (2).txt"), outcome.Changed);
        Assert.Equal("appeared", File.ReadAllText(Path.Combine(destination, "a (2).txt")));
    }

    [Fact]
    public void 同じ名前のファイルを2つ送ると_後の方は先の方を転送したあとの宛先と比べる()
    {
        // 別のフォルダから同じ名前を落とした場合。今までは黙って上書きしていた。計画が衝突として判定したとおりに進み、誤って中止しない
        var (first, second, destination) = (Dir("first"), Dir("second"), Dir("dst"));
        var older = Write(first, "a.txt", "older", Old);
        var newer = Write(second, "a.txt", "newer", New);
        var plan = CopyPlanner.Build([older, newer], destination, CopyCondition.NewerOnly);

        var outcome = Run(plan, moving: false);

        Assert.True(outcome.Completed);
        Assert.Null(outcome.Changed);
        Assert.Equal("newer", File.ReadAllText(Path.Combine(destination, "a.txt")));
    }

    [Theory]
    [InlineData(true)]    // 既にあったフォルダが消えた
    [InlineData(false)]   // 既にあったフォルダがファイルになった
    public void 中をたどったフォルダの種類が変わっていたら何も転送しない(bool deleted)
    {
        var (source, destination) = (Dir("src"), Dir("dst"));
        Directory.CreateDirectory(Path.Combine(source, "pack"));
        Write(Path.Combine(source, "pack"), "x.txt", "source", New);
        var existing = Path.Combine(destination, "pack");
        Directory.CreateDirectory(existing);
        var plan = CopyPlanner.Build([Path.Combine(source, "pack")], destination, CopyCondition.NewerOnly);

        var outcome = Run(plan, moving: false, () =>
        {
            Directory.Delete(existing);
            if (!deleted) File.WriteAllText(existing, "now a file");
        });

        Assert.False(outcome.Completed);
        Assert.Equal(existing, outcome.Changed);
        Assert.False(File.Exists(Path.Combine(existing, "x.txt")));
        if (deleted) Assert.False(Directory.Exists(existing));   // 作り直さない
        Assert.Null(outcome.Record);
    }

    [Fact]
    public void 移動でも宛先が変わっていたら動かさず_転送元は残る()
    {
        var (source, destination) = (Dir("src"), Dir("dst"));
        var file = Write(source, "a.txt", "source", New);
        var plan = CopyPlanner.Build([file], destination, CopyCondition.NewerOnly);

        var outcome = Run(plan, moving: true, () => Write(destination, "a.txt", "appeared", Newer));

        Assert.False(outcome.Completed);
        Assert.True(File.Exists(file));
        Assert.Equal("appeared", File.ReadAllText(Path.Combine(destination, "a.txt")));
    }

    [Fact]
    public void 中止した移動は_動かしていないフォルダの中の空のフォルダを消さない()
    {
        // フォルダごと渡す項目の配下は、後片付けの対象にしない。宛先には作られていないので、消すと戻せない
        var (source, destination) = (Dir("src"), Dir("dst"));
        var pack = Path.Combine(source, "pack");
        Directory.CreateDirectory(Path.Combine(pack, "empty", "deeper"));
        Write(pack, "x.txt", "source", New);
        var plan = CopyPlanner.Build([pack], destination, CopyCondition.NewerOnly);
        Assert.True(Assert.Single(plan.Items).WholeFolder);

        var outcome = Run(plan, moving: true, () => Directory.CreateDirectory(Path.Combine(destination, "pack")));

        Assert.False(outcome.Completed);
        Assert.Empty(outcome.Removed);
        Assert.True(Directory.Exists(Path.Combine(pack, "empty", "deeper")));
        Assert.True(File.Exists(Path.Combine(pack, "x.txt")));
    }

    [Fact]
    public void 宛先にあるフォルダへの移動は_中をたどった転送元のフォルダだけを消す()
    {
        var (source, destination) = (Dir("src"), Dir("dst"));
        var pack = Path.Combine(source, "pack");
        Directory.CreateDirectory(Path.Combine(pack, "sub"));
        Directory.CreateDirectory(Path.Combine(pack, "fresh", "empty"));   // fresh は宛先に無い。フォルダごと動く
        Write(pack, "x.txt", "x", New);
        Write(Path.Combine(pack, "sub"), "y.txt", "y", New);
        Write(Path.Combine(pack, "fresh"), "z.txt", "z", New);
        Directory.CreateDirectory(Path.Combine(destination, "pack", "sub"));
        var plan = CopyPlanner.Build([pack], destination, CopyCondition.NewerOnly);

        var outcome = Run(plan, moving: true);

        Assert.True(outcome.Completed);
        Assert.Equal("x", File.ReadAllText(Path.Combine(destination, "pack", "x.txt")));
        Assert.Equal("y", File.ReadAllText(Path.Combine(destination, "pack", "sub", "y.txt")));
        Assert.Equal("z", File.ReadAllText(Path.Combine(destination, "pack", "fresh", "z.txt")));
        Assert.True(Directory.Exists(Path.Combine(destination, "pack", "fresh", "empty")));   // 空の入れ子も一緒に動いた
        // 消したのは、中をたどった 2 つだけ（深い順）。fresh は OS が動かしたので、後片付けの対象でない
        Assert.Equal([Path.Combine(pack, "sub"), pack], outcome.Removed);
        Assert.False(Directory.Exists(pack));
        Assert.Equal(outcome.Removed.Order(), outcome.Record!.RemovedFolders.Order());
    }

    [Fact]
    public void 配下にリンクのあるフォルダをコピーしても_リンクの先の中身は渡らない()
    {
        var (source, destination, outside) = (Dir("src"), Dir("dst"), Dir("outside"));
        Write(outside, "secret.txt", "secret", New);
        Directory.CreateDirectory(Path.Combine(source, "pack"));
        Write(Path.Combine(source, "pack"), "x.txt", "source", New);
        Junction(Path.Combine(source, "pack", "link"), outside);
        try
        {
            var plan = CopyPlanner.Build([Path.Combine(source, "pack")], destination, CopyCondition.NewerOnly);

            var outcome = Run(plan, moving: false);

            Assert.True(outcome.Completed);
            Assert.Equal("source", File.ReadAllText(Path.Combine(destination, "pack", "x.txt")));
            Assert.False(File.Exists(Path.Combine(destination, "pack", "link", "secret.txt")));
        }
        finally { RemoveLink(Path.Combine(source, "pack", "link")); }   // 後始末でリンクの先を消さないよう、リンクだけを先に外す
    }

    [Fact]
    public void 同じドライブの中の移動は_リンクがあってもフォルダごと動かし_リンクの先は動かさない()
    {
        // 同じドライブの中の移動は名前の付け替え。OS はリンクの先をたどらず、リンクそのものを移す（今までの動作）。
        // 1 件ずつに分けると、リンクが転送元に残り、転送元のフォルダも消えず、不完全な移動が「完了」になる
        var (source, destination, outside) = (Dir("src"), Dir("dst"), Dir("outside"));
        var secret = Write(outside, "secret.txt", "secret", New);
        var pack = Path.Combine(source, "pack");
        Directory.CreateDirectory(pack);
        Write(pack, "x.txt", "source", New);
        Junction(Path.Combine(pack, "link"), outside);
        try
        {
            var plan = CopyPlanner.Build([pack], destination, CopyCondition.NewerOnly, moving: true);
            Assert.True(Assert.Single(plan.Items).WholeFolder);

            var outcome = Run(plan, moving: true);

            Assert.True(outcome.Completed);
            Assert.False(Directory.Exists(pack));                                                // 転送元のフォルダは残らない
            Assert.Equal("source", File.ReadAllText(Path.Combine(destination, "pack", "x.txt")));
            var moved = new DirectoryInfo(Path.Combine(destination, "pack", "link"));
            Assert.True(moved.Exists && moved.Attributes.HasFlag(FileAttributes.ReparsePoint));  // リンクはリンクのまま宛先へ移る
            Assert.True(File.Exists(secret));                                                    // リンクの先の中身は動かない
        }
        finally
        {
            RemoveLink(Path.Combine(pack, "link"));
            RemoveLink(Path.Combine(destination, "pack", "link"));
        }
    }

    /// <summary>ジャンクションだけを外す（再帰の削除がリンクの先を消す・失敗するのを避ける）。無ければ何もしない。</summary>
    private static void RemoveLink(string link)
    {
        try { if (Directory.Exists(link)) Directory.Delete(link); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [Fact]
    public void フォルダの確かめで中止した移動は_転送元の空のフォルダを消さない()
    {
        // 何も転送していないのに後片付けをすると、元から空だった転送元のフォルダが消える（転送 0 件の記録は残らないので、戻せない）
        var (source, destination) = (Dir("src"), Dir("dst"));
        var pack = Path.Combine(source, "pack");
        Directory.CreateDirectory(Path.Combine(pack, "sub"));                 // 元から空
        Write(pack, "x.txt", "x", New);
        Directory.CreateDirectory(Path.Combine(destination, "pack", "sub"));
        var plan = CopyPlanner.Build([pack], destination, CopyCondition.NewerOnly, moving: true);

        var outcome = Run(plan, moving: true, () => Directory.Delete(Path.Combine(destination, "pack"), recursive: true));

        Assert.False(outcome.Completed);
        Assert.Equal(Path.Combine(destination, "pack"), outcome.Changed);
        Assert.Empty(outcome.Removed);
        Assert.True(Directory.Exists(Path.Combine(pack, "sub")));
        Assert.True(File.Exists(Path.Combine(pack, "x.txt")));
        Assert.Null(outcome.Record);
    }

    // ---- ネットワークドライブでの確認（環境変数があるときだけ） ----

    private static readonly string? NetRoot = Environment.GetEnvironmentVariable("RETAC_NET_TEST_ROOT");

    /// <summary>
    /// 未設定のまま「合格」に見えないよう、スキップとして数える（CloudHydrationTests と同じやり方）。
    /// RETAC_NET_TEST_ROOT には、ネットワークドライブの上のテスト用のフォルダ（例 J:\ReTAC-Test）を入れる。
    /// <b>触るのは、その下に自分で作る GUID の名前のフォルダだけ。1 ファイル数バイト・合計 100 件以下。終わったら自分で消す。</b>
    /// </summary>
    private sealed class NetFactAttribute : FactAttribute
    {
        public NetFactAttribute()
        {
            if (string.IsNullOrEmpty(NetRoot)) Skip = "RETAC_NET_TEST_ROOT が未設定。ネットワークドライブでの確認（R-125）は未実施";
        }
    }

    /// <summary>ネットワークの側に、このテストだけのフォルダを作る。呼び出し側が finally で消す。</summary>
    private static string NetFolder()
    {
        Assert.True(Directory.Exists(NetRoot), NetRoot);
        var folder = Path.Combine(NetRoot!, "retac_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    [NetFact]
    public void ネットワークの宛先で_上書きになる差分更新が誤って中止にならない()
    {
        // 計画の時点の状態（一覧）と、転送の直前の状態の取り方が食い違うと、変わっていないのに「変わった」と判定してしまう
        var source = Dir("net-src");
        var destination = NetFolder();
        try
        {
            for (var i = 0; i < 10; i++)
            {
                Write(source, $"f{i}.txt", $"new{i}", New);
                Write(destination, $"f{i}.txt", $"old{i}", Old);
            }
            var plan = CopyPlanner.Build(Directory.GetFiles(source), destination, CopyCondition.NewerOnly);
            Assert.Equal(10, plan.Items.Count);

            var outcome = Run(plan, moving: false);

            Assert.Null(outcome.Changed);
            Assert.True(outcome.Completed);
            for (var i = 0; i < 10; i++) Assert.Equal($"new{i}", File.ReadAllText(Path.Combine(destination, $"f{i}.txt")));

            // もう一度。今度は全件が同じなので、何も転送しない
            Assert.Empty(CopyPlanner.Build(Directory.GetFiles(source), destination, CopyCondition.NewerOnly).Items);
        }
        finally { Directory.Delete(destination, recursive: true); }
    }

    [NetFact]
    public void ネットワークの宛先へ_フォルダごとコピーする()
    {
        var source = Dir("net-tree");
        var destination = NetFolder();
        try
        {
            for (var d = 0; d < 5; d++)
            {
                var folder = Path.Combine(source, $"d{d}");
                Directory.CreateDirectory(folder);
                for (var i = 0; i < 4; i++) Write(folder, $"f{i}.txt", $"{d}-{i}", New);
            }
            var plan = CopyPlanner.Build([source], destination, CopyCondition.NewerOnly);
            Assert.True(Assert.Single(plan.Items).WholeFolder);

            var outcome = Run(plan, moving: false);

            Assert.True(outcome.Completed);
            Assert.Equal(20, Directory.GetFiles(Path.Combine(destination, "net-tree"), "*", SearchOption.AllDirectories).Length);
        }
        finally { Directory.Delete(destination, recursive: true); }
    }

    [NetFact]
    public void ネットワークの宛先でも_宛先が変わっていたら中止する()
    {
        var source = Dir("net-changed");
        var destination = NetFolder();
        try
        {
            Write(source, "a.txt", "source", New);
            var target = Write(destination, "a.txt", "old", Old);
            var plan = CopyPlanner.Build([Path.Combine(source, "a.txt")], destination, CopyCondition.NewerOnly);

            var outcome = Run(plan, moving: false, () => Write(destination, "a.txt", "newer", Newer));

            Assert.False(outcome.Completed);
            Assert.Equal(target, outcome.Changed);
            Assert.Equal("newer", File.ReadAllText(target));
        }
        finally { Directory.Delete(destination, recursive: true); }
    }
}
