using ReTAC.App;

namespace ReTAC.Domain.Tests;

/// <summary>R-125: 転送の開始までの時間を区間ごとに測る（環境変数 RETAC_TRANSFER_TIMING があるときだけ）。</summary>
public class TransferTimingTests
{
    [Fact]
    public void 環境変数が無ければ測らない()
    {
        Environment.SetEnvironmentVariable(TransferTiming.Variable, null);
        Assert.Null(TransferTiming.Start());
    }

    [Fact]
    public void 区間の名前と時間を1行にまとめる()
    {
        var timing = new TransferTiming();
        timing.Mark("plan");
        timing.Mark("register");
        var line = timing.Format("copy items=3 folders=1");
        Assert.Matches(@"^copy items=3 folders=1 plan=\d+ms register=\d+ms$", line);
    }
}
