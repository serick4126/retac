using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ReTAC.Updater.Core;

/// <summary>終了を頼んだ ReTAC の、今の様子。</summary>
public enum PendingQuitState
{
    /// <summary>プロセスが終わった。</summary>
    Exited,

    /// <summary>ReTAC が「終わる途中」と答えた（K-5 の確認を出している間を含む）。</summary>
    Quitting,

    /// <summary>ReTAC が「受け付けていない」と答えた（断った・K-5 の確認で取りやめた）。</summary>
    NotQuitting,

    /// <summary>答えが無い（ウィンドウを閉じていく途中・応答しない）。</summary>
    Unknown,
}

/// <summary>見届けの終わり方。</summary>
public enum PendingQuitEnd
{
    /// <summary>頼んだ ReTAC がすべて終わった。</summary>
    AllExited,

    /// <summary>少なくとも 1 つが「受け付けていない」と答えて残った（取りやめた）。</summary>
    SomeStayed,

    /// <summary>答えが無い状態が上限まで続いた（応答しない ReTAC がある）。見届けを打ち切る。</summary>
    GaveUp,
}

/// <summary>
/// R-109-3 / R-109-6: 終了依頼を受け付けた ReTAC が K-5 の確認を出している間にアップデータを閉じても、利用者が後から
/// 「終了」を選べば ReTAC は終わる。そのとき ReTAC を使えない状態のまま残さないよう、アップデータは画面を閉じた後も
/// 頼んだ ReTAC を見届け、終わったら起動し直す。
/// 取りやめたかどうかは ReTAC に問い合わせて決める（ウィンドウの様子や時間からは決めない）。
/// ReTAC が答えている限り（K-5 の確認の間は「終わる途中」と答える）見届けを続ける。
/// 答えが無い状態だけが続くときは、上限（INV-UPDATER-NO-DEADLOCK）で打ち切る。
/// </summary>
public static class PendingQuitWatch
{
    /// <summary>
    /// 答えが無い状態が続いてよい時間の既定。<b>実際に経った時間で数える</b>（応答しない ReTAC への問い合わせは 1 回に最大 5 秒かかるので、
    /// 回数で数えると実際の待ちが何倍にも延びる）。
    /// </summary>
    public static readonly TimeSpan DefaultUnknownLimit = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 送っている最中の依頼の返事を先に待ってから、<see cref="Wait"/> で見届ける。
    /// 問い合わせが依頼を追い越すと、受け付ける前の ReTAC に「受け付けていない」と答えられて見届けをやめてしまうため。
    /// </summary>
    public static async Task<PendingQuitEnd> FollowAsync(IEnumerable<Task> inFlight, int count, Func<int, PendingQuitState> probeOne, Action pause)
    {
        foreach (var sending in inFlight)
        {
            try { await sending.ConfigureAwait(false); }
            catch (Exception) { }   // 送れなかった依頼も、様子の問い合わせで決める
        }
        return await Task.Run(() => Wait(count, probeOne, pause)).ConfigureAwait(false);
    }

    /// <summary>
    /// 頼んだ ReTAC を 1 つずつ調べる形の見届け。<b>1 巡の問い合わせは ReTAC ごとに別のスレッドで同時に行う。</b>
    /// 応答しない ReTAC への問い合わせは 1 件に最大 5 秒かかるので、順番に行うと ReTAC の数だけ 1 巡が延び、
    /// 上限（実際に経った時間）を確かめる前に何十秒も過ぎてしまうため。何個あっても 1 巡は 1 件分の時間で済む。
    /// 決まった ReTAC（終わった・取りやめた）には、もう問い合わせない。
    /// スレッドプールではなく専用のスレッドにする（固まった問い合わせでプールが埋まると、スレッドの追加が遅れて待ちが延びる）。
    /// </summary>
    /// <param name="probeOne">i 番目の ReTAC の様子を調べる</param>
    public static PendingQuitEnd Wait(int count, Func<int, PendingQuitState> probeOne, Action pause,
                                      TimeSpan? unknownLimit = null, Func<TimeSpan>? clock = null)
    {
        var states = Enumerable.Repeat(PendingQuitState.Unknown, count).ToArray();
        return Wait(() =>
        {
            var threads = Enumerable.Range(0, count)
                .Where(i => states[i] is PendingQuitState.Quitting or PendingQuitState.Unknown)
                .Select(i => new System.Threading.Thread(() => states[i] = probeOne(i)) { IsBackground = true })
                .ToList();
            foreach (var thread in threads) thread.Start();
            foreach (var thread in threads) thread.Join();
            return states.ToArray();
        }, pause, unknownLimit, clock);
    }

    /// <param name="probe">頼んだ ReTAC それぞれの様子を調べる（応答しない ReTAC には 1 回に数秒かかりうる）</param>
    /// <param name="pause">次に調べるまで待つ</param>
    /// <param name="unknownLimit">まだ決まっていない ReTAC がどれも答えない状態が、実際にこの時間続いたら打ち切る。既定は 60 秒</param>
    /// <param name="clock">経過時間を測る単調な時計。既定は Stopwatch（テストから差し替える）</param>
    public static PendingQuitEnd Wait(Func<IReadOnlyList<PendingQuitState>> probe, Action pause,
                                      TimeSpan? unknownLimit = null, Func<TimeSpan>? clock = null)
    {
        var limit = unknownLimit ?? DefaultUnknownLimit;
        if (clock is null)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            clock = () => stopwatch.Elapsed;
        }

        TimeSpan? silentSince = null;
        while (true)
        {
            var asked = clock();
            var states = probe();
            var pending = states.Where(s => s is PendingQuitState.Quitting or PendingQuitState.Unknown).ToList();
            if (pending.Count == 0)
                return states.All(s => s == PendingQuitState.Exited) ? PendingQuitEnd.AllExited : PendingQuitEnd.SomeStayed;

            // 答えている ReTAC が 1 つでもあれば（K-5 の確認の間など）、上限は数えない。答えが無くなった時刻から数える
            if (pending.All(s => s == PendingQuitState.Unknown)) silentSince ??= asked;
            else silentSince = null;

            // 問い合わせにかかった時間も含めて、実際に経った時間で打ち切る
            if (silentSince is { } since && clock() - since >= limit) return PendingQuitEnd.GaveUp;
            pause();
        }
    }

    /// <summary>
    /// 見届けの後に ReTAC を起動し直すか。<b>終了を頼んだ ReTAC だけ</b>で決める（頼んでいない ReTAC が自分で終わっても起動しない）。
    /// 頼んだものが 1 つでも終わっていれば起動する。打ち切ったとき（応答しない ReTAC がある）も、頼んだものがあれば起動する
    /// （時間切れの依頼も後から届いて終わりうる。そのとき使える ReTAC を残すため。固まったまま残っても、ReTAC は複数起動できる）。
    /// </summary>
    public static bool ShouldRelaunch(PendingQuitEnd end, IEnumerable<(bool Requested, bool Exited)> targets)
    {
        var requested = targets.Where(t => t.Requested).ToList();
        if (requested.Count == 0) return false;
        return end == PendingQuitEnd.GaveUp || requested.Any(t => t.Exited);
    }
}
