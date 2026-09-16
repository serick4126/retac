using ReTAC.Domain.Commands;
using ReTAC.Domain.Keys;
using ReTAC.Domain.Tools;

namespace ReTAC.Domain.Tests;

/// <summary>キー割り当ての解決と、外部ツール参照の整合（R-12 / F-01）。</summary>
public class KeyMapToolRefTests
{
    private static readonly KeyBinding Slot = new(Vk.Letter('E'));

    [Fact]
    public void 存在しないツールを指す割り当ては落とす()
    {
        var map = new KeyMap([]);
        map.Assign(Slot, new ToolTarget(99));

        map.DropUnknownTools([DefaultExternalTools.EditorId]);

        Assert.Null(map.Resolve(Slot));
    }

    [Fact]
    public void 存在するツールを指す割り当ては残す()
    {
        var map = new KeyMap([]);
        map.Assign(Slot, new ToolTarget(DefaultExternalTools.EditorId));

        map.DropUnknownTools([DefaultExternalTools.EditorId]);

        Assert.Equal(new ToolTarget(DefaultExternalTools.EditorId), map.Resolve(Slot));
    }

    [Fact]
    public void 組み込みコマンドの割り当ては触らない()
    {
        var map = new KeyMap([]);
        map.Assign(Slot, new BuiltinTarget(CommandId.Rename));

        map.DropUnknownTools([]);

        Assert.Equal(new BuiltinTarget(CommandId.Rename), map.Resolve(Slot));
    }

    [Fact]
    public void ツールを一つも持たないなら既定のツールキーも残らない()
    {
        var map = DefaultKeyMap.Create();

        map.DropUnknownTools([]);

        Assert.DoesNotContain(map.Bindings.Values, t => t is ToolTarget);
    }
}
