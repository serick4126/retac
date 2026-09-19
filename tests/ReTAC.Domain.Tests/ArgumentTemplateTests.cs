using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

/// <summary>F-02: 引数の文字列の解析</summary>
public class ArgumentTemplateTests
{
    private static MacroPart OnlyMacro(ArgumentTemplate template) =>
        Assert.IsType<MacroPart>(Assert.Single(Assert.Single(template.Arguments).Parts));

    [Fact]
    public void 空なら引数は無い()
    {
        var template = ArgumentTemplate.Parse("");
        Assert.True(template.IsValid);
        Assert.Empty(template.Arguments);
    }

    [Fact]
    public void 固定の引数は半角空白で分かれる()
    {
        var template = ArgumentTemplate.Parse("/b  -x");
        Assert.Equal(2, template.Arguments.Count);
        Assert.Equal("/b", Assert.IsType<LiteralPart>(Assert.Single(template.Arguments[0].Parts)).Text);
    }

    [Fact]
    public void 全角空白では区切らない()
    {
        // Windows のコマンドラインの区切りは半角空白とタブだけ
        var template = ArgumentTemplate.Parse("a　b");
        Assert.Equal("a　b", Assert.IsType<LiteralPart>(Assert.Single(Assert.Single(template.Arguments).Parts)).Text);
    }

    [Fact]
    public void 引用符で囲めば空白を含められ囲んだことを覚えている()
    {
        var template = ArgumentTemplate.Parse("\"a b\" c");
        Assert.Equal(2, template.Arguments.Count);
        Assert.True(template.Arguments[0].Quoted);
        Assert.False(template.Arguments[1].Quoted);
        Assert.Equal("a b", Assert.IsType<LiteralPart>(Assert.Single(template.Arguments[0].Parts)).Text);
    }

    [Fact]
    public void 引用符だけの引数は部品を持たない()
    {
        var argument = Assert.Single(ArgumentTemplate.Parse("\"\"").Arguments);
        Assert.True(argument.Quoted);
        Assert.Empty(argument.Parts);
    }

    [Theory]
    [InlineData("${file}", MacroName.File)]
    [InlineData("${path}", MacroName.Path)]
    [InlineData("${fileBasenameNoExtension}", MacroName.FileBasenameNoExtension)]
    [InlineData("${fileExtname}", MacroName.FileExtname)]
    [InlineData("${cursorFile}", MacroName.CursorFile)]
    [InlineData("${cursorPath}", MacroName.CursorPath)]
    [InlineData("${cwd}", MacroName.Cwd)]
    public void マクロを認識する(string text, MacroName expected)
    {
        var macro = OnlyMacro(ArgumentTemplate.Parse(text));
        Assert.Equal(expected, macro.Name);
        Assert.False(macro.Required);
    }

    [Fact]
    public void 直後のビックリマークは必須の印()
    {
        Assert.True(OnlyMacro(ArgumentTemplate.Parse("${file}!")).Required);
    }

    [Fact]
    public void 前後の文字とマクロを1つの引数に並べられる()
    {
        var parts = Assert.Single(ArgumentTemplate.Parse("--out=${fileBasenameNoExtension}.mp4").Arguments).Parts;
        Assert.Equal(3, parts.Count);
        Assert.Equal("--out=", Assert.IsType<LiteralPart>(parts[0]).Text);
        Assert.Equal(MacroName.FileBasenameNoExtension, Assert.IsType<MacroPart>(parts[1]).Name);
        Assert.Equal(".mp4", Assert.IsType<LiteralPart>(parts[2]).Text);
    }

    [Theory]
    [InlineData("$$", "$")]
    [InlineData("$${env:PATH}", "${env:PATH}")]
    [InlineData("$env:PATH", "$env:PATH")]
    public void ドル記号の書き方(string text, string expected)
    {
        var template = ArgumentTemplate.Parse(text);
        Assert.True(template.IsValid);
        Assert.Equal(expected, Assert.IsType<LiteralPart>(Assert.Single(Assert.Single(template.Arguments).Parts)).Text);
    }

    [Theory]
    [InlineData("${File}")]          // 大文字・小文字を区別する
    [InlineData("${git:root}")]      // 将来追加され得るマクロ。今は知らない名前
    [InlineData("${file:x}")]        // prompt 以外に「:」は付かない
    [InlineData("${file")]           // 閉じていない
    [InlineData("\"abc")]            // 引用符が閉じていない
    [InlineData("${prompt:a}{b")]    // 既定値が閉じていない
    [InlineData("${path}${file}")]   // 1 つの引数に ${path} と ${file} 系を混ぜない
    public void 誤りを見つける(string text)
    {
        var template = ArgumentTemplate.Parse(text);
        Assert.False(template.IsValid);
        Assert.NotEmpty(template.Errors[0].Message);
    }

    [Fact]
    public void 別の引数なら_pathと_fileを並べられる()
    {
        Assert.True(ArgumentTemplate.Parse("${path} ${file}").IsValid);
    }

    [Fact]
    public void 入力ダイアログのタイトルと既定値()
    {
        var template = ArgumentTemplate.Parse("${prompt:検索}{*.txt}!");
        var macro = OnlyMacro(template);
        Assert.Equal(MacroName.Prompt, macro.Name);
        Assert.True(macro.Required);
        Assert.Equal(0, macro.PromptIndex);
        Assert.Equal(new PromptRequest("検索", "*.txt"), Assert.Single(template.Prompts));
    }

    [Theory]
    [InlineData("${prompt}", "", "")]
    [InlineData("${prompt:}", "", "")]
    [InlineData("${prompt:}{b}", "", "b")]
    [InlineData("${prompt:a}{\"x y\"}", "a", "\"x y\"")]   // 括弧の中の引用符は文字どおり
    public void 入力ダイアログは省略できる(string text, string title, string defaultValue)
    {
        var template = ArgumentTemplate.Parse(text);
        Assert.True(template.IsValid);
        Assert.Equal(new PromptRequest(title, defaultValue), Assert.Single(template.Prompts));
    }

    [Fact]
    public void 入力ダイアログは左から順に番号が付く()
    {
        var template = ArgumentTemplate.Parse("${prompt:a} ${prompt:b}");
        Assert.Equal(["a", "b"], template.Prompts.Select(p => p.Title));
        Assert.Equal(1, Assert.IsType<MacroPart>(Assert.Single(template.Arguments[1].Parts)).PromptIndex);
    }
}
