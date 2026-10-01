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
    public void プロンプトはそれだけで一つの引数として書ける()
    {
        var template = ArgumentTemplate.Parse("x ${prompt} ${file}");
        Assert.True(template.IsValid);
        Assert.True(template.HasPrompt);
        Assert.False(ArgumentTemplate.Parse("${file}").HasPrompt);
    }

    [Theory]
    [InlineData("${prompt:検索}", "「${prompt:…}」の書き方は使えません。入力ダイアログ編集で設定してください。")]
    [InlineData("${prompt}{*.txt}", "${prompt} の後ろに既定値は書けません。入力ダイアログ編集で設定してください。")]
    [InlineData("${prompt}!", "${prompt} に「!」は付けられません。必須は入力ダイアログ編集で設定してください。")]
    [InlineData("${prompt} ${prompt}", "${prompt} は 1 つだけ書けます。")]
    [InlineData("-m${prompt}", "${prompt} は、前後に文字をつなげず、引用符で囲まずに、それだけで 1 つの引数として書いてください。")]
    [InlineData("${prompt}x", "${prompt} は、前後に文字をつなげず、引用符で囲まずに、それだけで 1 つの引数として書いてください。")]
    [InlineData("\"${prompt}\"", "${prompt} は、前後に文字をつなげず、引用符で囲まずに、それだけで 1 つの引数として書いてください。")]
    public void プロンプトの書き方の誤り(string text, string message)
    {
        var template = ArgumentTemplate.Parse(text);
        Assert.Contains(message, template.Errors.Select(e => e.Message));
    }

    [Fact]
    public void ほかのマクロのコロンは今までどおり誤り()
    {
        Assert.Contains("「${file}」には「:」を付けられません。", ArgumentTemplate.Parse("${file:x}").Errors.Select(e => e.Message));
    }
}
