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
        Assert.Equal("ReTAC.QuitForUpdateState", UpdateProtocol.StateMessageName);
        Assert.Equal(1, UpdateProtocol.Quitting);
        Assert.Equal(2, UpdateProtocol.NotQuitting);
    }

    [Fact]
    public void 様子の問い合わせは受け付けの印をそのまま答える()
    {
        var gate = new QuitForUpdateGate();
        Assert.Equal(UpdateProtocol.NotQuitting, gate.OnStateQuery());   // まだ頼まれていない

        gate.OnRequest(false, () => { });
        Assert.Equal(UpdateProtocol.Quitting, gate.OnStateQuery());      // 受け付けた（K-5 の確認の間も）

        gate.OnQuitFinished(quit: false);
        Assert.Equal(UpdateProtocol.NotQuitting, gate.OnStateQuery());   // K-5 の確認で取りやめた

        gate.OnRequest(false, () => { });
        gate.OnQuitFinished(quit: true);
        Assert.Equal(UpdateProtocol.Quitting, gate.OnStateQuery());      // 終了を選んだ。プロセスが消えるまで立ったまま
    }

    [Fact]
    public void 断った依頼の後の問い合わせは受け付けていないと答える()
    {
        var gate = new QuitForUpdateGate();
        gate.OnRequest(anyWindowDisabled: true, () => { });
        Assert.Equal(UpdateProtocol.NotQuitting, gate.OnStateQuery());
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

    [Theory]
    [InlineData(true, false)]    // 受け付けた後、K-5 の確認でウィンドウが無効になっている
    [InlineData(false, true)]    // 受け付けた後、ファイル操作が走っている
    [InlineData(true, true)]
    public void 受け付けた後の依頼はダイアログや操作の最中でも受け付け済みと返す(bool disabled, bool running)
    {
        var requested = true;
        Assert.Equal((UpdateProtocol.Accepted, false), UpdateProtocol.Decide(disabled, running, ref requested));
        Assert.True(requested);
    }

    // ---- QuitForUpdateGate（MainForm の配線）----

    [Fact]
    public void 受け口は受け付けたときだけ終了処理を1回予約する()
    {
        var gate = new QuitForUpdateGate();
        var scheduled = 0;

        Assert.Equal(UpdateProtocol.Accepted, gate.OnRequest(false, () => scheduled++));
        // 予約した終了処理が K-5 の確認を出している間（ウィンドウが無効）に、もう一度依頼が来る
        Assert.Equal(UpdateProtocol.Accepted, gate.OnRequest(true, () => scheduled++));
        Assert.Equal(UpdateProtocol.Accepted, gate.OnRequest(false, () => scheduled++));

        Assert.Equal(1, scheduled);
    }

    [Fact]
    public void 受け口はダイアログを開いている間は断り予約しない()
    {
        var gate = new QuitForUpdateGate();
        var scheduled = 0;
        Assert.Equal(UpdateProtocol.Refused, gate.OnRequest(true, () => scheduled++));
        Assert.Equal(0, scheduled);
        // ダイアログを閉じた後は受け付ける（断っても印は立っていない）
        Assert.Equal(UpdateProtocol.Accepted, gate.OnRequest(false, () => scheduled++));
        Assert.Equal(1, scheduled);
    }

    [Fact]
    public void 受け口はファイル操作の間だけ断る()
    {
        var gate = new QuitForUpdateGate();
        var scheduled = 0;

        using (gate.BeginOperation())
        {
            using (gate.BeginOperation())   // 別のウィンドウの操作が重なる
                Assert.Equal(UpdateProtocol.Refused, gate.OnRequest(false, () => scheduled++));
            Assert.Equal(UpdateProtocol.Refused, gate.OnRequest(false, () => scheduled++));
        }
        Assert.Equal(0, scheduled);
        Assert.Equal(UpdateProtocol.Accepted, gate.OnRequest(false, () => scheduled++));
        Assert.Equal(1, scheduled);
    }

    [Fact]
    public void 操作の終わりを2回伝えても数は1つしか減らない()
    {
        var gate = new QuitForUpdateGate();
        var outer = gate.BeginOperation();
        var inner = gate.BeginOperation();
        inner.Dispose();
        inner.Dispose();
        Assert.Equal(UpdateProtocol.Refused, gate.OnRequest(false, () => { }));
        outer.Dispose();
        Assert.Equal(UpdateProtocol.Accepted, gate.OnRequest(false, () => { }));
    }

    [Fact]
    public void 終了を取りやめたら次の依頼でまた予約する()
    {
        var gate = new QuitForUpdateGate();
        var scheduled = 0;
        gate.OnRequest(false, () => scheduled++);
        gate.OnQuitFinished(quit: false);   // K-5 の確認で取りやめた

        Assert.Equal(UpdateProtocol.Accepted, gate.OnRequest(false, () => scheduled++));
        Assert.Equal(2, scheduled);
    }

    [Fact]
    public void 終了したときは印を戻さない()
    {
        var gate = new QuitForUpdateGate();
        var scheduled = 0;
        gate.OnRequest(false, () => scheduled++);
        gate.OnQuitFinished(quit: true);

        Assert.Equal(UpdateProtocol.Accepted, gate.OnRequest(false, () => scheduled++));
        Assert.Equal(1, scheduled);
    }
}
