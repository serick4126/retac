using System;
using System.Collections.Generic;
using System.Linq;

namespace ReTAC.Updater.Core;

/// <summary>終了依頼を受け付けた ReTAC の、今の様子。</summary>
public readonly struct PendingQuitState
{
    public PendingQuitState(bool alive, bool idle)
    {
        Alive = alive;
        Idle = idle;
    }

    /// <summary>プロセスが生きている。</summary>
    public bool Alive { get; }

    /// <summary>生きていて、ウィンドウが操作できる（K-5 の確認などのダイアログを出していない）。</summary>
    public bool Idle { get; }
}

/// <summary>見張りの終わり方。</summary>
public enum PendingQuitEnd
{
    /// <summary>受け付けた ReTAC がすべて終わった。ReTAC を起動し直す。</summary>
    AllExited,

    /// <summary>ダイアログが閉じた後も生きている（K-5 の確認で取りやめた）。起動し直さない。</summary>
    StayedOpen,
}

/// <summary>
/// R-109-3 / R-109-6: 終了依頼を受け付けた ReTAC が K-5 の確認を出している間にアップデータを閉じても、
/// 利用者が後から「終了」を選べば ReTAC は終わる。そのとき ReTAC を使えない状態のまま残さないよう、
/// アップデータは画面を閉じた後も受け付けた ReTAC を見張り、終わったら起動し直す。
/// ダイアログが閉じた後も生きている状態が続けば（取りやめた）、見張りをやめる。
/// </summary>
public static class PendingQuitWatch
{
    /// <param name="probe">受け付けた ReTAC それぞれの様子を調べる</param>
    /// <param name="pause">次に調べるまで待つ</param>
    /// <param name="idleChecks">生きていて操作できる状態がこの回数続いたら、取りやめたとみなす
    /// （受け付けた直後の、ダイアログを出さずに閉じていく間を取りやめと見誤らないよう、少し待つ）</param>
    public static PendingQuitEnd Wait(Func<IReadOnlyList<PendingQuitState>> probe, Action pause, int idleChecks = 20)
    {
        var idle = 0;
        while (true)
        {
            var states = probe();
            if (states.All(s => !s.Alive)) return PendingQuitEnd.AllExited;
            idle = states.Any(s => s.Alive && s.Idle) ? idle + 1 : 0;
            if (idle >= idleChecks) return PendingQuitEnd.StayedOpen;
            pause();
        }
    }
}
