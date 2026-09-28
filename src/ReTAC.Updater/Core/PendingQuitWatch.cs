using System;
using System.Collections.Generic;
using System.Linq;

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

    /// <summary>答えが無い（ウィンドウを閉じていく途中・応答しない）。まだ終わる途中かもしれないので、見届けを続ける。</summary>
    Unknown,
}

/// <summary>見届けの終わり方。</summary>
public enum PendingQuitEnd
{
    /// <summary>頼んだ ReTAC がすべて終わった。</summary>
    AllExited,

    /// <summary>少なくとも 1 つが「受け付けていない」と答えて残った（取りやめた）。</summary>
    SomeStayed,
}

/// <summary>
/// R-109-3 / R-109-6: 終了依頼を受け付けた ReTAC が K-5 の確認を出している間にアップデータを閉じても、利用者が後から
/// 「終了」を選べば ReTAC は終わる。そのとき ReTAC を使えない状態のまま残さないよう、アップデータは画面を閉じた後も
/// 頼んだ ReTAC を見届け、終わったら起動し直す。
/// <b>取りやめたかどうかは ReTAC に問い合わせて決める。</b>ウィンドウの様子や時間からは決めない
/// （終了を選んだ後の設定の保存が長くかかると、操作できるように見える間が続きうるため）。時間の上限も設けない。
/// </summary>
public static class PendingQuitWatch
{
    /// <summary>
    /// 送っている最中の依頼の返事を先に待ってから、<see cref="Wait"/> で見届ける。
    /// 問い合わせが依頼を追い越すと、受け付ける前の ReTAC に「受け付けていない」と答えられて見届けをやめてしまうため。
    /// </summary>
    public static async System.Threading.Tasks.Task<PendingQuitEnd> FollowAsync(
        IEnumerable<System.Threading.Tasks.Task> inFlight, Func<IReadOnlyList<PendingQuitState>> probe, Action pause)
    {
        foreach (var sending in inFlight)
        {
            try { await sending.ConfigureAwait(false); }
            catch (Exception) { }   // 送れなかった依頼も、様子の問い合わせで決める
        }
        return await System.Threading.Tasks.Task.Run(() => Wait(probe, pause)).ConfigureAwait(false);
    }

    /// <param name="probe">頼んだ ReTAC それぞれの様子を調べる</param>
    /// <param name="pause">次に調べるまで待つ</param>
    public static PendingQuitEnd Wait(Func<IReadOnlyList<PendingQuitState>> probe, Action pause)
    {
        while (true)
        {
            var states = probe();
            var settled = states.All(s => s is PendingQuitState.Exited or PendingQuitState.NotQuitting);
            if (settled)
                return states.All(s => s == PendingQuitState.Exited) ? PendingQuitEnd.AllExited : PendingQuitEnd.SomeStayed;
            pause();
        }
    }
}
