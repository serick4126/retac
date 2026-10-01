using ReTAC.App;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

/// <summary>R-134: 変換の候補を調べている間に選択が変わったとき、古い行を変換しない</summary>
public class ConvertRequestGateTests
{
    [Fact]
    public void 待っている間に新しい要求が始まったら古い要求は最新でない()
    {
        var gate = new ConvertRequestGate();
        var first = gate.Begin();
        Assert.True(gate.IsCurrent(first));
        var second = gate.Begin();
        Assert.False(gate.IsCurrent(first));
        Assert.True(gate.IsCurrent(second));
    }

    [Fact]
    public void 逆順に終わっても最新だけが最新()
    {
        var gate = new ConvertRequestGate();
        var a = gate.Begin();
        var b = gate.Begin();
        var c = gate.Begin();
        Assert.False(gate.IsCurrent(a));
        Assert.False(gate.IsCurrent(b));
        Assert.True(gate.IsCurrent(c));
    }

    [Fact]
    public void 同じ値の別の行は参照で見分ける()
    {
        var first = PromptArgument.Fixed("x");
        var second = PromptArgument.Fixed("x");
        Assert.Equal(first, second);   // record の値は等しい
        IReadOnlyList<PromptArgument> rows = [first, second];
        Assert.Equal(0, ConvertRequestGate.IndexOfSame(rows, first));
        Assert.Equal(1, ConvertRequestGate.IndexOfSame(rows, second));
    }

    [Fact]
    public void 行が無くなっていれば見つからない()
    {
        var target = PromptArgument.Fixed("x");
        IReadOnlyList<PromptArgument> rows = [PromptArgument.Fixed("x")];
        Assert.Equal(-1, ConvertRequestGate.IndexOfSame(rows, target));
    }
}
