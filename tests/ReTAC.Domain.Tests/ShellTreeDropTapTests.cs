using ReTAC.Shell;

namespace ReTAC.Domain.Tests;

/// <summary>R-111-2 / T1: ツリーの受け口を包んで右ボタンを知る。包んだ先へはそのまま渡す</summary>
[Collection(nameof(DragButtonState))]   // 静的な状態を触るので、ほかと並べて走らせない
public class ShellTreeDropTapTests
{
    private sealed class FakeTarget : IOleDropTarget
    {
        public List<string> Calls { get; } = [];
        /// <summary>DragEnter / DragOver が返す値。負なら失敗</summary>
        public int Result { get; set; }
        public bool Throws { get; set; }
        public int DragEnter(IntPtr data, uint keyState, long point, ref uint effect) { Calls.Add($"Enter {keyState}"); return Answer(); }
        public int DragOver(uint keyState, long point, ref uint effect) { Calls.Add($"Over {keyState}"); return Answer(); }
        private int Answer() => Throws ? throw new InvalidOperationException() : Result;
        public int DragLeave() { Calls.Add("Leave"); return 0; }
        public int Drop(IntPtr data, uint keyState, long point, ref uint effect) { Calls.Add($"Drop {keyState}"); return 0; }
    }

    [Fact]
    public void 右ボタンのドラッグを覚えて包んだ先へそのまま渡す()
    {
        var inner = new FakeTarget();
        var tap = new ShellTreeDropTap(inner);
        uint effect = 7;
        tap.DragEnter(IntPtr.Zero, 2, 0, ref effect);
        tap.DragOver(2, 0, ref effect);
        Assert.True(DragButtonState.Right);
        tap.Drop(IntPtr.Zero, 0, 0, ref effect);   // 落とした時点の KeyState にボタンは残らない（T4）
        Assert.False(DragButtonState.Right);       // 落とした後は次のドラッグへ残さない
        Assert.Equal(["Enter 2", "Over 2", "Drop 0"], inner.Calls);
    }

    [Fact]
    public void 左ボタンのドラッグでは立たずDragLeaveで消える()
    {
        var tap = new ShellTreeDropTap(new FakeTarget());
        uint effect = 7;
        tap.DragEnter(IntPtr.Zero, 1, 0, ref effect);
        Assert.False(DragButtonState.Right);
        tap.DragEnter(IntPtr.Zero, 2, 0, ref effect);
        tap.DragLeave();                              // Esc で取り消したときも OLE は DragLeave を呼ぶ
        Assert.False(DragButtonState.Right);
    }

    [Fact]
    public void ReTACが右ボタンで始めたドラッグは受け口の通知が無くても右()
    {
        DragButtonState.SourceRight = true;
        try { Assert.True(DragButtonState.Right); }
        finally { DragButtonState.SourceRight = false; }
        Assert.False(DragButtonState.Right);
    }

    [Fact]
    public void 包む相手が無ければfalseを返す()
    {
        Assert.False(ShellTreeDropTap.Install(IntPtr.Zero));
    }

    [Fact]
    public void 包めたら包んだものが登録される()
    {
        var registered = new List<IOleDropTarget>();
        var result = ShellTreeDropTap.Replace((IntPtr)1, new FakeTarget(), _ => 0, (_, t) => { registered.Add(t); return 0; });
        Assert.Equal(ShellTreeDropTap.TapResult.Wrapped, result);
        Assert.IsType<ShellTreeDropTap>(Assert.Single(registered));
    }

    [Fact]
    public void 外せなければ何も登録しない()
    {
        var registered = 0;
        var result = ShellTreeDropTap.Replace((IntPtr)1, new FakeTarget(), _ => -1, (_, _) => { registered++; return 0; });
        Assert.Equal(ShellTreeDropTap.TapResult.NotWrapped, result);
        Assert.Equal(0, registered);
    }

    [Fact]
    public void 包んだものを登録できなければ元の受け口を登録し直す()
    {
        var original = new FakeTarget();
        var registered = new List<IOleDropTarget>();
        var result = ShellTreeDropTap.Replace((IntPtr)1, original, _ => 0,
            (_, t) => { registered.Add(t); return t is ShellTreeDropTap ? -1 : 0; });
        Assert.Equal(ShellTreeDropTap.TapResult.NotWrapped, result);
        Assert.Same(original, registered[^1]);   // 元へ戻す（ドロップそのものは受けられる）
    }

    [Fact]
    public void 元も戻せなければLostを返す()
    {
        var result = ShellTreeDropTap.Replace((IntPtr)1, new FakeTarget(), _ => 0, (_, _) => -1);
        Assert.Equal(ShellTreeDropTap.TapResult.Lost, result);
    }

    [Fact]
    public void 包んだ先のDragEnterが失敗したら右ボタンの印を消す()
    {
        var inner = new FakeTarget { Result = unchecked((int)0x80004005) };   // E_FAIL
        var tap = new ShellTreeDropTap(inner);
        uint effect = 7;
        Assert.Equal(unchecked((int)0x80004005), tap.DragEnter(IntPtr.Zero, 2, 0, ref effect));
        Assert.False(DragButtonState.Right);   // 失敗の後に DragLeave が来なくても残さない
        inner.Result = 0;
        tap.DragEnter(IntPtr.Zero, 1, 0, ref effect);   // 続く左ボタンのドラッグ
        Assert.False(DragButtonState.Right);
    }

    [Fact]
    public void 包んだ先のDragOverが失敗したら右ボタンの印を消す()
    {
        var inner = new FakeTarget();
        var tap = new ShellTreeDropTap(inner);
        uint effect = 7;
        tap.DragEnter(IntPtr.Zero, 2, 0, ref effect);
        inner.Result = unchecked((int)0x80004005);
        tap.DragOver(2, 0, ref effect);
        Assert.False(DragButtonState.Right);
        inner.Result = 0;
        tap.DragEnter(IntPtr.Zero, 1, 0, ref effect);
        Assert.False(DragButtonState.Right);
    }

    [Fact]
    public void 包んだ先が例外を投げたら右ボタンの印を消して例外はそのまま返す()
    {
        var inner = new FakeTarget { Throws = true };
        var tap = new ShellTreeDropTap(inner);
        uint effect = 7;
        Assert.Throws<InvalidOperationException>(() => tap.DragEnter(IntPtr.Zero, 2, 0, ref effect));
        Assert.False(DragButtonState.Right);
        inner.Throws = false;
        tap.DragEnter(IntPtr.Zero, 2, 0, ref effect);
        inner.Throws = true;
        Assert.Throws<InvalidOperationException>(() => tap.DragOver(2, 0, ref effect));
        Assert.False(DragButtonState.Right);
    }

    [Fact]
    public void 受け口を取り出せなければnullにして例外を外へ出さない()
    {
        Assert.Null(ShellTreeDropTap.Resolve(IntPtr.Zero, _ => throw new InvalidOperationException("呼ばれない")));
        Assert.Null(ShellTreeDropTap.Resolve((IntPtr)1, _ => throw new System.Runtime.InteropServices.COMException()));
        Assert.Null(ShellTreeDropTap.Resolve((IntPtr)1, _ => new object()));   // 受け口ではないもの
        var target = new FakeTarget();
        Assert.Same(target, ShellTreeDropTap.Resolve((IntPtr)1, _ => target));
    }
}
