using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

/// <summary>F-05: 外部ツールキュー</summary>
public class ExternalToolQueueTests
{
    private static readonly ExternalTool Tool = new() { Id = 9, Name = "変換", Path = "ffmpeg.exe" };

    private static LaunchRequest Request(string label) => new(Tool, [label], @"C:\work", label);

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task 前の1件が終わってから次を起動する()
    {
        var active = 0;
        var maxActive = 0;
        var order = new List<string>();
        var queue = new ExternalToolQueue(async request =>
        {
            lock (order)
            {
                order.Add(request.TargetLabel);
                maxActive = Math.Max(maxActive, Interlocked.Increment(ref active));
            }
            await Task.Delay(20);
            Interlocked.Decrement(ref active);
            return new RunOutcome(0);
        });

        queue.Enqueue([Request("a"), Request("b"), Request("c")]);
        await queue.WhenIdle();

        Assert.Equal(["a", "b", "c"], order);
        Assert.Equal(1, maxActive);
        Assert.All(queue.Snapshot(), e => Assert.Equal(QueueItemState.Succeeded, e.State));
    }

    [Fact]
    public async Task 失敗しても続ける()
    {
        var queue = new ExternalToolQueue(request => request.TargetLabel switch
        {
            "b" => Task.FromResult(new RunOutcome(1)),
            "c" => throw new InvalidOperationException("起動できませんでした"),
            _ => Task.FromResult(new RunOutcome(0)),
        });

        queue.Enqueue([Request("a"), Request("b"), Request("c"), Request("d")]);
        await queue.WhenIdle();

        var entries = queue.Snapshot();
        Assert.Equal(
            [QueueItemState.Succeeded, QueueItemState.Failed, QueueItemState.Failed, QueueItemState.Succeeded],
            entries.Select(e => e.State));
        Assert.Equal(1, entries[1].ExitCode);
        Assert.Equal("起動できませんでした", entries[2].Error);
        Assert.Equal("変換 完了 4 件（失敗 2）", queue.StatusText());
    }

    [Fact]
    public async Task 強制終了は失敗として数える()
    {
        var queue = new ExternalToolQueue(_ => Task.FromResult(new RunOutcome(-1, Killed: true)));
        queue.Enqueue([Request("a")]);
        await queue.WhenIdle();

        var entry = Assert.Single(queue.Snapshot());
        Assert.Equal(QueueItemState.Failed, entry.State);
        Assert.True(entry.Killed);
    }

    [Fact]
    public async Task 停止は待っている分だけを取りやめる()
    {
        var started = Signal();
        var release = Signal();
        var queue = new ExternalToolQueue(async _ =>
        {
            started.TrySetResult();
            await release.Task;
            return new RunOutcome(0);
        });

        queue.Enqueue([Request("a"), Request("b"), Request("c")]);
        await started.Task;
        Assert.Equal("変換 1/3", queue.StatusText());
        Assert.Equal((0, 3), queue.Progress()!.Value);
        Assert.Equal(2, queue.WaitingCount);

        queue.CancelWaiting();
        release.SetResult();
        await queue.WhenIdle();

        Assert.Equal([QueueItemState.Succeeded, QueueItemState.Cancelled, QueueItemState.Cancelled],
            queue.Snapshot().Select(e => e.State));
        Assert.Equal("変換 完了 1 件（取りやめ 2）", queue.StatusText());
        Assert.Null(queue.Progress());
    }

    [Fact]
    public async Task 処理中に足した分は後ろに並ぶ()
    {
        var started = Signal();
        var release = Signal();
        var order = new List<string>();
        var queue = new ExternalToolQueue(async request =>
        {
            lock (order) order.Add(request.TargetLabel);
            started.TrySetResult();
            await release.Task;
            return new RunOutcome(0);
        });

        queue.Enqueue([Request("a")]);
        await started.Task;
        queue.Enqueue([Request("b")]);
        release.SetResult();
        await queue.WhenIdle();

        Assert.Equal(["a", "b"], order);
        Assert.Equal(2, queue.Snapshot().Count);
    }

    [Fact]
    public async Task 終わった後に足すと数え直す()
    {
        var queue = new ExternalToolQueue(_ => Task.FromResult(new RunOutcome(0)));
        queue.Enqueue([Request("a"), Request("b")]);
        await queue.WhenIdle();

        queue.Enqueue([Request("c")]);
        await queue.WhenIdle();

        Assert.Equal("c", Assert.Single(queue.Snapshot()).Request.TargetLabel);
        Assert.Equal("変換 完了 1 件", queue.StatusText());
    }

    [Fact]
    public void 空なら何も出さない()
    {
        var queue = new ExternalToolQueue(_ => Task.FromResult(new RunOutcome(0)));
        Assert.Equal("", queue.StatusText());
        Assert.Null(queue.Progress());
    }

    [Fact]
    public async Task 通知の購読先が例外を投げても処理を続ける()
    {
        var queue = new ExternalToolQueue(_ => Task.FromResult(new RunOutcome(0)));
        queue.Changed += () => throw new InvalidOperationException("購読側の例外");

        queue.Enqueue([Request("a"), Request("b"), Request("c")]);
        await queue.WhenIdle();

        Assert.Equal(3, queue.Snapshot().Count);
        Assert.All(queue.Snapshot(), e => Assert.Equal(QueueItemState.Succeeded, e.State));
    }
}
