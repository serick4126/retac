using ReTAC.Domain.FileOps;

namespace ReTAC.Domain.Tests;

/// <summary>R-82 / R-85: 元に戻す</summary>
public class UndoTests
{
    private static readonly DateTime T = new(2026, 9, 17, 0, 0, 0, DateTimeKind.Utc);
    private static readonly ItemStamp FileStamp = new(false, 10, T);
    private static readonly ItemStamp FolderStamp = new(true, 0, T);

    private static UndoRecord Record(UndoKind kind, params UndoItem[] items) => new(kind, items, [], []);

    private static UndoProblem Check(UndoKind kind, UndoItem item, Dictionary<string, ItemStamp> files,
                                     params string[] emptyFolders) =>
        UndoCheck.Check(kind, item,
            path => files.TryGetValue(path, out var stamp) ? stamp : null,
            path => emptyFolders.Contains(path, StringComparer.OrdinalIgnoreCase));

    // ---- 履歴 ----

    [Fact]
    public void 履歴は最後に積んだものから取り出す()
    {
        var history = new UndoHistory();
        var first = Record(UndoKind.Copy, new UndoItem(@"C:\a\x", @"C:\b\x", FileStamp, false));
        var second = Record(UndoKind.Move, new UndoItem(@"C:\a\y", @"C:\b\y", FileStamp, false));
        history.Push(first);
        history.Push(second);
        Assert.Same(second, history.Peek());
        history.Pop();
        Assert.Same(first, history.Peek());
        history.Pop();
        Assert.Null(history.Peek());
        Assert.Equal(0, history.Count);
    }

    [Fact]
    public void 上限を超えたら古いものから捨てる()
    {
        var history = new UndoHistory();
        var oldest = Record(UndoKind.Copy, new UndoItem(null, @"C:\0", FileStamp, false));
        history.Push(oldest);
        for (var i = 1; i <= UndoHistory.Capacity; i++)
            history.Push(Record(UndoKind.Copy, new UndoItem(null, $@"C:\{i}", FileStamp, false)));
        Assert.Equal(UndoHistory.Capacity, history.Count);
        for (var i = 0; i < UndoHistory.Capacity; i++) { Assert.NotSame(oldest, history.Peek()); history.Pop(); }
    }

    [Fact]
    public void 空の記録は積まない()
    {
        var history = new UndoHistory();
        history.Push(new UndoRecord(UndoKind.Copy, [], [], []));
        Assert.Equal(0, history.Count);
        history.Push(new UndoRecord(UndoKind.Copy, [], [@"C:\new"], []));   // フォルダだけ作った記録は積む
        Assert.Equal(1, history.Count);
    }

    // ---- ドロップのコピーと移動（R-84 / R-93） ----

    private static readonly UndoItem Copied = new(@"D:\a\c.txt", @"C:\b\c.txt", FileStamp, false);
    private static readonly UndoItem Moved = new(@"C:\a\m.txt", @"C:\b\m.txt", FileStamp, false);

    [Fact]
    public void コピーと移動が混ざっても履歴は1件だけ増える()
    {
        var history = new UndoHistory();
        history.Push(UndoRecord.Transfer([Copied], [Moved], [@"C:\b"], []));
        Assert.Equal(1, history.Count);
        Assert.Equal("2 個の項目のコピーと移動", UndoText.Describe(history.Peek()!));
    }

    [Fact]
    public void 一度で戻す手順は移動を先にしてからコピーを戻す()
    {
        var record = UndoRecord.Transfer([Copied], [Moved], [@"C:\b"], []);
        Assert.Equal([(UndoKind.Move, Moved), (UndoKind.Copy, Copied with { Kind = UndoKind.Copy })],
                     record.Steps(record.Items).ToList());
        Assert.Equal([@"C:\b"], record.CreatedFolders);   // 作ったフォルダは記録全体に付く（戻した後、空なら消す）
    }

    [Fact]
    public void コピーの後で移動を取り消したら完了したコピーだけが1件残る()
    {
        var history = new UndoHistory();
        history.Push(UndoRecord.Transfer([Copied], [], [], []));
        Assert.Equal(1, history.Count);
        Assert.Equal(UndoKind.Copy, history.Peek()!.Kind);
        Assert.Equal("『c.txt』のコピー", UndoText.Describe(history.Peek()!));
    }

    // ---- 変化の検出 ----

    [Fact]
    public void 変わっていなければ問題なし()
    {
        var item = new UndoItem(@"C:\a\x.txt", @"C:\b\x.txt", FileStamp, false);
        Assert.Equal(UndoProblem.None, Check(UndoKind.Move, item, new() { [@"C:\b\x.txt"] = FileStamp }));
    }

    [Fact]
    public void 今の場所に無ければMissing()
    {
        var item = new UndoItem(@"C:\a\x.txt", @"C:\b\x.txt", FileStamp, false);
        Assert.Equal(UndoProblem.Missing, Check(UndoKind.Move, item, []));
    }

    [Fact]
    public void サイズか更新日時が違えばChanged()
    {
        var item = new UndoItem(@"C:\a\x.txt", @"C:\b\x.txt", FileStamp, false);
        Assert.Equal(UndoProblem.Changed, Check(UndoKind.Move, item, new() { [@"C:\b\x.txt"] = FileStamp with { Size = 11 } }));
        Assert.Equal(UndoProblem.Changed, Check(UndoKind.Move, item, new() { [@"C:\b\x.txt"] = FileStamp with { LastWriteTimeUtc = T.AddSeconds(1) } }));
    }

    [Fact]
    public void フォルダは更新日時だけを見る()
    {
        var item = new UndoItem(@"C:\a\d", @"C:\b\d", FolderStamp, false);
        Assert.Equal(UndoProblem.None, Check(UndoKind.Move, item, new() { [@"C:\b\d"] = FolderStamp with { Size = 99 } }));
    }

    [Fact]
    public void 作成したフォルダは空なら更新日時が違っても戻せる()
    {
        var item = new UndoItem(null, @"C:\b\new", FolderStamp, false);
        var later = new Dictionary<string, ItemStamp> { [@"C:\b\new"] = FolderStamp with { LastWriteTimeUtc = T.AddMinutes(1) } };
        Assert.Equal(UndoProblem.None, Check(UndoKind.Create, item, later, @"C:\b\new"));
        Assert.Equal(UndoProblem.Changed, Check(UndoKind.Create, item, later));
    }

    [Fact]
    public void 元の場所がふさがっていれば戻さない()
    {
        var item = new UndoItem(@"C:\a\x.txt", @"C:\b\x.txt", FileStamp, false);
        var files = new Dictionary<string, ItemStamp> { [@"C:\b\x.txt"] = FileStamp, [@"C:\a\x.txt"] = FileStamp };
        Assert.Equal(UndoProblem.OriginalOccupied, Check(UndoKind.Move, item, files));
        Assert.Equal(UndoProblem.OriginalOccupied, Check(UndoKind.Rename, item, files));
        Assert.Equal(UndoProblem.None, Check(UndoKind.Copy, item, files));   // コピーは元の場所を使わない
    }

    [Fact]
    public void 大文字小文字だけの改名はふさがっているとみなさない()
    {
        var item = new UndoItem(@"C:\a\x.txt", @"C:\a\X.txt", FileStamp, false);
        var files = new Dictionary<string, ItemStamp>(StringComparer.OrdinalIgnoreCase) { [@"C:\a\X.txt"] = FileStamp };
        Assert.Equal(UndoProblem.None, Check(UndoKind.Rename, item, files));
    }

    [Fact]
    public void 上書きしたコピーは戻せないが上書きした移動は戻せる()
    {
        var item = new UndoItem(@"C:\a\x.txt", @"C:\b\x.txt", FileStamp, Overwrote: true);
        var files = new Dictionary<string, ItemStamp> { [@"C:\b\x.txt"] = FileStamp };
        Assert.Equal(UndoProblem.OverwroteOnCopy, Check(UndoKind.Copy, item, files));
        Assert.Equal(UndoProblem.None, Check(UndoKind.Move, item, files));
    }

    // ---- 表示 ----

    [Fact]
    public void 記録の説明()
    {
        var one = new UndoItem(@"C:\a\a.txt", @"C:\b\a.txt", FileStamp, false);
        Assert.Equal("『a.txt』の移動", UndoText.Describe(Record(UndoKind.Move, one)));
        Assert.Equal("3 個の項目のコピー", UndoText.Describe(Record(UndoKind.Copy, one, one, one)));
        Assert.Equal("『新しいフォルダー』の作成",
            UndoText.Describe(Record(UndoKind.Create, new UndoItem(null, @"C:\b\新しいフォルダー", FolderStamp, false))));
        // 改名は元の名前で示す（利用者が覚えているのは操作前の名前）
        Assert.Equal("『a.txt』の名前の変更",
            UndoText.Describe(Record(UndoKind.Rename, new UndoItem(@"C:\a\a.txt", @"C:\a\b.txt", FileStamp, false))));
    }
}
