using System.Drawing;
using System.Windows.Forms;
using ReTAC.App;

namespace ReTAC.Domain.Tests;

/// <summary>
/// R-91-2: バー直下のフォルダ・グループのボタンのダブルクリック。BarDropDownButton がイベントから呼ぶ順に、
/// 状態のクラスを操作して確かめる（押す・離して展開・閉じる・外れる・ドラッグ）。
/// </summary>
public class BarDoubleClickStateTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0);
    private static readonly Point P0 = new(100, 100);
    private static readonly TimeSpan Time = TimeSpan.FromMilliseconds(500);
    private static readonly Size Size = new(8, 6);   // 半分は 4 / 3

    private static DateTime At(int ms) => T0 + TimeSpan.FromMilliseconds(ms);

    private static bool PressClosed(BarDoubleClickState s, DateTime t, Point? p = null) =>
        s.PressWhileClosed(t, p ?? P0, MouseButtons.Left, Time, Size);

    /// <summary>閉じた状態で押して離す（ジャンプでなければ、離したときに展開する）。</summary>
    private static void ClickToOpen(BarDoubleClickState s, DateTime t)
    {
        Assert.False(PressClosed(s, t));
        s.DropDownOpened();
        s.OpenedByRelease(t, P0);
    }

    /// <summary>展開している間にこのボタンを押す。ジャンプでなければ、閉じる処理（DropDownClosed が先に来る）を通す。</summary>
    private static bool PressOpen(BarDoubleClickState s, DateTime t, bool jumpWired) =>
        s.PressWhileOpen(t, P0, MouseButtons.Left, Time, Size, jumpWired, close: () =>
        {
            s.DropDownClosed();
            return true;
        });

    [Fact]
    public void フォルダは展開した状態から自分で閉じて範囲内で押すとジャンプする()
    {
        var s = new BarDoubleClickState();
        ClickToOpen(s, At(0));
        Assert.False(PressOpen(s, At(2000), jumpWired: true));   // 時間が経っているので閉じるだけ
        Assert.True(PressClosed(s, At(2100)));
    }

    [Fact]
    public void ジャンプした直後に範囲内でもう一度押してもジャンプを重ねない()
    {
        var s = new BarDoubleClickState();
        ClickToOpen(s, At(0));
        PressOpen(s, At(2000), jumpWired: true);
        Assert.True(PressClosed(s, At(2100)));
        Assert.False(PressClosed(s, At(2200)));
    }

    [Fact]
    public void 閉じた状態からのダブルクリックは今までどおり閉じてジャンプする()
    {
        var s = new BarDoubleClickState();
        ClickToOpen(s, At(0));
        var closed = false;
        Assert.True(s.PressWhileOpen(At(100), P0, MouseButtons.Left, Time, Size, jumpWired: true, close: () => closed = true));
        Assert.False(closed);   // ジャンプのときは閉じる処理を呼ばない（呼び出し側が閉じてジャンプする）
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 外側のクリックやEscで閉じた直後に範囲内で押しても展開する(bool jumpWired)
    {
        // 外側のクリックも Esc も、ボタンにとっては DropDownClosed が来るだけ。
        // 前に一度自分で閉じて（フォルダなら閉じた押下を覚えて）から、開き直した場合も含める
        var s = new BarDoubleClickState();
        ClickToOpen(s, At(0));
        PressOpen(s, At(2000), jumpWired);
        ClickToOpen(s, At(4000));
        s.DropDownClosed();
        Assert.False(PressClosed(s, At(4100)));
    }

    [Fact]
    public void 自分で閉じたあと時間が範囲外なら展開する()
    {
        var s = new BarDoubleClickState();
        ClickToOpen(s, At(0));
        PressOpen(s, At(2000), jumpWired: true);
        Assert.False(PressClosed(s, At(2000 + 501)));
    }

    [Fact]
    public void 自分で閉じたあと位置が範囲外なら展開する()
    {
        var s = new BarDoubleClickState();
        ClickToOpen(s, At(0));
        PressOpen(s, At(2000), jumpWired: true);
        Assert.False(PressClosed(s, At(2100), new Point(P0.X + 5, P0.Y)));
    }

    [Fact]
    public void 自分で閉じたあとボタンから外れたら展開する()
    {
        var s = new BarDoubleClickState();
        ClickToOpen(s, At(0));
        PressOpen(s, At(2000), jumpWired: true);
        s.MouseLeft();
        Assert.False(PressClosed(s, At(2100)));
    }

    [Fact]
    public void 自分で閉じたあとキーボードで展開して閉じたら展開する()
    {
        var s = new BarDoubleClickState();
        ClickToOpen(s, At(0));
        PressOpen(s, At(2000), jumpWired: true);
        s.DropDownOpened();
        s.DropDownClosed();
        Assert.False(PressClosed(s, At(2100)));
    }

    [Fact]
    public void 自分で閉じたあとドラッグを始めたら展開する()
    {
        var s = new BarDoubleClickState();
        ClickToOpen(s, At(0));
        PressOpen(s, At(2000), jumpWired: true);
        s.Cancel();
        Assert.False(PressClosed(s, At(2100)));
    }

    [Fact]
    public void グループは自分で閉じて範囲内で押しても展開し直す()
    {
        var s = new BarDoubleClickState();
        ClickToOpen(s, At(0));
        Assert.False(PressOpen(s, At(2000), jumpWired: false));
        Assert.False(PressClosed(s, At(2100)));
    }

    [Fact]
    public void 押しても閉じなかったときは閉じた押下を覚えない()
    {
        // Alt を押しながらなど、標準の処理が閉じなかったとき
        var s = new BarDoubleClickState();
        ClickToOpen(s, At(0));
        Assert.False(s.PressWhileOpen(At(2000), P0, MouseButtons.Left, Time, Size, jumpWired: true, close: () => false));
        Assert.False(PressClosed(s, At(2100)));
    }
}
