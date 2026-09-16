using ReTAC.Domain.Commands;

namespace ReTAC.Domain.Tests;

/// <summary>F-06: キーやメニューが指す先と、設定ファイルでの書き方</summary>
public class CommandTargetTests
{
    [Fact]
    public void 組み込みのコマンドを読み書きできる()
    {
        Assert.Equal(new BuiltinTarget(CommandId.OpenFile), CommandTarget.Parse("OpenFile"));
        Assert.Equal("OpenFile", new BuiltinTarget(CommandId.OpenFile).Serialize());
    }

    [Fact]
    public void 外部ツールを読み書きできる()
    {
        Assert.Equal(new ToolTarget(3), CommandTarget.Parse("Tool:3"));
        Assert.Equal("Tool:3", new ToolTarget(3).Serialize());
    }

    [Fact]
    public void 種類が違えば等しくない()
    {
        CommandTarget tool = new ToolTarget(1);
        Assert.False(tool == new BuiltinTarget(CommandId.OpenFile));
        Assert.True(tool == new ToolTarget(1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("65")]          // V-15: 数値文字列を Enum.TryParse に通さない
    [InlineData("-1")]
    [InlineData("NoSuchCommand")]
    [InlineData("Tool:")]
    [InlineData("Tool:0")]
    [InlineData("Tool:x")]
    [InlineData("Tool:+1")]
    public void 読めない文字は割り当てなし(string text)
    {
        Assert.Null(CommandTarget.Parse(text));
    }
}
