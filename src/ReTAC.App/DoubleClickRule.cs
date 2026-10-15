using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace ReTAC.App;

/// <summary>
/// R-91-2: ブックマークのフォルダのダブルクリック判定。バーのボタンは 2 回目の押下がメニューを閉じるのに使われて
/// 標準の DoubleClick が届かず、ホバーで開くメニュー項目も同様に信頼できないため、押下の時刻・位置から自前で判定する。
/// </summary>
internal static class DoubleClickRule
{
    /// <summary>
    /// 直前のクリックから見て 2 回目のクリックと言えるか。DoubleClickSize は 1 回目の位置を<b>中心</b>とする矩形の
    /// 幅・高さなので、それぞれ半分ずつで比べる（|dx| &lt;= Width / 2 かつ |dy| &lt;= Height / 2。丸ごとで比べると許容範囲が約 2 倍になる）。
    /// </summary>
    public static bool IsSecondClick(DateTime prevTime, Point prevPos, DateTime nowTime, Point nowPos,
        MouseButtons button, TimeSpan doubleClickTime, Size doubleClickSize)
    {
        if (button != MouseButtons.Left) return false;
        if (nowTime - prevTime > doubleClickTime) return false;
        if (Math.Abs(nowPos.X - prevPos.X) > doubleClickSize.Width / 2) return false;
        if (Math.Abs(nowPos.Y - prevPos.Y) > doubleClickSize.Height / 2) return false;
        return true;
    }
}

/// <summary>
/// 1 回目のクリックの時刻・位置を覚え、次の左ボタンの押下で判定する小さな状態。判定に使ったら
/// （true でも false でも）情報を捨てる。ドラッグの開始など、クリックとして成立しなかったときは <see cref="Forget"/> で捨てる。
/// </summary>
internal sealed class DoubleClickTracker
{
    private (DateTime Time, Point Pos)? _pending;

    public void Remember(DateTime time, Point pos) => _pending = (time, pos);

    /// <summary>古い情報を次のクリックに使い回さない（ドラッグの開始・キャンセルなど）。</summary>
    public void Forget() => _pending = null;

    /// <summary>
    /// 覚えている情報と比べて 2 回目のクリックか判定する。結果によらず情報は捨てる
    /// （3 回目を続けて押しても、2 回目との判定を使い回してダブルクリックにはしない）。
    /// </summary>
    public bool Decide(DateTime time, Point pos, MouseButtons button, TimeSpan doubleClickTime, Size doubleClickSize)
    {
        if (_pending is not { } prev) return false;
        _pending = null;
        return DoubleClickRule.IsSecondClick(prev.Time, prev.Pos, time, pos, button, doubleClickTime, doubleClickSize);
    }
}

/// <summary>
/// R-91-2: バー直下のボタン（BarDropDownButton）のダブルクリックの状態。記録を 2 つに分ける。
/// 1 つにまとめると、外側のクリックや Esc で閉じた直後の普通のクリックが、展開したときの記録と比べられてジャンプになる。
/// <list type="bullet">
/// <item>展開の記録: 閉じた状態で押して、離して展開した。展開している間の押下とだけ比べる（閉じた状態からのダブルクリック）。</item>
/// <item>閉じた押下の記録: 展開している間にこのボタンを押して閉じた。閉じた状態の押下とだけ比べる（展開した状態からのダブルクリック）。</item>
/// </list>
/// 閉じたら閉じ方を問わず両方捨て、自分の押下で閉じたときだけ、閉じた後に閉じた押下を覚え直す。
/// </summary>
internal sealed class BarDoubleClickState
{
    private readonly DoubleClickTracker _opened = new();
    private readonly DoubleClickTracker _closedByPress = new();

    /// <summary>閉じた状態の押下。true ならダブルクリック（展開せずにジャンプする）。</summary>
    public bool PressWhileClosed(DateTime time, Point pos, MouseButtons button, TimeSpan doubleClickTime, Size doubleClickSize) =>
        _closedByPress.Decide(time, pos, button, doubleClickTime, doubleClickSize);

    /// <summary>離して展開した。</summary>
    public void OpenedByRelease(DateTime time, Point pos) => _opened.Remember(time, pos);

    /// <summary>
    /// 展開している間の押下。true ならダブルクリック（呼び出し側が閉じてジャンプする。close は呼ばない）。
    /// false なら close で閉じる。close は標準の処理で閉じ、閉じたかどうかを返す（その中で <see cref="DropDownClosed"/> が
    /// 来て両方の記録が捨てられる）。閉じた後で覚えるので、この押下の記録だけが残る。ジャンプを繋いでいない
    /// ボタン（グループ）は覚えない。覚えると、次の押下が何も起きない押下になる。
    /// </summary>
    public bool PressWhileOpen(DateTime time, Point pos, MouseButtons button, TimeSpan doubleClickTime, Size doubleClickSize,
        bool jumpWired, Func<bool> close)
    {
        if (_opened.Decide(time, pos, button, doubleClickTime, doubleClickSize)) return true;
        if (close() && jumpWired) _closedByPress.Remember(time, pos);
        return false;
    }

    /// <summary>展開した（マウス・キーボードを問わない）。</summary>
    public void DropDownOpened() => _closedByPress.Forget();

    /// <summary>閉じた（外側のクリック・Esc・自分の押下など、閉じ方を問わない）。</summary>
    public void DropDownClosed()
    {
        _opened.Forget();
        _closedByPress.Forget();
    }

    /// <summary>ボタンから外れた。別の場所をクリックして戻ってきた押下をダブルクリックにしない。</summary>
    public void MouseLeft() => _closedByPress.Forget();

    /// <summary>ドラッグを始めた。クリックとして成立しなかったので、どちらの記録も使わせない。</summary>
    public void Cancel() => DropDownClosed();
}

/// <summary>
/// ホバーで開くメニュー項目の DoubleClickTracker を項目に結び付け、ドラッグの開始（BookmarkDropZone.EnableDrag）から
/// 項目の種類を気にせず捨てられるようにする対応表。BarDropDownButton は自分の CancelOpen で自分のトラッカーを直接
/// 捨てるので登録しない（Forget を呼んでも登録が無ければ何もしない）。
/// </summary>
internal static class DoubleClickTrackers
{
    private static readonly ConditionalWeakTable<ToolStripItem, DoubleClickTracker> s_map = new();

    public static void Register(ToolStripItem item, DoubleClickTracker tracker) => s_map.Add(item, tracker);

    /// <summary>ドラッグの開始など、クリックとして成立しなかったときに呼ぶ。登録が無ければ何もしない。</summary>
    public static void Forget(ToolStripItem item)
    {
        if (s_map.TryGetValue(item, out var tracker)) tracker.Forget();
    }
}
