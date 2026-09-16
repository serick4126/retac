namespace ReTAC.Domain.Tools;

public enum QueueItemState
{
    Waiting,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

/// <param name="Killed">利用者が「この 1 件を強制終了する」で終わらせた</param>
public sealed record RunOutcome(int ExitCode, bool Killed = false);

/// <param name="Error">起動できなかったときの理由</param>
public sealed record QueueEntry(LaunchRequest Request, QueueItemState State, int? ExitCode = null, bool Killed = false, string? Error = null);

/// <summary>
/// 外部ツールキュー（F-05）。「マークした項目ごとに起動する」を、前の 1 件の終了を待ってから次を起動する形で処理する。
/// <b>完全に独立した仕組み</b>で、ReTAC 内部の処理や将来の git 統合の処理は使わない（予定 §7 C-2）。
/// 起動と終了待ちは <c>run</c> に任せる（App がプロセスを扱う）。このクラスはプロセスを知らない。
/// 待つのは裏のスレッドで、ReTAC の操作は止めない（R-23）。
/// </summary>
public sealed class ExternalToolQueue(Func<LaunchRequest, Task<RunOutcome>> run)
{
    private readonly object _gate = new();
    private List<QueueEntry> _entries = [];
    private Task _pump = Task.CompletedTask;
    private bool _pumping;

    /// <summary>状態が変わった。<b>裏のスレッドから呼ばれる</b>ので、受け側で UI スレッドへ移すこと（S-07）。</summary>
    public event Action? Changed;

    public IReadOnlyList<QueueEntry> Snapshot()
    {
        lock (_gate) return [.. _entries];
    }

    /// <summary>待っている件数（動いている 1 件を含まない）。ReTAC の終了時の確認に使う（K-5）。</summary>
    public int WaitingCount
    {
        get { lock (_gate) return _entries.Count(e => e.State == QueueItemState.Waiting); }
    }

    /// <summary>今の処理が全部終わったら完了する Task。テストで使う。</summary>
    public Task WhenIdle()
    {
        lock (_gate) return _pump;
    }

    public void Enqueue(IEnumerable<LaunchRequest> requests)
    {
        lock (_gate)
        {
            // 前のまとまりが終わっていれば、その結果は捨てて数え直す（ステータスバーの「3/50」が今回の分を指すように）
            if (!_entries.Any(IsActive)) _entries = [];
            _entries.AddRange(requests.Select(r => new QueueEntry(r, QueueItemState.Waiting)));

            if (!_pumping)
            {
                _pumping = true;
                _pump = Task.Run(PumpAsync);
            }
        }
        Notify();
    }

    /// <summary>K-2:「停止」。待っている分を取りやめる。動いている 1 件はそのまま最後まで走らせる。</summary>
    public void CancelWaiting()
    {
        lock (_gate)
        {
            for (var i = 0; i < _entries.Count; i++)
                if (_entries[i].State == QueueItemState.Waiting)
                    _entries[i] = _entries[i] with { State = QueueItemState.Cancelled };
        }
        Notify();
    }

    /// <summary>ステータスバーの文言（F-05）。処理中は「ツール名 3/50」、終わったら結果を残す。空なら出さない。</summary>
    public string StatusText()
    {
        lock (_gate)
        {
            if (_entries.Count == 0) return "";

            var total = _entries.Count;
            var running = _entries.FindIndex(e => e.State == QueueItemState.Running);
            if (running >= 0) return $"{_entries[running].Request.Tool.Name} {running + 1}/{total}";

            var cancelled = _entries.Count(e => e.State == QueueItemState.Cancelled);
            var failed = _entries.Count(e => e.State == QueueItemState.Failed);
            var name = _entries[^1].Request.Tool.Name;
            if (_entries.Any(e => e.State == QueueItemState.Waiting)) return $"{name} 0/{total}";

            var text = $"{name} 完了 {total - cancelled} 件";
            if (failed > 0) text += $"（失敗 {failed}）";
            if (cancelled > 0) text += $"（取りやめ {cancelled}）";
            return text;
        }
    }

    /// <summary>タスクバーの進行バー（F-05）。処理中でなければ null。</summary>
    public (int Done, int Total)? Progress()
    {
        lock (_gate)
        {
            if (!_entries.Any(IsActive)) return null;
            return (_entries.Count(e => !IsActive(e)), _entries.Count);
        }
    }

    private static bool IsActive(QueueEntry entry) =>
        entry.State is QueueItemState.Waiting or QueueItemState.Running;

    /// <summary>
    /// m2: 購読側（UI）が例外を投げても、キューを回すループはそれで止めてはならない。
    /// 通知は補助的な機能なので、ここで握りつぶして次へ進む。
    /// </summary>
    private void Notify()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception)
        {
        }
    }

    private async Task PumpAsync()
    {
        while (true)
        {
            int index;
            LaunchRequest request;
            lock (_gate)
            {
                index = _entries.FindIndex(e => e.State == QueueItemState.Waiting);
                if (index < 0)
                {
                    _pumping = false;
                    return;
                }
                request = _entries[index].Request;
                _entries[index] = _entries[index] with { State = QueueItemState.Running };
            }
            Notify();

            QueueEntry finished;
            try
            {
                var outcome = await run(request).ConfigureAwait(false);
                var succeeded = outcome.ExitCode == 0 && !outcome.Killed;
                finished = new QueueEntry(request, succeeded ? QueueItemState.Succeeded : QueueItemState.Failed,
                    outcome.ExitCode, outcome.Killed);
            }
            catch (Exception ex)
            {
                // K-3: 1 件が起動できなくても続ける。何が起きても次へ進めるため、例外の型は絞らない
                finished = new QueueEntry(request, QueueItemState.Failed, Error: ex.Message);
            }

            // 動いている間は数え直さない（Enqueue は処理中なら足すだけ）ので、添字は変わっていない
            lock (_gate) _entries[index] = finished;
            Notify();
        }
    }
}
