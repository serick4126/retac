using System.Drawing;
using System.Windows.Forms;
using ReTAC.App;

namespace ReTAC.Domain.Tests;

public class DoubleClickRuleTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0);
    private static readonly Point P0 = new(100, 100);
    private static readonly TimeSpan Time = TimeSpan.FromMilliseconds(500);
    private static readonly Size Size = new(8, 6);   // 半分は 4 / 3

    [Fact]
    public void 時間内かつ範囲内なら二回目のクリック()
    {
        Assert.True(DoubleClickRule.IsSecondClick(T0, P0, T0 + TimeSpan.FromMilliseconds(200), P0, MouseButtons.Left, Time, Size));
    }

    [Fact]
    public void 時間の境界ちょうどは二回目のクリック()
    {
        Assert.True(DoubleClickRule.IsSecondClick(T0, P0, T0 + Time, P0, MouseButtons.Left, Time, Size));
    }

    [Fact]
    public void 時間の境界を超えたら二回目にならない()
    {
        Assert.False(DoubleClickRule.IsSecondClick(T0, P0, T0 + Time + TimeSpan.FromMilliseconds(1), P0, MouseButtons.Left, Time, Size));
    }

    [Fact]
    public void X方向が半分ちょうどなら二回目のクリック()
    {
        var pos = new Point(P0.X + Size.Width / 2, P0.Y);
        Assert.True(DoubleClickRule.IsSecondClick(T0, P0, T0, pos, MouseButtons.Left, Time, Size));
    }

    [Fact]
    public void X方向が半分を少し超えたら二回目にならない()
    {
        var pos = new Point(P0.X + Size.Width / 2 + 1, P0.Y);
        Assert.False(DoubleClickRule.IsSecondClick(T0, P0, T0, pos, MouseButtons.Left, Time, Size));
    }

    [Fact]
    public void Y方向が半分ちょうどなら二回目のクリック()
    {
        var pos = new Point(P0.X, P0.Y + Size.Height / 2);
        Assert.True(DoubleClickRule.IsSecondClick(T0, P0, T0, pos, MouseButtons.Left, Time, Size));
    }

    [Fact]
    public void Y方向が半分を少し超えたら二回目にならない()
    {
        var pos = new Point(P0.X, P0.Y + Size.Height / 2 + 1);
        Assert.False(DoubleClickRule.IsSecondClick(T0, P0, T0, pos, MouseButtons.Left, Time, Size));
    }

    [Fact]
    public void 中心とする矩形なのでX方向だけ全幅ずれたら二回目にならない()
    {
        // Width をそのまま許容範囲にする間違い（中心矩形ではなく片側基準）を検出する
        var pos = new Point(P0.X + Size.Width, P0.Y);
        Assert.False(DoubleClickRule.IsSecondClick(T0, P0, T0, pos, MouseButtons.Left, Time, Size));
    }

    [Fact]
    public void 左ボタン以外は範囲内でも二回目にならない()
    {
        Assert.False(DoubleClickRule.IsSecondClick(T0, P0, T0, P0, MouseButtons.Right, Time, Size));
    }
}

public class DoubleClickTrackerTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0);
    private static readonly Point P0 = new(100, 100);
    private static readonly TimeSpan Time = TimeSpan.FromMilliseconds(500);
    private static readonly Size Size = new(8, 6);

    [Fact]
    public void 覚えた直後の範囲内の二回目でtrueを一度だけ返す()
    {
        var tracker = new DoubleClickTracker();
        tracker.Remember(T0, P0);
        Assert.True(tracker.Decide(T0 + TimeSpan.FromMilliseconds(100), P0, MouseButtons.Left, Time, Size));
    }

    [Fact]
    public void 範囲外の二回目はfalseで閉じるだけになる()
    {
        var tracker = new DoubleClickTracker();
        tracker.Remember(T0, P0);
        Assert.False(tracker.Decide(T0 + Time + TimeSpan.FromSeconds(1), P0, MouseButtons.Left, Time, Size));
    }

    [Fact]
    public void ドラッグ開始などでForgetすると次の判定はfalseになる()
    {
        var tracker = new DoubleClickTracker();
        tracker.Remember(T0, P0);
        tracker.Forget();
        Assert.False(tracker.Decide(T0 + TimeSpan.FromMilliseconds(100), P0, MouseButtons.Left, Time, Size));
    }

    [Fact]
    public void 何も覚えていなければfalse()
    {
        var tracker = new DoubleClickTracker();
        Assert.False(tracker.Decide(T0, P0, MouseButtons.Left, Time, Size));
    }

    [Fact]
    public void ダブルクリックの後に前の時刻位置を使い回さない()
    {
        var tracker = new DoubleClickTracker();
        tracker.Remember(T0, P0);
        // 二回目でダブルクリックが成立する
        Assert.True(tracker.Decide(T0 + TimeSpan.FromMilliseconds(100), P0, MouseButtons.Left, Time, Size));
        // 三回目を続けて押しても、二回目の情報を使い回して成立しない（覚え直していないため）
        Assert.False(tracker.Decide(T0 + TimeSpan.FromMilliseconds(200), P0, MouseButtons.Left, Time, Size));
    }
}

public class DoubleClickTrackersTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0);
    private static readonly Point P0 = new(100, 100);
    private static readonly TimeSpan Time = TimeSpan.FromMilliseconds(500);
    private static readonly Size Size = new(8, 6);

    [Fact]
    public void ドラッグ開始で登録した項目のトラッカーを外から捨てられる()
    {
        var item = new ToolStripMenuItem();
        var tracker = new DoubleClickTracker();
        tracker.Remember(T0, P0);
        DoubleClickTrackers.Register(item, tracker);

        DoubleClickTrackers.Forget(item);

        // ドラッグを始めた後に素早くクリックしても、ドラッグ前の押下との二回目にはならない
        Assert.False(tracker.Decide(T0 + TimeSpan.FromMilliseconds(100), P0, MouseButtons.Left, Time, Size));
    }

    [Fact]
    public void 登録の無い項目にForgetを呼んでも何も起きない()
    {
        var item = new ToolStripMenuItem();
        DoubleClickTrackers.Forget(item);   // 例外にならなければ良い（バーのボタンはここに登録しない）
    }
}
