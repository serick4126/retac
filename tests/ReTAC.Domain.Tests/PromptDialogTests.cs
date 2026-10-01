using System.Windows.Forms;
using ReTAC.App;
using ReTAC.Domain.Navigation;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

/// <summary>R-131: 実行時の入力ダイアログ（ハンドルを作らずに、組み立てと値だけを確かめる）</summary>
public class PromptDialogTests
{
    private static readonly PromptDefinition Definition = new()
    {
        RememberLast = true,
        Items =
        [
            new PromptItem { Id = 1, Kind = PromptItemKind.Text, Label = "R&D", Initial = "既定", LastText = "前回" },
            new PromptItem { Id = 2, Kind = PromptItemKind.Folder, Label = "出力先", Initial = "", LastText = @"E:\前回" },
            new PromptItem { Id = 3, Kind = PromptItemKind.File, Label = "比較先", Initial = "b.txt", LastText = "c.txt" },
            new PromptItem { Id = 4, Kind = PromptItemKind.CheckBox, Label = "Q&A", Value = "-y", InitialChecked = true, LastChecked = false },
            new PromptItem
            {
                Id = 5, Kind = PromptItemKind.DropDown, Label = "画質", InitialChoiceId = 2, LastChoiceId = 1,
                Choices = [new PromptChoice { Id = 1, Label = "高速" }, new PromptChoice { Id = 2, Label = "標準" }],
            },
            new PromptItem { Id = 6, Kind = PromptItemKind.Folder, Label = "作業先" },
        ],
        Arguments = [.. Enumerable.Range(1, 6).Select(PromptArgument.Item)],
    };

    private static PromptDialog Open(PromptDefinition definition) =>
        new(definition, "ツール", new FolderHistory(), null, @"C:\work", PromptAnswerRules.Initial(definition));

    private static IEnumerable<Control> All(Control root) =>
        root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(All(c)));

    [Fact]
    public void 開いたときは前回の値で初期値に戻すと定義の初期値になる()
    {
        using var dialog = Open(Definition);
        Assert.Equal(PromptAnswerRules.Initial(Definition), dialog.ReadValues());

        dialog.ResetValues();
        Assert.Equal(PromptAnswerRules.Defaults(Definition), dialog.ReadValues());
    }

    [Fact]
    public void ラベルのアンドはアクセスキーにしない()
    {
        using var dialog = Open(Definition);
        var label = All(dialog).OfType<Label>().Single(l => l.Text == "R&D");
        Assert.False(label.UseMnemonic);
        var check = All(dialog).OfType<CheckBox>().Single(c => c.Text == "Q&A");
        Assert.False(check.UseMnemonic);
    }

    [Fact]
    public void 履歴と参照のアクセスキーは最初のフォルダの項目にだけ付ける()
    {
        using var dialog = Open(Definition);
        var texts = All(dialog).OfType<Button>().Select(b => b.Text).ToList();
        Assert.Single(texts, t => t == "履歴 ▼(&H)");
        Assert.Single(texts, t => t == "参照(&B)...");
        Assert.Single(texts, t => t == "履歴 ▼");
        Assert.Equal(2, texts.Count(t => t == "参照..."));   // ファイルと 2 つ目のフォルダ
    }

    [Fact]
    public void ボタンは初期値に戻すとOKとキャンセル()
    {
        using var dialog = Open(Definition);
        var texts = All(dialog).OfType<Button>().Select(b => b.Text).ToList();
        Assert.Contains("初期値に戻す(&R)", texts);
        Assert.Contains("OK", texts);
        Assert.Contains("キャンセル", texts);
    }

    [Fact]
    public void 項目が無ければ初期値に戻すは押せない()
    {
        using var dialog = Open(new PromptDefinition());
        Assert.False(All(dialog).OfType<Button>().Single(b => b.Text == "初期値に戻す(&R)").Enabled);
    }

    [Fact]
    public void 定義の無いプロンプトはツール名のテキスト一つで初期値に戻すと空()
    {
        var simple = PromptDefinition.Simple("検索");
        using var dialog = Open(simple);
        Assert.Equal("検索", dialog.Text);
        dialog.ResetValues();
        Assert.Equal(new PromptValue(""), dialog.ReadValues()[1]);
    }

    [Fact]
    public void タイトルが空ならツールの名前()
    {
        using var dialog = Open(Definition);
        Assert.Equal("ツール", dialog.Text);
        using var titled = Open(Definition with { Title = "展開" });
        Assert.Equal("展開", titled.Text);
    }

    [Fact]
    public void パネルの中のOKとキャンセルもモードレスで閉じるボタンになる()
    {
        using var dialog = Open(Definition);
        var closing = OwnerModal.ClosingButtons(dialog).Select(b => b.Text).ToList();
        Assert.Contains("OK", closing);
        Assert.Contains("キャンセル", closing);
        Assert.DoesNotContain("初期値に戻す(&R)", closing);   // DialogResult を持たないボタンは閉じない
        Assert.Same(dialog.AcceptButton, OwnerModal.ClosingButtons(dialog).Single(b => b.Text == "OK"));
        Assert.Same(dialog.CancelButton, OwnerModal.ClosingButtons(dialog).Single(b => b.Text == "キャンセル"));
    }

    [Theory]
    [InlineData("\"C:\\work\\out\"", @"C:\work\out")]     // 引用符付き（「パスのコピー」の形）
    [InlineData("out", @"C:\work\out")]                   // 相対パス
    [InlineData(@" C:\a ", @"C:\a")]
    [InlineData("", null)]
    [InlineData("\"\"", null)]
    [InlineData("bad|name", null)]
    [InlineData("\"\"C:\\x\"\"", null)]                    // 引用符は 1 つずつしか外さない
    public void 参照の最初の場所は入力を解決したパス(string text, string? expected)
    {
        Assert.Equal(expected, PromptDialog.BrowseStart(text, @"C:\work"));
    }

    [Theory]
    [InlineData("", @"C:\work", "")]                              // 空欄
    [InlineData(@"C:\work\sub", @"C:\work\sub", "")]              // フォルダが存在する
    [InlineData(@"C:\work\a.txt", @"C:\work", "a.txt")]           // ファイルが存在する
    [InlineData(@"C:\work\new.txt", @"C:\work", "")]              // ファイルは無いが親はある
    [InlineData(@"C:\work\missing\a.txt", @"C:\work", "")]        // 親も無い
    [InlineData("\"\"C:\\x\"\"", @"C:\work", "")]                 // 解決できないパス
    public void ファイル参照の最初の場所とファイル名(string text, string expectedDirectory, string expectedFileName)
    {
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\work", @"C:\work\sub" };
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\work\a.txt" };
        var (directory, fileName) = PromptDialog.FileBrowseStart(text, @"C:\work", folders.Contains, files.Contains);
        Assert.Equal(expectedDirectory, directory);
        Assert.Equal(expectedFileName, fileName);
    }

    [Fact]
    public void 項目が重なった定義でも組み立てで例外にしない()
    {
        var broken = new PromptDefinition { Items = [Definition.Items[0], Definition.Items[0]] };
        using var dialog = Open(broken);
        Assert.Single(dialog.ReadValues());
    }

    [Theory]
    // 親の中央に置く
    [InlineData(100, 100, 800, 600, 400, 300, 300, 250)]
    // 下にはみ出すなら作業領域の下端に合わせる（10 項目・150% で高さが伸びた）
    [InlineData(100, 500, 800, 600, 400, 700, 300, 340)]
    // 右と上にはみ出す
    [InlineData(1700, -200, 400, 300, 400, 300, 1520, 0)]
    // 作業領域より大きいなら左上に合わせる
    [InlineData(0, 0, 800, 600, 2000, 1200, 0, 0)]
    public void 最終の大きさで親の中央に置き作業領域に収める(int ox, int oy, int ow, int oh, int w, int h, int x, int y)
    {
        var area = new System.Drawing.Rectangle(0, 0, 1920, 1040);
        Assert.Equal(new System.Drawing.Point(x, y),
            DialogPlacement.Place(new System.Drawing.Rectangle(ox, oy, ow, oh), new System.Drawing.Size(w, h), area));
    }
}
