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

    /// <summary>
    /// R-143-1 で専用の入力コンテキストを持たせないダイアログ。入力できる欄が無いか、IME を使わない窓だけを載せる。
    /// 新しいダイアログはここに載せるか <c>ImeBlock.OwnContext(this)</c> を呼ぶかを決めないとテストが通らない。
    /// </summary>
    private static readonly Dictionary<string, string> s_noOwnContext = new()
    {
        ["MainForm"] = "主ウィンドウ。アドレスバー・インクリメンタルサーチはファイルリストと同じ窓で長く使うので共有のまま",
        ["DriveSelectForm"] = "R-143: 1 打鍵がドライブの選択。IME を使わない",
        ["AboutDialog"] = "入力欄なし",
        ["AttributeDialog"] = "日付と時刻の選択だけ。IME を使わない",
        ["BookmarkDialog"] = "選ぶだけのコンボボックス",
        ["ChoiceDialog"] = "入力欄なし",
        ["ConfirmDialog"] = "読むだけの欄",
        ["ConflictDialog"] = "入力欄なし",
        ["ExternalToolQueueForm"] = "入力欄なし",
        ["FileTypeDialog"] = "入力欄なし",
        ["MacroReferenceDialog"] = "読むだけの欄",
        ["SortDialog"] = "入力欄なし",
    };

    [Fact]
    public void EveryDialog_OwnsImeContext_OrIsListedWithReason()
    {
        var dir = Path.Combine(SchemaManifest.RepoRoot(), "src", "ReTAC.App");
        var forms = new Dictionary<string, string>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.cs"))
        {
            var text = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(text, @"class\s+(\w+)(?:\([^)]*\))?\s*:\s*Form\b"))
                forms[m.Groups[1].Value] = text;
        }

        Assert.NotEmpty(forms);
        var missing = forms.Where(f => !f.Value.Contains("ImeBlock.OwnContext(this)") && !s_noOwnContext.ContainsKey(f.Key))
                           .Select(f => f.Key).ToList();
        Assert.True(missing.Count == 0, "R-143-1: ImeBlock.OwnContext(this) を呼ぶか s_noOwnContext に理由を書く: " + string.Join(", ", missing));
        Assert.Empty(s_noOwnContext.Keys.Where(k => !forms.ContainsKey(k)));   // 消えたダイアログを載せたままにしない
        Assert.Empty(s_noOwnContext.Keys.Where(k => forms.TryGetValue(k, out var t) && t.Contains("ImeBlock.OwnContext(this)")));
    }
}
