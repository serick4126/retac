using System.ComponentModel;
using System.Diagnostics;
using ReTAC.Domain.Tools;

namespace ReTAC.App;

/// <summary>
/// プロセス全体で 1 本の外部ツールキュー（F-05 / K-4）と、動いている 1 件のプロセス。
/// 複数のウィンドウがあっても、キューも進行状況のウィンドウも 1 つ。
/// </summary>
internal static class ToolQueueHost
{
    private static readonly object Gate = new();
    private static Process? _running;
    private static LaunchRequest? _runningRequest;
    private static bool _killRequested;

    public static ExternalToolQueue Queue { get; } = new(RunAsync);

    private static async Task<RunOutcome> RunAsync(LaunchRequest request)
    {
        Process? process;
        try
        {
            process = Process.Start(ToolProcess.StartInfo(request));
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            // V-03: 落とさない。キューはこの 1 件を失敗として次へ進む（K-3）
            throw new InvalidOperationException($"{request.Tool.Path} を起動できませんでした。{ex.Message}", ex);
        }

        // 既に動いているアプリへ処理を渡して終わるもの（DDE など）はプロセスが返らない。終わったものとして進む
        if (process is null) return new RunOutcome(0);

        lock (Gate)
        {
            _running = process;
            _runningRequest = request;
            _killRequested = false;
        }
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            lock (Gate) return new RunOutcome(ExitCodeOf(process), _killRequested);
        }
        finally
        {
            lock (Gate) { _running = null; _runningRequest = null; }
            process.Dispose();
        }
    }

    private static int ExitCodeOf(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return -1;
        }
    }

    /// <summary>
    /// 予定 §1.4 A-3:「この 1 件を強制終了する」。ツールが起動した子プロセスも含めて終了する
    /// （`pwsh -File` の中で動く処理なども止める）。
    /// レビュー f1: 確認ダイアログが開いている間に次の 1 件へ進んでいることがある。呼び出し側が確認した
    /// <paramref name="request"/> が今もまさに動いているときだけ終了する（差し替わっていたら何もしない）。
    /// </summary>
    /// <returns>終了させられなかったときの理由。終了させた・動いていない・確認した対象がもう動いていないなら null</returns>
    public static string? KillRunning(LaunchRequest request)
    {
        Process? process;
        lock (Gate)
        {
            if (_running is null || !ReferenceEquals(_runningRequest, request)) return null;
            process = _running;
        }

        // 子プロセスをたどって終わらせるので時間がかかりうる。ロックの外で行う
        try
        {
            // m1: 確認の間に自然に終わっていた。強制終了として数えない。
            // M-3: HasExited も try の中に置く。確認の間に RunAsync の finally が process を
            // 既に Dispose していると ObjectDisposedException になりうる
            if (process.HasExited) return null;

            lock (Gate) _killRequested = true;
            process.Kill(entireProcessTree: true);
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or AggregateException)
        {
            // ツールが自分で管理者権限に昇格していると、通常の権限の ReTAC からは終了できない
            lock (Gate) _killRequested = false;
            return ex.Message;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // 押す間に自然に終わっていた、または RunAsync の finally が既に Dispose していた。
            // どちらも強制終了として数えない。次の 1 件の KillRunning まで _killRequested を残さない
            lock (Gate) _killRequested = false;
            return null;
        }
    }
}
