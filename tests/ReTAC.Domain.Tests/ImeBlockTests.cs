using ReTAC.Domain.Tests;

namespace ReTAC.Domain.Tests;

/// <summary>
/// B-04: ファイルリストとドライブバーは IME を使わない（L の次の 1 打鍵がジャンプになる）。
/// DriveBar はコンストラクタでグラフィックスを作りハンドルが要るため、テストでは作れない。
/// ソースの配線だけ確かめる。実際の切り離しは実機（IME オンで L の次に D）で確かめる。
/// </summary>
public class ImeBlockTests
{
    private static string Src(string name) => File.ReadAllText(Path.Combine(SchemaManifest.RepoRoot(), "src", "ReTAC.App", name));

    [Theory]
    [InlineData("DriveBar.cs")]
    [InlineData("FileListView.cs")]
    public void SelfDrawnControls_DetachIme(string file)
    {
        var text = Src(file);
        Assert.Contains("ImeMode = ImeMode.Disable", text);
        Assert.Contains("ImeBlock.Detach(this)", text);
        Assert.DoesNotContain("ImmAssociateContext", text);   // P/Invoke は ImeBlock の 1 か所だけ
    }
}
