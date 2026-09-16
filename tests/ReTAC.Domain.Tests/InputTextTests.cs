using ReTAC.Domain.Navigation;

namespace ReTAC.Domain.Tests;

/// <summary>B-01: 入力欄から受けたパス・名前の端の落とし方</summary>
public class InputTextTests
{
    [Fact]
    public void 末尾の全角空白は残す()
    {
        // U+3000 はファイル名に使える。string.Trim() はこれを落としてしまう
        Assert.Equal("フォルダ　　", InputText.TrimEdge("フォルダ　　"));
        Assert.Equal("a　", InputText.TrimEdge("a　"));
    }

    [Fact]
    public void 末尾の半角空白とタブは落とす()
    {
        // Windows はファイル名の末尾に ASCII 空白を許さない
        Assert.Equal("フォルダ", InputText.TrimEdge("フォルダ  "));
        Assert.Equal("フォルダ", InputText.TrimEdge("フォルダ\t"));
    }

    [Fact]
    public void 先頭の半角空白も落とす()
    {
        Assert.Equal("C:\\x", InputText.TrimEdge("  C:\\x"));
    }

    [Fact]
    public void 先頭の全角空白は残す()
    {
        Assert.Equal("　名前", InputText.TrimEdge("　名前"));
    }

    [Fact]
    public void 空文字と空白だけの入力は空になる()
    {
        Assert.Equal("", InputText.TrimEdge(""));
        Assert.Equal("", InputText.TrimEdge("   "));
        // 全角空白だけなら残る。それが名前として妥当かは呼び出し側が判断する
        Assert.Equal("　", InputText.TrimEdge("　"));
    }
}
