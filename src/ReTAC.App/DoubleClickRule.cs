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
