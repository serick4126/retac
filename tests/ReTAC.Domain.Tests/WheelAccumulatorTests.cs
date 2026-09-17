using ReTAC.Domain.Listing;

namespace ReTAC.Domain.Tests;

/// <summary>R-76: ホイールの端数の持ち越し。1 ノッチ = 1 列</summary>
public class WheelAccumulatorTests
{
    private const int Notch = 120;

    [Fact]
    public void ノッチ1つで列1つ()
    {
        Assert.Equal(1, new WheelAccumulator().Add(120, Notch));
        Assert.Equal(-1, new WheelAccumulator().Add(-120, Notch));
    }

    [Fact]
    public void 端数は足し込まれ1ノッチ分たまったら1列()
    {
        var wheel = new WheelAccumulator();
        Assert.Equal(0, wheel.Add(30, Notch));
        Assert.Equal(0, wheel.Add(30, Notch));
        Assert.Equal(0, wheel.Add(30, Notch));
        Assert.Equal(1, wheel.Add(30, Notch));
        Assert.Equal(0, wheel.Add(30, Notch));   // 使った分は残らない
    }

    [Fact]
    public void 向きが変わったら端数を捨てる()
    {
        var wheel = new WheelAccumulator();
        wheel.Add(90, Notch);
        Assert.Equal(0, wheel.Add(-30, Notch));   // 90 は捨てられ -30 だけが残る
        Assert.Equal(-1, wheel.Add(-90, Notch));
    }

    [Fact]
    public void 複数ノッチ分は一度に返す()
    {
        Assert.Equal(2, new WheelAccumulator().Add(240, Notch));
        Assert.Equal(-3, new WheelAccumulator().Add(-360, Notch));
    }

    [Fact]
    public void Resetで端数が消える()
    {
        var wheel = new WheelAccumulator();
        wheel.Add(90, Notch);
        wheel.Reset();
        Assert.Equal(0, wheel.Add(30, Notch));
    }
}
