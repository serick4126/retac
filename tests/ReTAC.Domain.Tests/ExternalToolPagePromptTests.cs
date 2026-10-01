using ReTAC.App;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

/// <summary>R-133: 設定ページの入口（${prompt} の挿入）とマクロの一覧</summary>
public class ExternalToolPagePromptTests
{
    [Theory]
    [InlineData("", 0, 0, "${prompt}", 9)]
    [InlineData("x", 1, 0, "x ${prompt}", 11)]
    [InlineData("x y", 1, 0, "x ${prompt} y", 11)]
    [InlineData("xy", 1, 0, "x ${prompt} y", 11)]
    [InlineData("a OLD b", 2, 3, "a ${prompt} b", 11)]
    [InlineData("a\t", 2, 0, "a\t${prompt}", 11)]
    public void プロンプトは前後の文字とつながらないように挿入する(string text, int start, int length, string expected, int caret)
    {
        var (result, at) = ExternalToolPage.WithPrompt(text, start, length)!.Value;
        Assert.Equal(expected, result);
        Assert.Equal(caret, at);
        Assert.True(ArgumentTemplate.Parse(result).IsValid);
    }

    [Theory]
    [InlineData("\"a b\"", 2, 0)]          // 引用符の中
    [InlineData("\"a b\"", 1, 0)]          // 開きの引用符の直後
    [InlineData("\"a\" b", 1, 3)]          // 選んでいる範囲が引用符を含む
    [InlineData("x \"a", 4, 0)]            // 閉じていない引用符の中
    public void 引用符の中には挿入しない(string text, int start, int length)
    {
        Assert.Null(ExternalToolPage.WithPrompt(text, start, length));
    }

    [Fact]
    public void 閉じた引用符の後ろには挿入できる()
    {
        var (result, _) = ExternalToolPage.WithPrompt("\"a\" b", 3, 0)!.Value;
        Assert.Equal("\"a\" ${prompt} b", result);
        Assert.True(ArgumentTemplate.Parse(result).IsValid);
    }

    [Fact]
    public void 引数欄にプロンプトが無ければ古い定義では開かない()
    {
        var tool = new ExternalTool { Id = 5, Arguments = "${prompt}", Prompt = PromptDefinition.Simple("x") };
        Assert.Same(tool.Prompt, ExternalToolPage.DefinitionToEdit(tool, "a ${prompt}"));
        Assert.Null(ExternalToolPage.DefinitionToEdit(tool, "a b"));                  // 引数欄から消した直後（まだ下書きに定義が残っている）
        Assert.Null(ExternalToolPage.DefinitionToEdit(tool with { Prompt = null }, "${prompt}"));
    }

    [Fact]
    public void 項目の番号が重なった定義でもヘルパーを開ける()
    {
        var item = new PromptItem { Id = 1, Kind = PromptItemKind.Text, Label = "a" };
        var broken = new PromptDefinition { Items = [item, item with { Label = "b" }], Arguments = [PromptArgument.Item(1)] };
        using var dialog = new PromptSettingsDialog(broken, "x", new ReTAC.Domain.Navigation.FolderHistory(), null, @"C:\work");
        Assert.Contains("項目の番号が重なっています。", PromptDefinitionRules.Validate(broken).Select(p => p.Message));   // OK で知らせる
    }

    [Fact]
    public void マクロを含む引数の行に入れるマクロの一覧にはプロンプトを出さない()
    {
        using var all = new MacroReferenceDialog();
        using var without = new MacroReferenceDialog(excludePrompt: true);
        Assert.Contains("${prompt}", all.Inserts);
        Assert.DoesNotContain("${prompt}", without.Inserts);
        Assert.Contains("${file}", without.Inserts);
    }
}
