using ReTAC.App;
using ReTAC.Updater.Core;

namespace ReTAC.Domain.Tests.Updater;

/// <summary>R-109: アップデータの判定（版・zip の項目・入れ替えの順・結果の表示・版をまたぐ約束）。</summary>
public class UpdaterDecisionTests
{
    // ---- 版（R-109-2）----

    [Theory]
    [InlineData("v2.8.0", 2, 8, 0)]
    [InlineData("v10.0.12", 10, 0, 12)]
    public void タグは_vXYZ_だけを読む(string tag, int major, int minor, int build) =>
        Assert.Equal(new Version(major, minor, build), Versions.ParseTag(tag));

    [Theory]
    [InlineData("2.8.0")]
    [InlineData("v2.8")]
    [InlineData("v2.8.0-beta")]
    [InlineData("v2.8.0.1")]
    [InlineData(" v2.8.0")]
    [InlineData("")]
    [InlineData(null)]
    public void 形の違うタグは読めない(string? tag) => Assert.Null(Versions.ParseTag(tag));

    [Theory]
    [InlineData("2.8.0", "2.7.1", "2.7.1", false)]   // 新しい版がある
    [InlineData("2.7.1", "2.7.1", "2.7.1", true)]    // 同じ
    [InlineData("2.7.0", "2.7.1", "2.7.1", true)]    // 古い
    [InlineData("2.8.0", "2.8.0", "2.7.1", false)]   // アップデータだけ古い → 最新ではない
    [InlineData("2.8.0", "2.7.1", "2.8.0", false)]   // ReTAC.exe だけ古い
    public void 最新かは2つのexeの古いほうで決める(string latest, string reTac, string updater, bool upToDate) =>
        Assert.Equal(upToDate, Versions.IsUpToDate(Version.Parse(latest), Version.Parse(reTac), Version.Parse(updater)));

    [Fact]
    public void アップデータが無ければReTACだけで決める()
    {
        Assert.True(Versions.IsUpToDate(new Version(2, 8, 0), new Version(2, 8, 0), null));
        Assert.False(Versions.IsUpToDate(new Version(2, 8, 0), new Version(2, 7, 1), null));
    }

    [Fact]
    public void ファイルバージョンは先頭の3つだけを使う()
    {
        // テストの実行ファイル自身の版（Directory.Build.props）。4 つめが何であっても 3 つに落ちる
        var version = Versions.OfFile(typeof(MainForm).Assembly.Location)!;
        Assert.Equal(-1, version.Revision);
    }

    // ---- 判定し直し（R-109-4）----

    private static readonly Version V2 = new(2, 8, 0);
    private static readonly Version V1 = new(2, 7, 1);

    private static Dictionary<string, string?> Hashes(string mark) =>
        Distribution.Names.ToDictionary(n => n, n => (string?)(n + mark));

    [Fact]
    public void 何も変わっていなければ変更なし() =>
        Assert.Equal(ReplaceResult.Unchanged, Versions.Rejudge(Hashes("old"), Hashes("old"), V2, V1, V1));

    [Fact]
    public void 二つのexeが新版なら文書が古くても完了()
    {
        var after = Hashes("old");
        after[Protocol.ReTacExe] = "new";
        after[Protocol.UpdaterExe] = "new";
        Assert.Equal(ReplaceResult.Completed, Versions.Rejudge(Hashes("old"), after, V2, V2, V2));
    }

    [Theory]
    [InlineData(Protocol.ReTacExe)]      // ReTAC.exe だけ新版
    [InlineData(Protocol.UpdaterExe)]    // アップデータだけ新版
    [InlineData("README.md")]            // 文書だけ新版
    public void 一部だけ新しければ一部を更新(string changed)
    {
        var after = Hashes("old");
        after[changed] = "new";
        var reTac = changed == Protocol.ReTacExe ? V2 : V1;
        var updater = changed == Protocol.UpdaterExe ? V2 : V1;
        Assert.Equal(ReplaceResult.Partial, Versions.Rejudge(Hashes("old"), after, V2, reTac, updater));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void 完了と最新の判定はそろっている(bool reTacNew, bool updaterNew)
    {
        var after = Hashes("old");
        if (reTacNew) after[Protocol.ReTacExe] = "new";
        if (updaterNew) after[Protocol.UpdaterExe] = "new";
        var reTac = reTacNew ? V2 : V1;
        var updater = updaterNew ? V2 : V1;

        var completed = Versions.Rejudge(Hashes("old"), after, V2, reTac, updater) == ReplaceResult.Completed;
        Assert.Equal(completed, Versions.IsUpToDate(V2, reTac, updater));
    }

    // ---- zip の項目（R-109-2）----

    private static readonly string Staging = Path.Combine(Path.GetTempPath(), "ReTAC.update-test");

    private static string[] Official => [.. Distribution.Names];

    [Fact]
    public void 公式の6ファイルは受け付ける() => Assert.Null(Distribution.Check(Official, Staging));

    [Fact]
    public void 文書が欠けていても2つのexeがあれば受け付ける() =>
        Assert.Null(Distribution.Check([Protocol.ReTacExe, Protocol.UpdaterExe], Staging));

    [Fact]
    public void 大文字小文字の違う名前も配布物として扱う() =>
        Assert.Null(Distribution.Check(["retac.EXE", "RETAC.UPDATER.exe", "readme.md"], Staging));

    [Theory]
    [InlineData(@"..\x")]
    [InlineData(@"a\..\..\x")]
    [InlineData(@"C:\x")]
    [InlineData(@"\x")]
    [InlineData(@"sub\ReTAC.exe")]
    [InlineData("sub/ReTAC.exe")]
    public void 準備フォルダの直下を指さない項目は拒否する(string entry) =>
        Assert.NotNull(Distribution.Check([.. Official, entry], Staging));

    [Theory]
    [InlineData("x.dll")]
    [InlineData("retac.settings.json")]
    [InlineData("sub/")]
    [InlineData("RETAC.EXE")]      // ReTAC.exe と大文字小文字だけ違う
    [InlineData("ReTAC.exe")]      // 同じ名前が 2 つ
    public void 配布物の外や重複は拒否する(string extra) =>
        Assert.Equal(Reason.UnsupportedFormat, Distribution.Check([.. Official, extra], Staging));

    [Theory]
    [InlineData(Protocol.ReTacExe)]
    [InlineData(Protocol.UpdaterExe)]
    public void exeが欠けたzipは拒否する(string missing) =>
        Assert.Equal(Reason.UnsupportedFormat, Distribution.Check(Official.Where(n => n != missing), Staging));

    [Fact]
    public void ReTACexeは最後に入れ替える()
    {
        var order = Distribution.ReplaceOrder([Protocol.ReTacExe, "README.md", Protocol.UpdaterExe, "LICENSE"]);
        Assert.Equal(Protocol.ReTacExe, order[^1]);
        Assert.Equal(4, order.Count);
    }

    // ---- 結果の表示（R-109-4）----

    [Theory]
    [InlineData(false, 0, false)]
    [InlineData(true, 0, true)]
    [InlineData(false, 1, true)]
    [InlineData(true, 3, true)]
    public void 起動するのはReTACを終了させたか入れ替えたとき(bool stopped, int replaced, bool launch) =>
        Assert.Equal(launch, Outcome.ShouldLaunch(stopped, replaced));

    [Theory]
    [InlineData(ReplaceResult.Unchanged, false, false, "GitHub に接続できませんでした。ReTAC は変更していません。")]
    [InlineData(ReplaceResult.Unchanged, true, true, "GitHub に接続できませんでした。ReTAC は変更していません。")]
    [InlineData(ReplaceResult.Unchanged, true, false, "GitHub に接続できませんでした。ReTAC は変更していませんが、起動できませんでした。")]
    [InlineData(ReplaceResult.Completed, true, true, "v2.8.0 に更新しました。")]
    [InlineData(ReplaceResult.Completed, true, false, "v2.8.0 に更新しましたが、ReTAC を起動できませんでした。")]
    [InlineData(ReplaceResult.Partial, true, true, "一部のファイルを更新できませんでした。もう一度更新してください。")]
    [InlineData(ReplaceResult.Partial, true, false, "一部のファイルを更新できませんでした。ReTAC も起動できませんでした。もう一度更新してください。")]
    public void 結果の文は表の7行のとおり(ReplaceResult result, bool launched, bool succeeded, string expected) =>
        Assert.Equal(expected, Outcome.Message(result, Reason.CannotConnect, launched, succeeded, "v2.8.0"));

    [Fact]
    public void すべての理由に文がある()
    {
        foreach (Reason reason in Enum.GetValues(typeof(Reason)))
            Assert.False(string.IsNullOrWhiteSpace(Outcome.ReasonText(reason)));
    }

    // ---- 終了コード（R-109-5）----

    [Fact]
    public void 終了コードは結果と理由を往復する()
    {
        foreach (ReplaceResult result in Enum.GetValues(typeof(ReplaceResult)))
            foreach (Reason reason in Enum.GetValues(typeof(Reason)))
                Assert.Equal((result, reason), ExitCodes.Decode(ExitCodes.Encode(result, reason)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]                   // 結果の番号が無い
    [InlineData(unchecked((int)0xC0000005))]   // 異常終了
    public void 決まった形でない終了コードは読まない(int code) => Assert.Null(ExitCodes.Decode(code));

    // ---- 版をまたぐ約束（R-109）----

    [Fact]
    public void ReTACとアップデータの約束の値が一致する()
    {
        Assert.Equal(UpdateProtocol.MessageName, Protocol.MessageName);
        Assert.Equal(UpdateProtocol.WindowPropName, Protocol.WindowPropName);
        Assert.Equal(UpdateProtocol.Accepted, Protocol.Accepted);
        Assert.Equal(UpdateProtocol.Refused, Protocol.Refused);
        Assert.Equal(UpdateProtocol.StateMessageName, Protocol.StateMessageName);
        Assert.Equal(UpdateProtocol.Quitting, Protocol.Quitting);
        Assert.Equal(UpdateProtocol.NotQuitting, Protocol.NotQuitting);
        Assert.Equal("ReTAC.QuitForUpdateState", Protocol.StateMessageName);
        // 値そのものも固定する（両方を同時に変えても通らないように）
        Assert.Equal("ReTAC.QuitForUpdate", Protocol.MessageName);
        Assert.Equal("ReTAC.MainWindow", Protocol.WindowPropName);
    }
}
