using ReTAC.Domain.Navigation;

namespace ReTAC.Domain.Tests;

/// <summary>R-87: ダイレクトジャンプの入力を行き先に変える（ダイアログとアドレスバーで共通）</summary>
public class JumpInputTests
{
    private static readonly HashSet<string> Folders = new(StringComparer.OrdinalIgnoreCase) { @"C:\", @"C:\Work", @"C:\Work\Sub" };
    private static readonly HashSet<string> Files = new(StringComparer.OrdinalIgnoreCase) { @"C:\Work\a.txt" };

    private static JumpTarget? Decide(string input) =>
        JumpInput.Decide(@"C:\Work", input, Folders.Contains, Files.Contains);

    [Fact]
    public void フォルダはそのフォルダへ() => Assert.Equal(new JumpTarget(@"C:\Work\Sub", null), Decide(@"C:\Work\Sub"));

    [Fact]
    public void 相対パスはカレントフォルダ基準() => Assert.Equal(new JumpTarget(@"C:\Work\Sub", null), Decide("Sub"));

    [Fact]
    public void ファイルはそのフォルダへ移ってカーソルを合わせる() =>
        Assert.Equal(new JumpTarget(@"C:\Work", "a.txt"), Decide(@"C:\Work\a.txt"));

    [Theory]
    [InlineData(@"C:\Nothing")]
    [InlineData(@"..\..")]        // R-61-3: ルートを超える
    [InlineData("a|b")]           // 名前に使えない文字
    [InlineData("")]
    public void 見つからない入力は行き先なし(string input) => Assert.Null(Decide(input));
}
