using ReTAC.App;

namespace ReTAC.Domain.Tests;

/// <summary>R-109-3: アップデータからの終了依頼への返事と、版をまたぐ約束の値。</summary>
public class UpdateProtocolTests
{
    [Fact]
    public void 版をまたぐ約束の値は変わっていない()
    {
        // 値を変えると、旧版の ReTAC を新版のアップデータが（またはその逆が）終了させられなくなる
        Assert.Equal("ReTAC.QuitForUpdate", UpdateProtocol.MessageName);
        Assert.Equal("ReTAC.MainWindow", UpdateProtocol.WindowPropName);
        Assert.Equal(1, UpdateProtocol.Accepted);
        Assert.Equal(2, UpdateProtocol.Refused);
    }

    [Fact]
    public void 無効なウィンドウがあると断り印は立たない()
    {
        var requested = false;
        Assert.Equal((UpdateProtocol.Refused, false), UpdateProtocol.Decide(true, false, ref requested));
        Assert.False(requested);
    }

    [Fact]
    public void ファイル操作の最中は断る()
    {
        var requested = false;
        Assert.Equal((UpdateProtocol.Refused, false), UpdateProtocol.Decide(false, true, ref requested));
        Assert.False(requested);
    }

    [Fact]
    public void 受け付けた後の依頼では終了処理を始めない()
    {
        var requested = false;
        Assert.Equal((UpdateProtocol.Accepted, true), UpdateProtocol.Decide(false, false, ref requested));
        Assert.Equal((UpdateProtocol.Accepted, false), UpdateProtocol.Decide(false, false, ref requested));
        Assert.True(requested);
    }

    [Fact]
    public void 印を戻せば次の依頼でまた終了処理を始める()
    {
        var requested = false;
        UpdateProtocol.Decide(false, false, ref requested);
        requested = false;   // K-5 の確認で取りやめたとき、MainForm が印を戻す
        Assert.Equal((UpdateProtocol.Accepted, true), UpdateProtocol.Decide(false, false, ref requested));
    }

    [Fact]
    public void 受け付け済みでもダイアログを開いていれば断る()
    {
        var requested = true;
        Assert.Equal((UpdateProtocol.Refused, false), UpdateProtocol.Decide(true, false, ref requested));
        Assert.True(requested);
    }
}
