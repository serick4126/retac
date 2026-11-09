using ReTAC.Domain.Navigation;

namespace ReTAC.Domain.Tests;

/// <summary>R-87: ダイレクトジャンプの入力を行き先に変える（ダイアログとアドレスバーで共通）</summary>
public class JumpInputTests
{
    private static readonly HashSet<string> Folders = new(StringComparer.OrdinalIgnoreCase) { @"C:\", @"C:\Work", @"C:\Work\Sub" };
    private static readonly HashSet<string> Files = new(StringComparer.OrdinalIgnoreCase) { @"C:\Work\a.txt" };

    private static JumpTarget? Decide(string input) =>
        JumpInput.Decide(@"C:\Work", input, Folders.Contains, Files.Contains, _ => null);

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

    [Fact]
    public void 引用符で囲んだフォルダのパスは_そのフォルダへ() =>
        Assert.Equal(new JumpTarget(@"C:\Work\Sub", null), Decide("\"C:\\Work\\Sub\""));

    [Fact]
    public void 環境変数を含む入力は置き換えて行き先を決める()
    {
        // R-147。実環境ではなく差し替えた読み取りを使う
        Func<string, string?> env = name => name == "P32_JUMP" ? "Sub" : null;
        Assert.Equal(new JumpTarget(@"C:\Work\Sub", null), JumpInput.Decide(@"C:\Work", "%P32_JUMP%", Folders.Contains, Files.Contains, env));
        Assert.Equal(new JumpTarget(@"C:\Work", "a.txt"), JumpInput.Decide(@"C:\Work", @"C:\Work\%P32_JUMP%\..\a.txt", Folders.Contains, Files.Contains, env));
        Assert.Null(JumpInput.Decide(@"C:\Work", "%P32_JUMP%", Folders.Contains, Files.Contains, _ => null));   // 未定義は文字のまま。無い
    }

    [Fact]
    public void 引用符で囲んだファイルのパスは_そのフォルダへ移ってカーソルを合わせる() =>
        Assert.Equal(new JumpTarget(@"C:\Work", "a.txt"), Decide("\"C:\\Work\\a.txt\""));
}
